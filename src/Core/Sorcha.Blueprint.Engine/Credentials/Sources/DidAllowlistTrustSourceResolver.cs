// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Engine.Credentials.Sources;

/// <summary>
/// Explicit DID-allowlist trust source (feature 135). Vouches when the issuer is listed in the
/// source's <see cref="TrustSourceRef.AllowedIssuers"/> directly, or is alsoKnownAs-equivalent
/// to a listed identifier. The equivalence lookup goes through <see cref="IIssuerDirectory"/>
/// (replacing the wallet-only <c>IssuerEquivalenceMatcher</c> in the unified path).
/// </summary>
public class DidAllowlistTrustSourceResolver(IIssuerDirectory directory) : ITrustSourceResolver
{
    private readonly IIssuerDirectory _directory = directory ?? throw new ArgumentNullException(nameof(directory));

    /// <inheritdoc />
    public TrustSourceKind Kind => TrustSourceKind.DidAllowlist;

    /// <inheritdoc />
    public async Task<TrustSourceVouch> VouchAsync(IssuerContext issuer, TrustSourceRef source, CancellationToken cancellationToken = default)
    {
        var allowed = source.AllowedIssuers;
        if (allowed is null || allowed.Count == 0)
            return TrustSourceVouch.Decline(TrustFailureReason.UntrustedIssuer);

        // Direct match.
        if (allowed.Contains(issuer.IssuerId, StringComparer.Ordinal))
            return Vouch(source);

        // Equivalence match — the issuer's alsoKnownAs set intersects the allowlist.
        var entry = await _directory.LookupAsync(issuer.IssuerId, cancellationToken).ConfigureAwait(false);
        if (entry.Resolved && entry.AlsoKnownAs.Any(aka => allowed.Contains(aka, StringComparer.Ordinal)))
            return Vouch(source);

        return TrustSourceVouch.Decline(TrustFailureReason.UntrustedIssuer);
    }

    private static TrustSourceVouch Vouch(TrustSourceRef source) => new()
    {
        Vouched = true,
        Assurance = source.ConfersAssurance ?? AssuranceLevel.Low
    };
}
