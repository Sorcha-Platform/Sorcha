// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Auth;

/// <summary>
/// Client for token introspection via the Tenant Service (RFC 7662).
/// Services use this to verify whether a token is active and retrieve its claims.
/// </summary>
public interface ITokenIntrospectionClient
{
    /// <summary>
    /// Introspects a token to determine if it is active and retrieve its claims.
    /// </summary>
    /// <param name="token">The token to introspect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The introspection result, or null if the request fails.</returns>
    Task<TokenIntrospectionResult?> IntrospectAsync(
        string token,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a token introspection request per RFC 7662.
/// </summary>
public class TokenIntrospectionResult
{
    /// <summary>
    /// Whether the token is currently active.
    /// </summary>
    public bool Active { get; init; }

    /// <summary>
    /// The subject (user or service ID) of the token.
    /// </summary>
    public string? Sub { get; init; }

    /// <summary>
    /// The token type (user or service).
    /// </summary>
    public string? TokenType { get; init; }

    /// <summary>
    /// Scopes associated with the token.
    /// </summary>
    public string? Scope { get; init; }

    /// <summary>
    /// The client (service) that requested the token.
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// Token expiration time (Unix timestamp).
    /// </summary>
    public long? Exp { get; init; }

    /// <summary>
    /// Token issued-at time (Unix timestamp).
    /// </summary>
    public long? Iat { get; init; }
}
