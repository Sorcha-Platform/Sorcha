// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;
using System.Text;

using Grpc.Core;
using Grpc.Core.Interceptors;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Sorcha.Validator.Service.GrpcServices;

/// <summary>
/// gRPC server interceptor for the validator's peer surface. Modelled on
/// <c>Sorcha.Peer.Service.GrpcServices.PeerAuthInterceptor</c> — same opportunistic-auth shape
/// (validate a token when one is presented, otherwise continue as a lower-trust caller) — and then
/// adds the half that was missing platform-wide: it <b>acts</b> on that classification, refusing
/// methods that are not federation-reachable per <see cref="ValidatorGrpcAccessPolicy"/>.
///
/// <para>Before this, <c>MapGrpcService&lt;ValidatorGrpcService&gt;()</c> carried no authorization at
/// all while every REST group in the same <c>Program.cs</c> did, and the gRPC port is published
/// (<c>5801:8081</c>). Peer's interceptor did classify callers, but nothing anywhere consumed its
/// <c>IsAuthenticatedKey</c> / node-identity flags, so the "lower trust" half of FR-014 was inert.
/// </para>
/// </summary>
public sealed class ValidatorGrpcAccessInterceptor : Interceptor
{
    /// <summary>Context key indicating whether the calling peer presented a valid token.</summary>
    public const string IsAuthenticatedKey = "validator-is-authenticated";

    /// <summary>Context key for the authenticated caller's subject, when present.</summary>
    public const string AuthenticatedCallerIdKey = "validator-authenticated-caller-id";

    /// <summary>
    /// Context key for the caller's node-identity certificate thumbprint (Feature 175), captured
    /// from the mTLS client certificate when one was presented. Installation-neutral: unlike a JWT,
    /// this identifies a federated node regardless of which installation issued its tokens.
    /// </summary>
    public const string NodeIdentityThumbprintKey = "validator-node-identity-thumbprint";

    private readonly ILogger<ValidatorGrpcAccessInterceptor> _logger;
    private readonly TokenValidationParameters? _validationParameters;

    /// <summary>DI-friendly constructor.</summary>
    public ValidatorGrpcAccessInterceptor(
        ILogger<ValidatorGrpcAccessInterceptor> logger,
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
                // Matches PeerAuthInterceptor: peer-to-peer traffic may not carry an audience, and
                // requiring this installation's audience would refuse every federated caller.
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(
                    configuration.GetValue("JwtSettings:ClockSkewMinutes", 2))
            };
        }
    }

    /// <inheritdoc />
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        return await continuation(request, context);
    }

    /// <inheritdoc />
    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        return await continuation(requestStream, context);
    }

    /// <inheritdoc />
    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        await continuation(request, responseStream, context);
    }

    /// <inheritdoc />
    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        await continuation(requestStream, responseStream, context);
    }

    /// <summary>
    /// Classifies the caller, then refuses the call when the method is not federation-reachable and
    /// the caller is unauthenticated. Throws <see cref="RpcException"/> with
    /// <see cref="StatusCode.Unauthenticated"/> — the call never reaches the service.
    /// </summary>
    private void Authorize(ServerCallContext context)
    {
        var isAuthenticated = Classify(context);

        if (isAuthenticated || ValidatorGrpcAccessPolicy.IsFederationReachable(context.Method))
        {
            return;
        }

        var reason = ValidatorGrpcAccessPolicy.AuthenticatedOnlyReason(context.Method)
                     ?? "not federation-reachable (unclassified methods are private by default)";

        _logger.LogWarning(
            "SEC-AUDIT: refused unauthenticated gRPC call to {Method} from {Peer} — {Reason}",
            context.Method, context.Peer, reason);

        throw new RpcException(new Status(
            StatusCode.Unauthenticated,
            "This validator method requires an authenticated caller."));
    }

    /// <summary>
    /// Captures the node-identity thumbprint and validates a bearer token if one is present.
    /// Returns whether the caller is authenticated. Never throws for a bad token — an invalid or
    /// expired token degrades to anonymous, exactly as <c>PeerAuthInterceptor</c> does, so a
    /// federated peer with a stale token still reaches the federation surface.
    /// </summary>
    private bool Classify(ServerCallContext context)
    {
        var clientCertificate = context.GetHttpContext()?.Connection.ClientCertificate;
        if (clientCertificate is not null)
        {
            context.UserState[NodeIdentityThumbprintKey] = clientCertificate.Thumbprint;
        }

        var authHeader = context.RequestHeaders.GetValue("authorization");

        if (string.IsNullOrEmpty(authHeader) || _validationParameters is null)
        {
            context.UserState[IsAuthenticatedKey] = false;
            return false;
        }

        var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader["Bearer ".Length..].Trim()
            : authHeader;

        try
        {
            var principal = new JwtSecurityTokenHandler()
                .ValidateToken(token, _validationParameters, out _);

            var callerId = principal.FindFirst("sub")?.Value
                        ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            context.UserState[IsAuthenticatedKey] = true;
            context.UserState[AuthenticatedCallerIdKey] = callerId ?? "unknown";
            return true;
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogWarning("Validator gRPC call presented an expired token — treating as anonymous");
            context.UserState[IsAuthenticatedKey] = false;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Validator gRPC call presented an invalid token — treating as anonymous");
            context.UserState[IsAuthenticatedKey] = false;
            return false;
        }
    }
}
