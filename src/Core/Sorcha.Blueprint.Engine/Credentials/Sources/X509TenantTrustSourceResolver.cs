// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography.X509Certificates;

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Engine.Credentials.Sources;

/// <summary>
/// Tenant X.509 trust source (feature 135). Vouches when the credential's x5c chain validates
/// to a trusted tenant root (with CRL when enabled). Lifts the chain-build logic that used to
/// live ad hoc in the HAIP verifier into a reusable, evaluator-driven source.
/// </summary>
public class X509TenantTrustSourceResolver(ITenantTrustAnchorProvider anchorProvider) : ITrustSourceResolver
{
    private readonly ITenantTrustAnchorProvider _anchorProvider =
        anchorProvider ?? throw new ArgumentNullException(nameof(anchorProvider));

    /// <inheritdoc />
    public virtual TrustSourceKind Kind => TrustSourceKind.X509Tenant;

    /// <summary>The anchor identifier to request; null = the tenant default. Overridden by the trustlist source.</summary>
    protected virtual string? AnchorId(TrustSourceRef source) => null;

    /// <inheritdoc />
    public async Task<TrustSourceVouch> VouchAsync(IssuerContext issuer, TrustSourceRef source, CancellationToken cancellationToken = default)
    {
        if (issuer.X5cChain is null || issuer.X5cChain.Count == 0)
            return TrustSourceVouch.Decline(TrustFailureReason.ChainInvalid);

        var anchors = await _anchorProvider.GetAnchorsAsync(AnchorId(source), cancellationToken).ConfigureAwait(false);
        if (anchors is null || anchors.Roots.Count == 0)
            return TrustSourceVouch.Decline(TrustFailureReason.SourceUnavailable);

        var certs = new List<X509Certificate2>();
        var roots = new List<X509Certificate2>();
        try
        {
            foreach (var der in issuer.X5cChain)
                certs.Add(X509CertificateLoader.LoadCertificate(der));
            foreach (var der in anchors.Roots)
                roots.Add(X509CertificateLoader.LoadCertificate(der));

            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.RevocationMode = anchors.CheckRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck;
            if (anchors.CheckRevocation)
            {
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(30);
            }
            foreach (var root in roots)
                chain.ChainPolicy.CustomTrustStore.Add(root);
            for (int i = 1; i < certs.Count; i++)
                chain.ChainPolicy.ExtraStore.Add(certs[i]);

            if (!chain.Build(certs[0]))
                return TrustSourceVouch.Decline(TrustFailureReason.ChainInvalid);

            return new TrustSourceVouch
            {
                Vouched = true,
                Assurance = source.ConfersAssurance ?? AssuranceLevel.Substantial,
                ApplyEvidence = e =>
                {
                    e.CrlVersion = anchors.CrlVersion;
                    e.TrustListId = anchors.AnchorSetId;
                    e.TrustListFreshness = anchors.Freshness;
                }
            };
        }
        finally
        {
            foreach (var c in certs) c.Dispose();
            foreach (var r in roots) r.Dispose();
        }
    }
}
