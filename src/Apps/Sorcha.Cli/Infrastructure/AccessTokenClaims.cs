// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;

using Sorcha.ServiceClients.Auth;

namespace Sorcha.Cli.Infrastructure;

/// <summary>
/// Reads claims out of the CLI's cached access token.
/// </summary>
/// <remarks>
/// Claim names come from the shared <see cref="TokenClaimConstants"/> rather than bare string
/// literals, so a claim rename in the token issuer surfaces here at compile time instead of as a
/// silent null at runtime.
/// </remarks>
public static class AccessTokenClaims
{
    /// <summary>
    /// Extracts the caller's organisation id (<c>org_id</c>) from a JWT access token.
    /// </summary>
    /// <param name="accessToken">The raw JWT. May be null or malformed.</param>
    /// <returns>The organisation id, or <see langword="null"/> if absent or unreadable.</returns>
    public static string? TryGetOrgId(string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
            return jwt.Claims.FirstOrDefault(c => c.Type == TokenClaimConstants.OrgId)?.Value;
        }
        catch
        {
            // A malformed or non-JWT token is a "cannot determine org" answer, not a crash.
            return null;
        }
    }
}
