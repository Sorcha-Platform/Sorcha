// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sorcha.PresentationLifecycle.Abstractions;
using Sorcha.Verifier.Engine;
using Sorcha.Verifier.Engine.Models;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 127 — <see cref="IPresentationConsumer"/> for the Sorcha Wallet PWA.
/// First non-HAIP consumer in the F111 timebound presentation lifecycle.
/// </summary>
/// <remarks>
/// <para>The Sorcha wallet posts its signed verifiable presentation (vp_token,
/// optionally accompanied by a device delegation credential) to F111's
/// existing <c>POST /api/presentations/callbacks/sorcha-wallet/{requestId}</c>
/// endpoint. F111's <c>PresentationLifecycleService.HandleOutcomeAsync</c>
/// dispatches the payload here.</para>
/// <para>Verification is fully server-side and offline-friendly: this
/// consumer instantiates an in-memory <see cref="VerifierSession"/> from the
/// pending-presentation context and invokes <see cref="IVerifiablePresentationValidator"/>
/// from <c>Sorcha.Verifier.Engine</c> — the same validator the reference
/// F125 verifier desk uses on the PWA side. No external service round-trip.</para>
/// <para>Initiation is supplied via the new
/// <see cref="IPresentationConsumer.BuildInitiationAsync"/> extension (the
/// F111 "non-HAIP initiation contract" that was deferred until this feature).
/// The descriptor carries an OID4VP <c>openid4vp://</c> request URI that the
/// council page renders as the hybrid universal QR / tap-link affordance.</para>
/// <para>The verifier <c>client_id</c> (the council org DID) is resolved by the
/// lifecycle service from the blueprint's owning organisation and supplied via
/// <see cref="PresentationInitiationContext.VerifierClientId"/> (Spec 5). The
/// OID4VP request is unsigned in this flow, so <c>client_id</c> is a display
/// identity; signed request objects (mutual auth) are a deferred follow-up.</para>
/// </remarks>
public sealed class SorchaWalletPresentationConsumer : IPresentationConsumer
{
    private readonly IVerifiablePresentationValidator _validator;
    private readonly ILogger<SorchaWalletPresentationConsumer> _logger;

    /// <summary>Stable short identifier referenced by blueprints via the new <c>PresentationSource.SorchaWallet</c> enum value.</summary>
    public const string ConsumerNameValue = "sorcha-wallet";

    public SorchaWalletPresentationConsumer(
        IVerifiablePresentationValidator validator,
        ILogger<SorchaWalletPresentationConsumer> logger)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string ConsumerName => ConsumerNameValue;

