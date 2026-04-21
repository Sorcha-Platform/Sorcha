// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Sorcha.Testing;

/// <summary>
/// Per-service configuration for <see cref="TestAuthHandler"/>.
/// </summary>
public class TestAuthHandlerOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// Claims emitted by default for every authenticated request.
    /// </summary>
    public IList<Claim> DefaultClaims { get; set; } = new List<Claim>();

    /// <summary>
    /// Role added to the principal when no X-Test-Role header is present.
    /// Set to null or empty to add no default role.
    /// </summary>
    public string? DefaultRole { get; set; }

    /// <summary>
    /// Additional header-to-claim mappings beyond the built-in standard set.
    /// Each (header, claimType) pair lifts the header value (when present) into a claim.
    /// </summary>
    public IList<(string Header, string ClaimType)> HeaderClaimMappings { get; set; }
        = new List<(string, string)>();

    /// <summary>
    /// When true (default), requests without an Authorization header return
    /// AuthenticateResult.NoResult() so endpoints allowing anonymous access
    /// still work. When false, the handler issues a ticket for every request.
    /// Useful for test projects whose helpers don't set bearer tokens.
    /// </summary>
    public bool RequireAuthorizationHeader { get; set; } = true;
}
