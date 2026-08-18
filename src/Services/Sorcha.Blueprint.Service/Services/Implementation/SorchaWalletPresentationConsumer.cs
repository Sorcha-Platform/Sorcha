// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sorcha.PresentationLifecycle.Abstractions;
using Sorcha.Verifier.Engine;
using Sorcha.Verifier.Engine.Dcql;
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

        // #1195 Phase 2 (Task 6b) — T032 landed: the VerifierSession is ALWAYS rebuilt from the
        // pending-state fields the lifecycle persisted at initiation (context) — never accepted
        // from the caller. (G2 fix, 2026-07-29 catch-up security review: the wire payload used to
        // carry an optional `session` object that, when present, was used verbatim. An
        // authenticated citizen could POST their own session with an attacker-chosen
        // RequiredVct/RequiredClaims/nonce and satisfy any credential gate with any held
        // credential. The `Session` wire field has been removed entirely — this rebuild path is
        // now the ONLY path.) Every refusal here is NAMED and distinct — wrong nonce (validator:
        // "KB-JWT nonce does not match session"), expired session, and genuinely-unknown session
        // are three different answers.
        VerifierSession session;
        {
            if (string.IsNullOrEmpty(context.Nonce) || string.IsNullOrEmpty(context.CredentialType))
            {
                // No nonce/credentialType on the pending state means nothing the KB-JWT could be
                // verified against. Since T032 the store persists these on every entry, so this is
                // NOT a legacy row — it means the pending presentation expired/was deleted before
                // this callback arrived (TTL or post-outcome cleanup), or initiation never ran for
                // this requestId. Named decline either way.
                _logger.LogWarning(
                    "Sorcha-wallet consumer cannot rebuild a VerifierSession for requestId={RequestId}: " +
                    "the pending state carries no nonce/credentialType (expired/deleted pending entry, " +
                    "or initiation never ran for this requestId).",
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

            var expiresAt = context.ExpiresAt ?? context.InitiatedAt.AddMinutes(5);
            if (DateTimeOffset.UtcNow > expiresAt)
            {
                _logger.LogWarning(
                    "Sorcha-wallet outcome for requestId={RequestId} arrived after the session expired at {ExpiresAt:O}.",
                    context.PresentationRequestId, expiresAt);
                return new PresentationOutcome(
                    Kind: PresentationOutcomeKind.Decline,
                    VerifiedClaims: null,
                    Reason: PresentationDeclineReason.VerifierError,
                    VerifierDiagnostics: new Dictionary<string, object>
                    {
                        ["error"] = "session-expired",
                        ["requestId"] = context.PresentationRequestId,
                        ["expiresAt"] = expiresAt
                    },
                    PresentationSubmissionHash: null);
            }

            session = new VerifierSession
            {
                SessionId = context.PresentationRequestId.ToString("N"),
                // MUST match the client_id the request object was served with — the wallet's
                // KB-JWT binds aud to it. Same resolution rule as BuildInitiationAsync.
                ClientId = ResolveClientId(context),
                Nonce = context.Nonce,
                RequiredVct = context.CredentialType,
                RequiredClaims = context.RequiredClaimNames ?? [],
                Purpose = "credential-gate",
                CreatedAt = context.InitiatedAt,
                ExpiresAt = expiresAt
            };
        }

        VerificationOutcome outcome;
        try
        {
            outcome = await _validator.ValidateAsync(
                session,
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

        // Task 6 fix round (#1195 Phase 2) — requiredClaims act as the GATE, and the emitted
        // VerifiedClaims are the FULL verified disclosure set (design §4.1 full-disclosure
        // bind: the device copy mirrors exactly what the root carries; optional claims like
        // middleName degrade gracefully instead of refusing citizens who don't have one).
        // Minimal disclosure is enforced where it belongs: at the wallet's consent sheet
        // (only consented disclosures enter the vp_token) and by the validator's RFC 9901
        // digest anchoring (only issuer-committed disclosures reach DisclosedClaims) — so
        // nothing here is client-controlled beyond what the issuer signed and the citizen
        // chose to disclose.
        var requiredClaimSet = new HashSet<string>(session.RequiredClaims, StringComparer.Ordinal);

        // The gate: every required claim must be present in the verified disclosure set.
        var missing = requiredClaimSet
            .Where(r => !outcome.DisclosedClaims.ContainsKey(r))
            .ToList();
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

        // The pass-through: everything the validator verified, verbatim.
        var verifiedClaims = outcome.DisclosedClaims.ToDictionary(
            kv => kv.Key,
            kv => (object)(kv.Value ?? string.Empty),
            StringComparer.Ordinal);

        var submissionHash = ComputePresentationHash(session, verifiedClaims);
        return new PresentationOutcome(
            Kind: PresentationOutcomeKind.Success,
            VerifiedClaims: verifiedClaims,
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
        // gate. The request object is unsigned in this flow (alg "none"), so client_id
        // is a display identity; US6 (Feature 181) signs it with the verifier
        // certificate and makes it cryptographically load-bearing.
        var clientId = ResolveClientId(context);

        // Feature 181 (T014) — the ask is expressed in DCQL inside a SERVED request
        // object (the request_uri form every Sorcha producer converges on), built via
        // the ONE shared builder (FR-008). The wallet fetches request_uri, parses
        // dcql_query, and posts its {vpToken, delegation} response to response_uri
        // (F111's callback route).
        //
        // Feature 181 US2 (T029) — when the lifecycle service supplies a pre-built
        // multi-credential query (every presentation requirement on the action, plus
        // credential_sets for any anyOfGroup alternatives) it is served verbatim; the
        // single-ask build is the back-compatible fallback for pre-US2 callers.
        var dcqlQuery = ResolveDeclaredQuery(context);

        var baseUrl = context.PublicBaseUrl?.TrimEnd('/') ?? string.Empty;
        var requestUri = $"{baseUrl}/api/presentations/{context.PresentationRequestId:N}/request-object";
        var responseUri = $"{baseUrl}/api/presentations/callbacks/{ConsumerNameValue}/{context.PresentationRequestId}";

        var now = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object>
        {
            ["iss"] = clientId,
            ["aud"] = "https://self-issued.me/v2",
            ["iat"] = now.ToUnixTimeSeconds(),
            ["response_type"] = "vp_token",
            ["response_mode"] = "direct_post",
            ["response_uri"] = responseUri,
            ["client_id"] = clientId,
            ["nonce"] = nonce,
            ["state"] = context.PresentationRequestId.ToString(),
            ["dcql_query"] = JsonSerializer.SerializeToElement(dcqlQuery, DcqlJson.Options)
        };
        var requestObjectJwt = BuildUnsignedJwt(payload);

        var authorizationRequestUri =
            $"openid4vp://authorize?client_id={Uri.EscapeDataString(clientId)}" +
            $"&request_uri={Uri.EscapeDataString(requestUri)}";

        return Task.FromResult(new ConsumerInitiationDescriptor(
            AuthorizationRequestUri: authorizationRequestUri,
            RequestUri: requestUri,
            Nonce: nonce,
            RequestObjectJwt: requestObjectJwt));
    }

    /// <summary>
    /// Resolve the DCQL query the request object should carry. Prefers the lifecycle
    /// service's pre-built multi-credential query (Feature 181 US2); falls back to the
    /// single-ask build from the context's credential type + required claims. A declared
    /// query that fails to deserialize falls through to the single-ask build (logged) so a
    /// serialization mishap can never block the gate.
    /// </summary>
    private DcqlQuery ResolveDeclaredQuery(PresentationInitiationContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.DeclaredDcqlQueryJson))
        {
            try
            {
                var declared = JsonSerializer.Deserialize<DcqlQuery>(context.DeclaredDcqlQueryJson, DcqlJson.Options);
                if (declared is { Credentials.Count: > 0 })
                {
                    return declared;
                }

                _logger.LogWarning(
                    "Declared DCQL query for requestId {RequestId} was empty; falling back to single-ask build.",
                    context.PresentationRequestId);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Declared DCQL query for requestId {RequestId} did not deserialize; falling back to single-ask build.",
                    context.PresentationRequestId);
            }
        }

        var requiredClaims = context.RequiredClaimNames ?? [];
        return DcqlRequestBuilder.Build(
            [DcqlCredentialAsk.SdJwt("credential", context.CredentialType ?? "unknown", requiredClaims)]);
    }

    /// <summary>
    /// Build an UNSIGNED request-object JWT (<c>alg: none</c>, empty signature segment).
    /// The Sorcha wallet decodes the payload without signature verification until US6
    /// (Feature 181) introduces verifier-certificate signing + wallet-side validation.
    /// </summary>
    private static string BuildUnsignedJwt(Dictionary<string, object> payload)
    {
        static string B64Url(byte[] bytes) => Convert.ToBase64String(bytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, object> { ["alg"] = "none", ["typ"] = "oauth-authz-req+jwt" });
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        return $"{B64Url(header)}.{B64Url(body)}.";
    }

    /// <summary>
    /// The ONE client_id resolution rule, shared by <see cref="BuildInitiationAsync"/> (what the
    /// request object serves) and the Task 6b session reconstruction (what the KB-JWT's <c>aud</c>
    /// is verified against). A drift between the two would make every wallet presentation fail
    /// the audience check.
    /// </summary>
    private static string ResolveClientId(PresentationInitiationContext context)
        => context.VerifierClientId ?? "did:sorcha:org:UNKNOWN";

    private static PresentationDeclineReason MapDeclineReason(IReadOnlyList<string> errors)
    {
        // Map verifier-engine error strings onto the F111 closed-set enum.
        // Pattern matching on substring is brittle but pragmatic; the verifier
        // engine's error messages live in one place and are tested.
        foreach (var error in errors)
        {
            if (error.Contains("revoked", StringComparison.OrdinalIgnoreCase))
                return PresentationDeclineReason.Revoked;
            // Feature 192 — kept below revoked so the terminal status wins for a credential that
            // is somehow both.
            if (error.Contains("suspended", StringComparison.OrdinalIgnoreCase))
                return PresentationDeclineReason.Suspended;
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
/// <remarks>
/// G2 security fix (2026-07-29 catch-up review): this record deliberately carries NO
/// client-supplied <c>VerifierSession</c>. It used to — an optional <c>session</c> wire field
/// that, when present, was used verbatim instead of the server-rebuilt session. Since the
/// callback endpoint only requires consumer-tier authentication (any signed-in citizen — see
/// <c>RequireConsumerAudience</c>, CLAUDE.md §13), that let an attacker POST their own session
/// object carrying an attacker-chosen <c>RequiredVct</c>, an emptied <c>RequiredClaims</c> gate,
/// and an attacker nonce/clientId — satisfying any credential gate with any held credential, with
/// no forgery required. <see cref="SorchaWalletPresentationConsumer.VerifyAsync"/> now ALWAYS
/// rebuilds the <c>VerifierSession</c> from the server-side pending-presentation row
/// (<c>PresentationInitiationContext</c>); nothing under this record's shape can influence which
/// session the validator checks against. System.Text.Json silently drops any unknown
/// <c>session</c> property an old or malicious caller still sends.
/// </remarks>
public sealed record SorchaWalletVerificationPayload
{
    [JsonPropertyName("vpToken")]
    public string VpToken { get; init; } = string.Empty;

    [JsonPropertyName("delegationCredential")]
    public string? DelegationCredential { get; init; }
}
