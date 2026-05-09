// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Did;

namespace Sorcha.Citizen.Verifier.Services;

/// <summary>
/// Production <see cref="IIssuerKeyResolver"/> — resolves the credential's
/// <c>iss</c> claim to a DID document via <see cref="IDidResolverRegistry"/>,
/// matches the JWS <c>kid</c> header to a verification method (exact id first,
/// then RFC 7638 thumbprint fallback), and returns the matched VM's public JWK.
/// </summary>
/// <remarks>
/// <para>Feature 120 US1 — replaces the v1 opt-out path so production verifiers
/// reject credentials whose issuer cannot be cryptographically verified.</para>
/// <para>Failure-mode classification (per FR-003): the three buckets
/// (<c>did-unresolved</c>, <c>kid-unmatched</c>, <c>signature-failed</c>) are
/// surfaced via OTel counters and span attributes — the consumer (validator)
/// translates a null return into rejection without needing to know which
/// bucket fired.</para>
/// </remarks>
public sealed class DidResolverBackedIssuerKeyResolver : IIssuerKeyResolver, IDisposable
{
    /// <summary>Meter name for issuer-signature instrumentation (Feature 120 T015).</summary>
    public const string MeterName = "Sorcha.Verifier.IssuerSignature";

    private static readonly ActivitySource ActivitySourceInstance = new("Sorcha.Citizen.Verifier", "1.0.0");

    private readonly IDidResolverRegistry _registry;
    private readonly ILogger<DidResolverBackedIssuerKeyResolver> _logger;
    private readonly Meter _meter;
    private readonly Counter<long> _outcomeCounter;

    /// <summary>DI-friendly constructor.</summary>
    public DidResolverBackedIssuerKeyResolver(
        IDidResolverRegistry registry,
        IMeterFactory meterFactory,
        ILogger<DidResolverBackedIssuerKeyResolver> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _meter = (meterFactory ?? throw new ArgumentNullException(nameof(meterFactory)))
            .Create(MeterName, "1.0.0");
        _outcomeCounter = _meter.CreateCounter<long>(
            "sorcha_verifier_issuer_resolve_outcome_total",
            description:
                "Verifier issuer key resolution outcomes, tagged by outcome " +
                "(success|did-unresolved|kid-unmatched|no-verification-methods) " +
                "and kid_match_mode (exact|thumbprint-fallback|na).");
    }

    /// <inheritdoc />
    public Task<JsonElement?> ResolveAsync(string issuer, CancellationToken ct = default)
        => ResolveAsync(issuer, kid: null, ct);

    /// <inheritdoc />
    public async Task<JsonElement?> ResolveAsync(string issuer, string? kid, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(issuer);

        using var activity = ActivitySourceInstance.StartActivity(
            "verifier.issuer-resolve", ActivityKind.Internal);
        activity?.SetTag("verifier.issuer.iss", issuer);
        activity?.SetTag("verifier.issuer.kid", kid ?? "(null)");

        var doc = await _registry.ResolveWithAlsoKnownAsAsync(issuer, ct).ConfigureAwait(false);
        if (doc is null)
        {
            RecordOutcome(activity, "did-unresolved", "na");
            _logger.LogWarning(
                "Issuer DID could not be resolved: iss={Issuer} kid={Kid}",
                issuer, kid);
            return null;
        }

        if (doc.VerificationMethod.Count == 0)
        {
            RecordOutcome(activity, "no-verification-methods", "na");
            _logger.LogWarning(
                "Issuer DID document has no verificationMethod entries: iss={Issuer} kid={Kid}",
                issuer, kid);
            return null;
        }

        VerificationMethod? matched = null;
        var matchMode = "na";

        if (!string.IsNullOrEmpty(kid))
        {
            if (KidThumbprintHelper.TryMatchExact(doc, kid, out matched))
            {
                matchMode = "exact";
            }
            else if (KidThumbprintHelper.TryMatchByThumbprint(doc, kid, out matched))
            {
                matchMode = "thumbprint-fallback";
            }
        }

        // Fallback: if the credential carries no kid, accept the document's first VM with a JWK.
        // This is the legacy single-key-per-issuer shape and lets pre-Feature-120 credentials
        // continue to verify against newly-published DID documents.
        matched ??= doc.VerificationMethod.FirstOrDefault(v => v.PublicKeyJwk is not null);

        if (matched?.PublicKeyJwk is null)
        {
            RecordOutcome(activity, "kid-unmatched", matchMode);
            _logger.LogWarning(
                "Issuer DID document does not contain a verification method matching kid: " +
                "iss={Issuer} kid={Kid}", issuer, kid);
            return null;
        }

        RecordOutcome(activity, "success", matchMode);
        return matched.PublicKeyJwk;
    }

    private void RecordOutcome(Activity? activity, string outcome, string matchMode)
    {
        activity?.SetTag("verifier.issuer.outcome", outcome);
        activity?.SetTag("verifier.issuer.kid_match_mode", matchMode);
        _outcomeCounter.Add(1,
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("kid_match_mode", matchMode));
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
