// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Sorcha.UI.Components.User.Services.Verification;

namespace Sorcha.Verifier.Services;

/// <summary>
/// Desk-verifier implementation of <see cref="IVerifierIdentityProvider"/> (Feature 164, B3 US3).
/// Returns a stable <c>did:sorcha:verifier:{orgId}</c> as the OID4VP <c>client_id</c>; the org id
/// is read from <c>Verifier:OrgId</c> configuration (defaults to "demo" when absent for the
/// reference verifier). Stable across sessions — unlike the PWA's ephemeral P-256 identity.
/// </summary>
public sealed class StableOrgVerifierIdentityProvider : IVerifierIdentityProvider
{
    private readonly string _clientId;

    /// <summary>Initialises the provider from configuration.</summary>
    public StableOrgVerifierIdentityProvider(IConfiguration configuration)
    {
        var orgId = configuration["Verifier:OrgId"] ?? "demo";
        _clientId = $"did:sorcha:verifier:{orgId}";
    }

    /// <inheritdoc />
    public Task<string> GetClientIdAsync(CancellationToken ct = default)
        => Task.FromResult(_clientId);
}