    /// <inheritdoc />
    public async Task<PresentationOutcome> VerifyAsync(
        PresentationInitiationContext context,
        object verifierPayload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var payload = verifierPayload switch
        {
            SorchaWalletVerificationPayload swp => swp,
            JsonElement je => je.Deserialize<SorchaWalletVerificationPayload>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }),
            _ => null
        };

        if (payload is null || string.IsNullOrWhiteSpace(payload.VpToken))
        {
            _logger.LogWarning(
                "Sorcha-wallet consumer received unexpected verifier payload (type={Type}, requestId={RequestId})",
                verifierPayload?.GetType().FullName ?? "null",
                context.PresentationRequestId);
            return new PresentationOutcome(
                Kind: PresentationOutcomeKind.Decline,
                VerifiedClaims: null,
                Reason: PresentationDeclineReason.VerifierError,
                VerifierDiagnostics: new Dictionary<string, object>
                {
                    ["payloadType"] = verifierPayload?.GetType().FullName ?? "null"
                },
                PresentationSubmissionHash: null);
        }

        if (payload.Session is null)
        {
            // The lifecycle service (T032) is expected to populate
            // payload.Session from the pending state + blueprint metadata
            // before dispatching here. Until that wiring lands, this branch
            // surfaces a clear, debuggable error rather than fabricating a
            // session that wouldn't match what the wallet signed against.
            _logger.LogWarning(
                "Sorcha-wallet consumer received payload without a VerifierSession (requestId={RequestId}); " +
                "T032 lifecycle dispatch must populate Session from pending state.",
                context.PresentationRequestId);
            return new PresentationOutcome(
                Kind: PresentationOutcomeKind.Decline,
                VerifiedClaims: null,
                Reason: PresentationDeclineReason.VerifierError,
                VerifierDiagnostics: new Dictionary<string, object>
                {
                    ["error"] = "session-missing",
                    ["requestId"] = context.PresentationRequestId
                },
                PresentationSubmissionHash: null);
        }

        VerificationOutcome outcome;
        try
        {
            outcome = await _validator.ValidateAsync(
                payload.Session,
                payload.VpToken,
                payload.DelegationCredential,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Sorcha-wallet verifier engine threw for requestId {RequestId}",
                context.PresentationRequestId);
            return new PresentationOutcome(
                Kind: PresentationOutcomeKind.Decline,
                VerifiedClaims: null,
                Reason: PresentationDeclineReason.VerifierError,
                VerifierDiagnostics: new Dictionary<string, object>
                {
                    ["error"] = ex.GetType().Name
                },
                PresentationSubmissionHash: null);
        }

        if (!outcome.Accepted)
        {
            return new PresentationOutcome(
                Kind: PresentationOutcomeKind.Decline,
                VerifiedClaims: null,
                Reason: MapDeclineReason(outcome.Errors),
                VerifierDiagnostics: new Dictionary<string, object>
                {
                    ["errors"] = outcome.Errors,
                    ["completedAt"] = outcome.CompletedAt
                },
                PresentationSubmissionHash: null);
        }

        // Filter disclosed claims to the verifier-session's required set. Minimal
        // disclosure invariant — the consumer surfaces only what the blueprint
        // asked for. The wallet may include more in the VP; what crosses the
        // boundary into F111's outcome record is the strict required subset.
        var requiredClaimSet = new HashSet<string>(payload.Session.RequiredClaims, StringComparer.Ordinal);
        var filteredClaims = outcome.DisclosedClaims
            .Where(kv => requiredClaimSet.Contains(kv.Key))
            .ToDictionary(
                kv => kv.Key,
                kv => (object)(kv.Value ?? string.Empty),
                StringComparer.Ordinal);

        // Are any required claims missing? Decline with SchemaMismatch.
        var missing = requiredClaimSet.Except(filteredClaims.Keys).ToList();
        if (missing.Count > 0)
        {
            _logger.LogWarning(
                "Sorcha-wallet presentation missing required claims {Missing} for requestId {RequestId}",
                string.Join(",", missing), context.PresentationRequestId);
            return new PresentationOutcome(
                Kind: PresentationOutcomeKind.Decline,
                VerifiedClaims: null,
                Reason: PresentationDeclineReason.SchemaMismatch,
                VerifierDiagnostics: new Dictionary<string, object>
                {
                    ["missingClaims"] = missing
                },
                PresentationSubmissionHash: null);
        }

        var submissionHash = ComputePresentationHash(payload.Session, filteredClaims);
        return new PresentationOutcome(
            Kind: PresentationOutcomeKind.Success,
            VerifiedClaims: filteredClaims,
            Reason: null,
            VerifierDiagnostics: null,
            PresentationSubmissionHash: $"sha256:{submissionHash}");
    }

    /// <inheritdoc />
    public Task<ConsumerInitiationDescriptor> BuildInitiationAsync(
        PresentationInitiationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // Spec 5 — the lifecycle service resolves the verifier (council org) DID from
        // the blueprint's owning organisation and supplies it as VerifierClientId.
        // The placeholder is the explicit graceful-degradation fallback for orgs with
        // no published DID document (never issued a credential) — it never blocks the
        // gate. The request is unsigned in this flow, so client_id is a display
        // identity; Scope B (signed request objects) makes it cryptographically
        // load-bearing and resolves it against the same DID via the F120 resolver.
        var clientId = context.VerifierClientId ?? "did:sorcha:org:UNKNOWN";
        var uri =
            $"openid4vp://?client_id={Uri.EscapeDataString(clientId)}" +
            $"&response_type=vp_token" +
            $"&nonce={Uri.EscapeDataString(nonce)}" +
            $"&request_id={Uri.EscapeDataString(context.PresentationRequestId.ToString("N"))}";

        return Task.FromResult(new ConsumerInitiationDescriptor(
            AuthorizationRequestUri: uri,
            RequestUri: null,
            Nonce: nonce));
    }

    private static PresentationDeclineReason MapDeclineReason(IReadOnlyList<string> errors)
    {
        // Map verifier-engine error strings onto the F111 closed-set enum.
        // Pattern matching on substring is brittle but pragmatic; the verifier
        // engine's error messages live in one place and are tested.
        foreach (var error in errors)
        {
            if (error.Contains("revoked", StringComparison.OrdinalIgnoreCase))
                return PresentationDeclineReason.Revoked;
            if (error.Contains("expired", StringComparison.OrdinalIgnoreCase))
                return PresentationDeclineReason.ExpiredCredential;
            if (error.Contains("issuer", StringComparison.OrdinalIgnoreCase))
                return PresentationDeclineReason.WrongIssuer;
            if (error.Contains("signature", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("kb-jwt", StringComparison.OrdinalIgnoreCase))
                return PresentationDeclineReason.SignatureInvalid;
            if (error.Contains("schema", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("claim", StringComparison.OrdinalIgnoreCase))
                return PresentationDeclineReason.SchemaMismatch;
        }
        return PresentationDeclineReason.VerifierError;
    }

    private static string ComputePresentationHash(
        VerifierSession session,
        IReadOnlyDictionary<string, object> claims)
    {
        var canonical = string.Concat(
            session.RequiredVct,
            ":",
            string.Join(",", claims.Keys.OrderBy(k => k, StringComparer.Ordinal)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

/// <summary>
/// Wire shape posted by the Sorcha wallet PWA to F111's existing
/// <c>POST /api/presentations/callbacks/sorcha-wallet/{requestId}</c> endpoint.
/// </summary>
/// <param name="VpToken">SD-JWT VC compact-JWS presentation.</param>
/// <param name="DelegationCredential">Optional device delegation credential when the wallet's signing key is bound to the holder via a separate VC.</param>
/// <param name="Session">In-memory verifier session reconstructed by the lifecycle service from pending state. T032 populates this; until then, payloads without a session are rejected with VerifierError.</param>
public sealed record SorchaWalletVerificationPayload
{
    [JsonPropertyName("vpToken")]
    public string VpToken { get; init; } = string.Empty;

    [JsonPropertyName("delegationCredential")]
    public string? DelegationCredential { get; init; }

    [JsonPropertyName("session")]
    public VerifierSession? Session { get; init; }
}
