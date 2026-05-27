// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Did;

namespace Sorcha.Wallet.Service.Credentials;

/// <summary>
/// Issuer-allowlist equivalence helper (Feature 120 US5 / FR-014).
/// </summary>
/// <remarks>
/// <para>Three-step match per <c>contracts/did-resolver-registry-contract.md</c>
/// "Consumer expectations":</para>
/// <list type="number">
///   <item>Direct-string match against the allowlist (existing behaviour).</item>
///   <item>The credential's issuer cross-resolves to a document whose <c>alsoKnownAs</c>
///   contains an allowlist entry.</item>
///   <item>An allowlist entry cross-resolves to a document whose <c>alsoKnownAs</c>
///   contains the credential's issuer.</item>
/// </list>
/// <para>Short-circuits on first match. When <paramref name="registry"/> is null, only
/// step 1 runs (sync direct-string match) — which preserves the pre-Feature-120
/// behaviour for callers that have not yet been wired to the registry.</para>
/// </remarks>
public static class IssuerEquivalenceMatcher
{
    /// <summary>
    /// Returns true iff <paramref name="credentialIssuer"/> is accepted by
    /// <paramref name="acceptedIssuers"/>, honouring DID equivalence via the
    /// supplied <paramref name="registry"/>.
    /// </summary>
    public static async Task<bool> IsAcceptedAsync(
        string credentialIssuer,
        IReadOnlyList<string>? acceptedIssuers,
        IDidResolverRegistry? registry,
        CancellationToken ct = default)
    {
        // No allowlist → any issuer accepted (caller's policy).
        if (acceptedIssuers is null || acceptedIssuers.Count == 0) return true;

        // Step 1 — direct-string match.
        if (acceptedIssuers.Contains(credentialIssuer, StringComparer.Ordinal)) return true;

        // Without a registry, equivalence-aware steps cannot run.
        if (registry is null) return false;

        // Step 2 — credential issuer's alsoKnownAs contains an allowlist entry.
        var credentialDoc = await registry.ResolveWithAlsoKnownAsAsync(credentialIssuer, ct).ConfigureAwait(false);
        if (credentialDoc?.AlsoKnownAs is { Count: > 0 } credentialAka)
        {
            foreach (var allowed in acceptedIssuers)
            {
                if (credentialAka.Contains(allowed, StringComparer.Ordinal)) return true;
            }
        }

        // Step 3 — any allowlist entry's alsoKnownAs contains the credential's issuer.
        foreach (var allowed in acceptedIssuers)
        {
            var allowedDoc = await registry.ResolveWithAlsoKnownAsAsync(allowed, ct).ConfigureAwait(false);
            if (allowedDoc?.AlsoKnownAs?.Contains(credentialIssuer, StringComparer.Ordinal) == true)
                return true;
        }

        return false;
    }
}
