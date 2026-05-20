// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Engine.Credentials.Sources;

/// <summary>
/// Register / decentralised-identifier trust source (feature 135). Vouches for an issuer when
/// its identifier resolves in the directory and the signing key is currently authorised in
/// assertionMethod (rotated/revoked keys are dropped from assertionMethod — Feature 120).
/// </summary>
public class RegisterTrustSourceResolver(IIssuerDirectory directory) : ITrustSourceResolver
{
    private readonly IIssuerDirectory _directory = directory ?? throw new ArgumentNullException(nameof(directory));

    /// <inheritdoc />
    public TrustSourceKind Kind => TrustSourceKind.Register;

    /// <inheritdoc />
    public async Task<TrustSourceVouch> VouchAsync(IssuerContext issuer, TrustSourceRef source, CancellationToken cancellationToken = default)
    {
        var entry = await _directory.LookupAsync(issuer.IssuerId, cancellationToken).ConfigureAwait(false);
        if (!entry.Resolved)
            return TrustSourceVouch.Decline(TrustFailureReason.UntrustedIssuer);

        // When the credential names the signing key, it MUST be in assertionMethod. If the
        // directory lists assertionMethod keys but the signing key is not among them, the key
        // has been rotated out / revoked — decline.
        if (!string.IsNullOrEmpty(issuer.SigningKeyId)
            && entry.AssertionMethodKeyIds.Count > 0
            && !entry.AssertionMethodKeyIds.Contains(issuer.SigningKeyId, StringComparer.Ordinal))
        {
            return TrustSourceVouch.Decline(TrustFailureReason.UntrustedIssuer);
        }

        return new TrustSourceVouch
        {
            Vouched = true,
            Assurance = source.ConfersAssurance ?? AssuranceLevel.Low,
            ApplyEvidence = e => e.RegisterHeight = entry.RegisterHeight
        };
    }
}
