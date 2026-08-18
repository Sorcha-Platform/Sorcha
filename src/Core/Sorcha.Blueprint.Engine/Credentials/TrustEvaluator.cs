// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// The single trust authority consulted by every credential-verification path (feature 135).
/// Combines pluggable trust sources per the policy's combinator, establishes the assurance
/// level (source-tier with an upward-only claim override), checks revocation, and produces a
/// pinnable <see cref="TrustEvidence"/> record. Fail-closed by default (FR-013).
/// </summary>
public class TrustEvaluator : ITrustEvaluator
{
    private readonly ITrustResolverRegistry _registry;
    private readonly IStatusListChecker? _statusChecker;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TrustEvaluator> _logger;

    public TrustEvaluator(
        ITrustResolverRegistry registry,
        IStatusListChecker? statusChecker = null,
        TimeProvider? timeProvider = null,
        ILogger<TrustEvaluator>? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _statusChecker = statusChecker;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<TrustEvaluator>.Instance;
    }

    /// <inheritdoc />
    public async Task<TrustDecision> EvaluateAsync(
        IssuerContext issuer,
        TrustPolicy? policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issuer);

        // Step 0 — signature is a hard precondition. The format handler verifies it; if it could
        // not, trust cannot be established (fail-closed). This is the correctness gap being closed:
        // a credential is never trusted on an unverified signature.
        if (!issuer.SignatureVerified)
        {
            _logger.LogWarning("Trust rejected: issuer signature not verified for {Issuer}", issuer.IssuerId);
            return new TrustDecision
            {
                IsTrusted = false,
                SignatureValid = false,
                FailureReason = TrustFailureReason.SignatureInvalid,
                Message = "Issuer signature was not verified."
            };
        }

        // Step 1 — apply the default policy when none was declared (FR-026): a single register
        // source at low assurance. Legacy issuer identifiers are migrated to a did-allowlist
        // source upstream (at requirement binding), so a null policy here means "register@Low".
        var effectivePolicy = NormalisePolicy(policy);
        var policyDigest = ComputePolicyDigest(effectivePolicy);

        // Step 2 — ask each source whether it vouches.
        var vouches = new List<(TrustSourceRef Source, TrustSourceVouch Vouch)>();
        foreach (var source in effectivePolicy.Sources)
        {
            var resolver = _registry.Resolve(source.Kind);
            TrustSourceVouch vouch;
            if (resolver is null)
            {
                _logger.LogWarning("No trust resolver registered for source kind {Kind}", source.Kind);
                vouch = TrustSourceVouch.Decline(TrustFailureReason.SourceUnavailable);
            }
            else
            {
                vouch = await resolver.VouchAsync(issuer, source, cancellationToken).ConfigureAwait(false);
            }
            vouches.Add((source, vouch));
        }

        // Step 3 — combine.
        var vouched = vouches.Where(v => v.Vouch.Vouched).ToList();
        bool trusted = effectivePolicy.Combinator switch
        {
            TrustCombinator.AllOf => vouches.Count > 0 && vouches.All(v => v.Vouch.Vouched),
            _ => vouched.Count > 0 // AnyOf
        };

        if (!trusted)
        {
            // Feature 177 — a signature-valid issuer that no source vouches for is accepted at reduced
            // assurance (a Warn) ONLY when the policy explicitly opts in; the default stays fail-closed.
            // This block is inert when the flag is false (falls through to the reject below).
            if (effectivePolicy.WarnOnUnlistedVerifiedIssuer)
            {
                _logger.LogWarning(
                    "Trust reduced-assurance (Warn): signature verified but no source vouched for {Issuer}",
                    issuer.IssuerId);
                return new TrustDecision
                {
                    IsTrusted = true,
                    SignatureValid = true,
                    ReducedAssurance = true,
                    EstablishedAssurance = AssuranceLevel.None,
                    Message = "Issuer signature verified but not vouched for by any trust source; " +
                              "accepted at reduced assurance (feature 177).",
                    Evidence = BaseEvidence(issuer, policyDigest)
                };
            }

            var reason = vouches
                .Where(v => !v.Vouch.Vouched)
                .Select(v => v.Vouch.Reason)
                .FirstOrDefault(r => r is not null) ?? TrustFailureReason.UntrustedIssuer;
            _logger.LogWarning("Trust rejected for {Issuer}: {Reason}", issuer.IssuerId, reason);
            return new TrustDecision
            {
                IsTrusted = false,
                SignatureValid = true,
                FailureReason = reason,
                Message = $"No trust source vouched for the issuer under the {effectivePolicy.Combinator} policy.",
                Evidence = BaseEvidence(issuer, policyDigest)
            };
        }

        // Step 4 — establish assurance: the strongest level conferred by a vouching source, then an
        // upward-only override from an explicit credential claim, honoured only when a trusted
        // (>= Substantial) source vouched (clarification A4 / FR-012).
        var established = vouched.Count > 0
            ? vouched.Max(v => v.Vouch.Assurance)
            : AssuranceLevel.Low;

        if (issuer.ClaimedAssurance is { } claimed
            && claimed > established
            && established >= AssuranceLevel.Substantial)
        {
            established = claimed;
        }

