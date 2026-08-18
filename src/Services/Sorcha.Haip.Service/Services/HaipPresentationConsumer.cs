// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sorcha.Haip.Service.Models;
using Sorcha.PresentationLifecycle.Abstractions;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Feature 111 — HAIP-specific <see cref="IPresentationConsumer"/> adapter. Consumes
/// verifier results from <see cref="HaipPresentationVerifier"/> and maps them onto
/// the consumer-agnostic <see cref="PresentationOutcome"/> that Blueprint Service
/// writes to the register.
/// </summary>
public sealed class HaipPresentationConsumer : IPresentationConsumer
{
    private readonly ILogger<HaipPresentationConsumer> _logger;

    public HaipPresentationConsumer(ILogger<HaipPresentationConsumer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ConsumerName => "haip";

    public Task<PresentationOutcome> VerifyAsync(
        PresentationInitiationContext context,
        object verifierPayload,
        CancellationToken cancellationToken)
    {
        // The Blueprint callback endpoint accepts the body as a JsonElement and
        // forwards it opaquely — per the IPresentationConsumer contract, the
        // consumer is responsible for deserialising the payload into its own
        // types. An in-process test double can also pass a VerificationResult
        // directly; handle both.
        var result = verifierPayload switch
        {
            VerificationResult vr => vr,
            JsonElement je => je.Deserialize<VerificationResult>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }),
            _ => null
        };

        if (result is null)
        {
            _logger.LogWarning(
                "HAIP consumer received unexpected verifier payload type {Type} for requestId {RequestId}",
                verifierPayload?.GetType().FullName ?? "null", context.PresentationRequestId);
            return Task.FromResult(new PresentationOutcome(
                Kind: PresentationOutcomeKind.Decline,
                VerifiedClaims: null,
                Reason: PresentationDeclineReason.VerifierError,
                VerifierDiagnostics: new Dictionary<string, object>
                {
                    ["payloadType"] = verifierPayload?.GetType().FullName ?? "null"
                },
                PresentationSubmissionHash: null));
        }

        if (result.IsValid)
        {
            // Compact presentation-submission hash for audit. Computed over
            // the concatenated sorted claim keys + issuer so auditors can
            // demonstrate the verifier processed a specific claim set.
            var canonical = string.Concat(
                result.Issuer ?? "",
                ":",
                string.Join(",", result.VerifiedClaims.Keys.OrderBy(k => k, StringComparer.Ordinal)));
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

            return Task.FromResult(new PresentationOutcome(
                Kind: PresentationOutcomeKind.Success,
                VerifiedClaims: result.VerifiedClaims,
                Reason: null,
                VerifierDiagnostics: null,
                PresentationSubmissionHash: $"sha256:{hash}"));
        }

        var reason = MapReason(result);
        var diagnostics = new Dictionary<string, object>
        {
            ["errors"] = result.Errors,
            ["holderKeyVerified"] = result.HolderKeyVerified,
            ["x5cChainValid"] = result.X5cChainValid ?? (object)false,
            ["statusCheckResult"] = result.StatusCheckResult ?? (object)string.Empty
        };

        return Task.FromResult(new PresentationOutcome(
            Kind: PresentationOutcomeKind.Decline,
            VerifiedClaims: null,
            Reason: reason,
            VerifierDiagnostics: diagnostics,
            PresentationSubmissionHash: null));
    }

    private static PresentationDeclineReason MapReason(VerificationResult result)
    {
        var allErrors = string.Join(" | ", result.Errors);
        if (allErrors.Contains("expired", StringComparison.OrdinalIgnoreCase))
            return PresentationDeclineReason.ExpiredCredential;
        if (allErrors.Contains("issuer", StringComparison.OrdinalIgnoreCase))
            return PresentationDeclineReason.WrongIssuer;
        if (allErrors.Contains("revoked", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(result.StatusCheckResult, "revoked", StringComparison.OrdinalIgnoreCase))
            return PresentationDeclineReason.Revoked;
        // Feature 192 — checked AFTER revoked so a credential that is somehow both keeps the
        // terminal reason. Without this arm a suspension falls all the way through to
        // VerifierError ("the verifier broke"), which is worse than the revocation it used to
        // report: the compiler cannot catch it because these are equality tests, not a switch.
        if (allErrors.Contains("suspended", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(result.StatusCheckResult, "suspended", StringComparison.OrdinalIgnoreCase))
            return PresentationDeclineReason.Suspended;
        if (allErrors.Contains("schema", StringComparison.OrdinalIgnoreCase) ||
            allErrors.Contains("claim", StringComparison.OrdinalIgnoreCase))
            return PresentationDeclineReason.SchemaMismatch;
        if (allErrors.Contains("signature", StringComparison.OrdinalIgnoreCase) ||
            allErrors.Contains("x5c", StringComparison.OrdinalIgnoreCase) ||
            allErrors.Contains("holder key", StringComparison.OrdinalIgnoreCase))
            return PresentationDeclineReason.SignatureInvalid;
        return PresentationDeclineReason.VerifierError;
    }
}
