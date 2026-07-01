// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Sorcha.Peer.Service.GrpcServices;

/// <summary>
/// gRPC server interceptor that extracts and validates JWT tokens from the authorization
/// metadata key. Authenticated peers receive an "authenticated" flag in the call context;
/// anonymous peers are allowed through with a lower-trust flag (FR-014).
/// </summary>
public class PeerAuthInterceptor : Interceptor
{
    private readonly ILogger<PeerAuthInterceptor> _logger;
    private readonly TokenValidationParameters? _validationParameters;

    /// <summary>
    /// Context key indicating whether the calling peer is authenticated.
    /// </summary>
    public const string IsAuthenticatedKey = "peer-is-authenticated";

    /// <summary>
    /// Context key for the authenticated peer's service name (if present).
    /// </summary>
    public const string AuthenticatedPeerIdKey = "peer-authenticated-id";

    /// <summary>
    /// Context key for the calling peer's node-identity certificate thumbprint (Feature 175), captured
    /// from the mTLS client certificate when the peer presented one. Installation-neutral: this is the
    /// stable federation identity of the caller, independent of any installation-scoped JWT.
    /// </summary>
    public const string NodeIdentityThumbprintKey = "peer-node-identity-thumbprint";

    public PeerAuthInterceptor(
        ILogger<PeerAuthInterceptor> logger,
        IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var signingKey = configuration["JwtSettings:SigningKey"];
        if (!string.IsNullOrEmpty(signingKey))
        {
            var keyBytes = Encoding.UTF8.GetBytes(signingKey);
            if (keyBytes.Length < 32) Array.Resize(ref keyBytes, 32);

            _validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                ValidateIssuer = configuration.GetValue("JwtSettings:ValidateIssuer", true),
                ValidIssuer = configuration["JwtSettings:Issuer"],
                ValidateAudience = false, // Peer-to-peer traffic may not have audience set
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(
                    configuration.GetValue("JwtSettings:ClockSkewMinutes", 2))
            };
        }
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        SetAuthContext(context);
        return await continuation(request, context);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        SetAuthContext(context);
        await continuation(request, responseStream, context);
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        SetAuthContext(context);
        return await continuation(requestStream, context);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        SetAuthContext(context);
        await continuation(requestStream, responseStream, context);
    }

    private void SetAuthContext(ServerCallContext context)
    {
        // Feature 175: capture the caller's node-identity certificate thumbprint from the mTLS client
        // certificate (present only when the peer endpoint runs TLS and the peer presented a cert).
        var clientCertificate = context.GetHttpContext()?.Connection.ClientCertificate;
        if (clientCertificate is not null)
        {
            context.UserState[NodeIdentityThumbprintKey] = clientCertificate.Thumbprint;
            _logger.LogDebug(
                "gRPC call presented node identity certificate {Thumbprint}", clientCertificate.Thumbprint);
        }

        var authHeader = context.RequestHeaders.GetValue("authorization");

        if (string.IsNullOrEmpty(authHeader) || _validationParameters is null)
        {
            // Anonymous peer — allowed through with lower trust
            context.UserState[IsAuthenticatedKey] = false;
            _logger.LogDebug("gRPC call from anonymous peer (no authorization header)");
            return;
        }

        var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader["Bearer ".Length..].Trim()
            : authHeader;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, _validationParameters, out _);

            var peerId = principal.FindFirst("sub")?.Value
                      ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            context.UserState[IsAuthenticatedKey] = true;
            context.UserState[AuthenticatedPeerIdKey] = peerId ?? "unknown";

            _logger.LogDebug("gRPC call from authenticated peer {PeerId}", peerId);
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogWarning("gRPC call with expired token — treating as anonymous");
            context.UserState[IsAuthenticatedKey] = false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "gRPC token validation failed — treating as anonymous");
            context.UserState[IsAuthenticatedKey] = false;
        }
    }
}
