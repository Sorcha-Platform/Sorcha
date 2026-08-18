// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sorcha.PresentationLifecycle.Abstractions;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 111 — Blueprint-side <see cref="IPresentationConsumer"/> adapter for
/// HAIP. Reads the verifier-result JSON forwarded by
/// <c>Sorcha.Haip.Service.Services.PresentationCallbackRelay</c> and maps it
/// onto a consumer-agnostic <see cref="PresentationOutcome"/> that the
/// lifecycle service writes to the register.
/// </summary>
/// <remarks>
/// <para>
/// The consumer interface is registered in Blueprint Service's DI because
/// Blueprint owns the lifecycle dispatcher
/// (<c>PresentationLifecycleService</c>) that resolves
/// <c>IEnumerable&lt;IPresentationConsumer&gt;</c> by name. HAIP Service's own
/// <c>HaipPresentationConsumer</c> registration is process-local and
/// unreachable from here — the previous architecture mistake that made the
/// callback endpoint return <c>"No IPresentationConsumer registered with
/// name 'haip'."</c> on every HAIP-driven Phase 2 walkthrough.
/// </para>
/// <para>
/// The wire shape mirrors <c>Sorcha.Haip.Service.Models.VerificationResult</c>
/// — duplicated here so Blueprint Service does not need to project-reference
/// HAIP Service. Both ends carry <c>[JsonPropertyName]</c> attributes for the
/// same camelCase property names so the serialized payload is interchangeable.
/// </para>
/// </remarks>
public sealed class HaipPresentationConsumer : IPresentationConsumer
{
    private readonly ILogger<HaipPresentationConsumer> _logger;

    public HaipPresentationConsumer(ILogger<HaipPresentationConsumer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string ConsumerName => "haip";

    /// <inheritdoc />
    public Task<PresentationOutcome> VerifyAsync(
        PresentationInitiationContext context,
        object verifierPayload,
        CancellationToken cancellationToken)
    {
        var result = verifierPayload switch
        {
            HaipVerificationResult vr => vr,
            JsonElement je => je.Deserialize<HaipVerificationResult>(
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

    private static PresentationDeclineReason MapReason(HaipVerificationResult result)
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

/// <summary>
/// Wire shape for HAIP-side verification results forwarded by
/// <c>PresentationCallbackRelay</c>. Mirrors
/// <c>Sorcha.Haip.Service.Models.VerificationResult</c> — kept duplicated
/// here so this service does not project-reference HAIP Service.
/// </summary>
internal sealed class HaipVerificationResult
{
    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("verifiedClaims")]
    public Dictionary<string, object> VerifiedClaims { get; set; } = new();

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();

    [JsonPropertyName("holderKeyVerified")]
    public bool HolderKeyVerified { get; set; }

    [JsonPropertyName("x5cChainValid")]
    public bool? X5cChainValid { get; set; }

    [JsonPropertyName("statusCheckResult")]
    public string? StatusCheckResult { get; set; }

    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }
}