        if (established < effectivePolicy.MinAssuranceLevel)
        {
            _logger.LogWarning(
                "Trust rejected for {Issuer}: assurance {Established} below required {Required}",
                issuer.IssuerId, established, effectivePolicy.MinAssuranceLevel);
            return new TrustDecision
            {
                IsTrusted = false,
                SignatureValid = true,
                EstablishedAssurance = established,
                FailureReason = TrustFailureReason.InsufficientAssurance,
                Message = $"Established assurance '{established}' is below the required minimum '{effectivePolicy.MinAssuranceLevel}'.",
                Evidence = BaseEvidence(issuer, policyDigest)
            };
        }

        // Step 5 — revocation / status, fail-closed by policy.
        var statusOutcome = await CheckStatusAsync(issuer, cancellationToken).ConfigureAwait(false);
        if (statusOutcome is { } reject)
        {
            reject.SignatureValid = true;
            reject.EstablishedAssurance = established;
            reject.Evidence = BaseEvidence(issuer, policyDigest);
            return reject;
        }

        // Step 6 — accept. Build the evidence record from the deciding source(s).
        var deciding = (effectivePolicy.Combinator == TrustCombinator.AllOf ? vouches : vouches.Where(v => v.Vouch.Vouched))
            .ToList();
        var evidence = BaseEvidence(issuer, policyDigest);
        evidence.AssuranceLevel = established;
        // The strongest vouching source is the headline; apply every vouch's evidence fragment.
        var headline = vouched.OrderByDescending(v => v.Vouch.Assurance).First();
        evidence.VouchingSource = headline.Source.Kind;
        foreach (var v in deciding)
            v.Vouch.ApplyEvidence?.Invoke(evidence);

        _logger.LogInformation(
            "Trust accepted for {Issuer}: source={Source} assurance={Assurance}",
            issuer.IssuerId, evidence.VouchingSource, established);

        return new TrustDecision
        {
            IsTrusted = true,
            SignatureValid = true,
            EstablishedAssurance = established,
            DecidingSources = deciding.Select(v => v.Source.Kind).Distinct().ToList(),
            Evidence = evidence
        };
    }

    /// <summary>
    /// Resolves the credential's status reference. Returns a rejection <see cref="TrustDecision"/>
    /// when revoked/suspended or when the status is unresolved under a fail-closed policy; null
    /// when the credential is active or carries no status reference under fail-open.
    /// </summary>
    private async Task<TrustDecision?> CheckStatusAsync(IssuerContext issuer, CancellationToken ct)
    {
        // Evaluate EVERY declared purpose. A credential is unusable if any of them is set, so
        // checking only the primary reference lets a suspended-but-not-revoked credential through.
        var references = issuer.Statuses is { Count: > 0 }
            ? issuer.Statuses
            : issuer.Status is null ? [] : new[] { issuer.Status };

        if (references.Count == 0)
            return null; // no status reference — treated as active

        if (_statusChecker is null)
        {
            // A status was claimed but we cannot resolve it.
            return issuer.RevocationPolicy == RevocationCheckPolicy.FailClosed
                ? TrustDecision.Reject(TrustFailureReason.RevocationUnavailable,
                    "Credential carries a status reference but no status checker is available (fail-closed).")
                : null;
        }

        foreach (var reference in references)
        {
            var bit = await _statusChecker.CheckAsync(reference, ct).ConfigureAwait(false);

            switch (bit)
            {
                case StatusListBit.NotSet:
                    continue;

                case StatusListBit.Set:
                    // The purpose is named so an operator can tell a suspension from a revocation in
                    // the log, even though both refuse and the failure reason cannot yet carry it.
                    return TrustDecision.Reject(
                        TrustFailureReason.Revoked,
                        reference.Purpose is { Length: > 0 } purpose
                            ? $"Credential status '{purpose}' is set."
                            : "Credential is revoked or suspended.");

                default:
                    if (issuer.RevocationPolicy == RevocationCheckPolicy.FailClosed)
                    {
                        return TrustDecision.Reject(TrustFailureReason.RevocationUnavailable,
                            "Revocation status could not be resolved (fail-closed).");
                    }
                    continue;
            }
        }

        return null;
    }

    private static TrustPolicy NormalisePolicy(TrustPolicy? policy)
    {
        if (policy is null || policy.Sources is null || policy.Sources.Count == 0)
            return TrustPolicyExtensions.FromLegacyIssuers(null); // register@Low default
        return policy;
    }

    private TrustEvidence BaseEvidence(IssuerContext issuer, string policyDigest) => new()
    {
        IssuerId = issuer.IssuerId,
        EvaluatedAt = _timeProvider.GetUtcNow(),
        PolicyDigest = policyDigest
    };

    /// <summary>
    /// Stable SHA-256 over the canonical policy shape so an offline re-evaluation can confirm it
    /// used the same policy (FR-015).
    /// </summary>
    public static string ComputePolicyDigest(TrustPolicy policy)
    {
        var canonical = new
        {
            combinator = policy.Combinator.ToString(),
            minAssuranceLevel = policy.MinAssuranceLevel.ToString(),
            warnOnUnlistedVerifiedIssuer = policy.WarnOnUnlistedVerifiedIssuer,
            sources = policy.Sources
                .Select(s => new
                {
                    kind = s.Kind.ToString(),
                    confersAssurance = s.ConfersAssurance?.ToString(),
                    allowedIssuers = s.AllowedIssuers?.OrderBy(i => i, StringComparer.Ordinal).ToArray(),
                    trustListId = s.TrustListId
                })
                .OrderBy(s => s.kind, StringComparer.Ordinal)
                .ToArray()
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(canonical);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
