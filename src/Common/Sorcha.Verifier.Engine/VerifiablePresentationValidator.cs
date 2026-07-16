// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Sorcha.Cryptography.Secp256k1;
using Sorcha.Verifier.Engine.Models;

namespace Sorcha.Verifier.Engine;

/// <summary>
/// v1 reference verifier validator. Performs the offline chain check the citizen
/// wallet PWA design requires:
///
/// <list type="number">
///   <item>Parse the SD-JWT VC compact form (<c>credential.disclosure1.disclosureN.kbjwt</c>).</item>
///   <item>Extract the holder JWK from the credential's <c>cnf.jwk</c> claim.</item>
///   <item>When a device delegation credential is supplied, verify its signature with that
///         holder JWK, extract the device JWK from its <c>cnf.jwk</c>, and check its status-list bit;
///         the KB-JWT is then verified with the device JWK. When NO delegation is supplied
///         (server-custody, #1195 Phase 2) the KB-JWT is verified directly against the credential's
///         own <c>cnf.jwk</c> holder key — a legitimate server-side wallet holds cnf and signs.</item>
///   <item>Verify the KB-JWT signature with the delegation device JWK (delegation model) or the
///         credential's own holder JWK (server-custody model).</item>
///   <item>Verify KB-JWT <c>nonce</c> + <c>aud</c> match the verifier session.</item>
///   <item>Verify required claim names appear in the disclosed claim set.</item>
/// </list>
///
/// <para>v1 deliberately defers full issuer-signature verification — the issuer key
/// resolver is optional, and when absent the validator trusts the credential payload
/// shape but logs a warning. Full DID-backed issuer trust lands with the production
/// verifier hardening pass; the citizen wallet feature's novel contribution is the
/// holder→device delegation chain, which IS verified here in full.</para>
/// </summary>
public sealed class VerifiablePresentationValidator : IVerifiablePresentationValidator
{
    private static readonly TimeSpan DefaultClockSkew = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultKbJwtMaxLifetime = TimeSpan.FromSeconds(120);

    private readonly IStatusListCache _statusListCache;
    private readonly IIssuerKeyResolver _issuerKeys;
    private readonly TimeProvider _clock;
    private readonly ILogger<VerifiablePresentationValidator> _logger;
    private readonly bool _requireIssuerSignature;
    private readonly FederationVerifierMetrics? _metrics;
    private readonly TimeSpan _clockSkew;
    private readonly TimeSpan _kbJwtMaxLifetime;

    /// <summary>
    /// Initialises a new instance. <paramref name="issuerKeys"/> defaults to
    /// the opt-out resolver via DI registration; <paramref name="requireIssuerSignature"/>
    /// is read from configuration <c>Verifier:RequireIssuerSignature</c>
    /// (default false in v1, true expected in production hardening pass).
    /// Feature 138: <paramref name="metrics"/> records trust rejections, <paramref name="clockSkew"/>
    /// (<c>Verifier:ClockSkewSeconds</c>) bounds wall-clock tolerance, and
    /// <paramref name="kbJwtMaxLifetime"/> (<c>Verifier:KbJwtMaxLifetimeSeconds</c>) caps KB-JWT lifetime.
    /// </summary>
    public VerifiablePresentationValidator(
        IStatusListCache statusListCache,
        IIssuerKeyResolver issuerKeys,
        TimeProvider clock,
        ILogger<VerifiablePresentationValidator> logger,
        bool requireIssuerSignature = false,
        FederationVerifierMetrics? metrics = null,
        TimeSpan? clockSkew = null,
        TimeSpan? kbJwtMaxLifetime = null)
    {
        _statusListCache = statusListCache ?? throw new ArgumentNullException(nameof(statusListCache));
        _issuerKeys = issuerKeys ?? throw new ArgumentNullException(nameof(issuerKeys));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _requireIssuerSignature = requireIssuerSignature;
        _metrics = metrics;
        _clockSkew = clockSkew ?? DefaultClockSkew;
        _kbJwtMaxLifetime = kbJwtMaxLifetime ?? DefaultKbJwtMaxLifetime;
    }

    /// <summary>
    /// Back-compat constructor used by the existing test suite (Feature 114
    /// Phase 3 tests passed in just status list + clock + logger). Wires the
    /// opt-out issuer resolver — which preserves the v1 contract of
    /// "trust holder→device chain even if issuer is unverifiable".
    /// </summary>
    public VerifiablePresentationValidator(
        IStatusListCache statusListCache,
        TimeProvider clock,
        ILogger<VerifiablePresentationValidator> logger)
        : this(statusListCache, new OptOutIssuerKeyResolver(), clock, logger, false)
    {
    }

    /// <inheritdoc />
    public async Task<VerificationOutcome> ValidateAsync(
        VerifierSession session,
        string vpToken,
        string? delegationCredential,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(vpToken);

        var errors = new List<string>();
        var disclosed = new Dictionary<string, object?>();

        // ── Feature 155 — per-layer verdict trail state ───────────────────────────
        //   These locals accumulate the raw inputs for the LivePresentation / IssuerSignature /
        //   Revocation layers as validation proceeds. They are turned into ValidationLayerResults at
        //   every return point (success or failure) by BuildLayers, so the trail reflects whatever was
        //   determined before an early reject — without altering the accept/reject decision itself.
        var layerState = new LayerState();

        // Local builder so every return path attaches the same structured layers. Failure paths that
        // short-circuit before a layer's inputs are known simply omit that layer (matches the contract:
        // only surface what was actually checked; never fabricate a Pass).
        IReadOnlyList<ValidationLayerResult> BuildLayers(bool accepted)
            => layerState.Build(accepted, errors, session);

        try
        {
            // ── 1. Parse the SD-JWT VC compact form ───────────────────────────────
            var (credentialJwt, disclosureSegments, kbJwt) = SplitSdJwt(vpToken);
            if (credentialJwt is null)
            {
                errors.Add("vp_token is not a valid SD-JWT compact serialisation.");
                return Failure(errors, BuildLayers(false));
            }
            if (kbJwt is null)
            {
                errors.Add("vp_token is missing the trailing key-binding JWT.");
                return Failure(errors, BuildLayers(false));
            }

            var credentialPayload = TryParseJwtPayload(credentialJwt);
            if (credentialPayload is null)
            {
                errors.Add("vp_token credential JWT is malformed.");
                return Failure(errors, BuildLayers(false));
            }

            // ── 2. Validate vct matches the requested credential type ─────────────
            var vct = TryGetString(credentialPayload.Value, "vct");
            if (!string.Equals(vct, session.RequiredVct, StringComparison.Ordinal))
            {
                errors.Add($"Credential vct '{vct}' does not match required '{session.RequiredVct}'.");
            }

            // ── 3. Extract holder JWK from credential cnf.jwk ─────────────────────
            if (!TryExtractCnfJwk(credentialPayload.Value, out var holderJwk))
            {
                errors.Add("Credential is missing cnf.jwk (holder key binding).");
                return Failure(errors, BuildLayers(false));
            }
            // Record the holder key's type/curve for the LivePresentation layer — the curve the holder
            // key binds with (Ed25519 for the default wallet, P-256 otherwise) is the fact that made an
            // ES256-only verifier reject Ed25519-holder presentations, so it's the first thing to surface.
            layerState.HolderKey = DescribeJwk(holderJwk);

            // ── 4. Establish the KB-JWT signing key (two custody models) ──────────
            //   The presentation carries its custody model implicitly, in whether a device
            //   delegation credential accompanies it:
            //
            //   • DELEGATION model (F114/F127 device-bound wallets, the desk/doorstep verifiers):
            //     the holder key signs a device delegation credential whose cnf is the device key,
            //     and the KB-JWT is device-signed. The delegation's signature (holder key), expiry,
            //     and status-list bit are all verified here. Behaviour is unchanged when a
            //     delegation credential is supplied.
            //
            //   • SERVER-CUSTODY model (#1195 Phase 2, design §2): NO delegation. A legitimate
            //     server-side wallet holds the credential's own cnf holder key and signs the KB-JWT
            //     directly with it — there is no device key and nothing to delegate. The KB-JWT is
            //     verified straight against the credential's cnf.jwk (same semantics as
            //     SdJwtService.VerifyPresentationAsync on the HAIP path). This is what the Task-6
            //     "bind to device" flow posts (holder-signed vp_token, no delegation).
            JsonElement kbSigningKey;
            bool serverCustody;
            if (!string.IsNullOrWhiteSpace(delegationCredential))
            {
                serverCustody = false;
                layerState.DelegationAlg = HeaderAlg(delegationCredential);

                var delegationErrors = await ValidateDelegationAsync(
                    delegationCredential, holderJwk, layerState, ct);
                if (delegationErrors.Count > 0)
                {
                    errors.AddRange(delegationErrors);
                    return Failure(errors, BuildLayers(false));
                }

                var delegationPayload = TryParseJwtPayload(delegationCredential);
                if (delegationPayload is null
                    || !TryExtractCnfJwk(delegationPayload.Value, out var deviceJwk))
                {
                    errors.Add("Delegation credential is missing cnf.jwk (device key).");
                    return Failure(errors, BuildLayers(false));
                }
                layerState.DeviceKey = DescribeJwk(deviceJwk);
                kbSigningKey = deviceJwk;
            }
            else
            {
                // Server-custody: the KB-JWT MUST be signed by the credential's own cnf holder key
                // (extracted at step 3). The missing-cnf case already rejected loudly above with
                // "Credential is missing cnf.jwk (holder key binding)" — so reaching here with no
                // delegation guarantees a holder key to verify against.
                serverCustody = true;
                layerState.ServerCustody = true;
                kbSigningKey = holderJwk;
            }

            // ── 4b. Verify the credential's issuer signature ──────────────────────
            //   The issuer DID is in the iss claim. We resolve a public JWK via
            //   IIssuerKeyResolver — production wires DID resolution, tests and
            //   the demo register keys explicitly, the v1 default opts out and
            //   accepts on the holder→device chain alone (logged warning).
            var issuer = TryGetString(credentialPayload.Value, "iss");
            if (string.IsNullOrEmpty(issuer))
            {
                errors.Add("Credential is missing iss claim.");
                return Failure(errors, BuildLayers(false));
            }
            // Feature 120 — pass the credential's JWS kid header to the resolver so DID-resolver-backed
            // implementations can pick the correct verification method out of multi-key documents.
            var credentialHeader = TryParseJwtHeader(credentialJwt);
            var credentialKid = credentialHeader is { } h ? TryGetString(h, "kid") : null;
            layerState.Issuer = issuer;
            layerState.CredentialId = TryGetString(credentialPayload.Value, "jti");
            layerState.IssuerKid = credentialKid;
            layerState.IssuerAlg = credentialHeader is { } hh ? TryGetString(hh, "alg") : null;
            var issuerJwk = await _issuerKeys.ResolveAsync(issuer, credentialKid, ct);
            var issuerSignatureVerified = false;
            if (issuerJwk is not null)
            {
                if (!VerifyJwsSignature(credentialJwt, issuerJwk.Value, out _))
                {
                    errors.Add($"Credential signature verification failed against issuer '{issuer}' key.");
                    layerState.IssuerSignature = IssuerLayer.ResolvedFailed;
                    return Failure(errors, BuildLayers(false));
                }

                issuerSignatureVerified = true;
                layerState.IssuerSignature = IssuerLayer.ResolvedVerified;
            }
            else if (_requireIssuerSignature)
            {
                errors.Add(
                    $"No public key available for issuer '{issuer}' and " +
                    "RequireIssuerSignature is enabled. Reject.");
                layerState.IssuerSignature = IssuerLayer.UnresolvedRequired;
                return Failure(errors, BuildLayers(false));
            }
            else
            {
                _logger.LogWarning(
                    "Issuer '{Issuer}' key unresolved; accepting on holder→device chain only " +
                    "(v1 contract; enable Verifier:RequireIssuerSignature to harden).",
                    issuer);
                layerState.IssuerSignature = IssuerLayer.UnresolvedNotRequired;
            }

            // ── 5. Verify KB-JWT signature ────────────────────────────────────────
            //   Delegation model → the delegation's device key; server-custody → the credential's
            //   own cnf holder key. VerifyJwsSignature dispatches on the KB-JWT header alg
            //   (ES256 via ECDsa, EdDSA via BouncyCastle, ES256K), so an Ed25519 holder key — the
            //   default Sorcha wallet, signing its own KB-JWT under server-custody — verifies just
            //   as a P-256 device or holder key does. The two custody models raise DISTINCT named
            //   errors so the verdict never conflates "wrong device key" with "holder key mismatch".
            if (!VerifyJwsSignature(kbJwt, kbSigningKey, out var kbPayload))
            {
                errors.Add(serverCustody
                    ? "Key binding mismatch: KB-JWT not signed by the credential's cnf key."
                    : "KB-JWT signature verification failed against device key.");
                return Failure(errors, BuildLayers(false));
            }

            // ── 5b. Enforce KB-JWT freshness (Feature 138 US5) ────────────────────
            //   The key-binding proof carries its own short, independently-enforced exp. Checked here —
            //   the earliest point the payload is signature-verified and therefore trustworthy — so a
            //   captured proof replayed after its exp is rejected even while the session is still open
            //   (the session nonce/aud/TTL alone left a multi-minute replay window). exp is mandatory
            //   (FR-017); freshness is wall-clock within the configured skew (FR-018). Mid-session
            //   revocation is independently re-checked at verify time by the delegation status-list
            //   check above (US1 fail-closed), together satisfying FR-019.
            // The KB-JWT signature verified against the device key and the delegation chain held — the
            // holder→device binding is sound. Record this for the LivePresentation layer; nonce/aud/exp
            // mismatches below append their own detail and flip the layer to Fail.
            layerState.KbJwtVerified = true;
            var kbHeaderEl = TryParseJwtHeader(kbJwt);
            layerState.KbJwtAlg = kbHeaderEl is { } kh ? TryGetString(kh, "alg") : null;

            if (!kbPayload.TryGetProperty("exp", out var kbExpEl) || kbExpEl.ValueKind != JsonValueKind.Number)
            {
                errors.Add("KB-JWT is missing the mandatory exp claim.");
                _metrics?.PresentationReplayRejected("kbjwt_missing_exp");
                layerState.LivePresentationFailed = true;
                return Failure(errors, BuildLayers(false));
            }
            var kbExp = DateTimeOffset.FromUnixTimeSeconds(kbExpEl.GetInt64());
            var nowUtc = _clock.GetUtcNow();
            if (nowUtc > kbExp + _clockSkew)
            {
                errors.Add("KB-JWT has expired; the key-binding proof is no longer fresh.");
                _metrics?.PresentationReplayRejected("kbjwt_expired");
                layerState.LivePresentationFailed = true;
                return Failure(errors, BuildLayers(false));
            }
            // Cap the proof lifetime so an over-long-lived KB-JWT cannot widen the replay window.
            if (kbPayload.TryGetProperty("iat", out var kbIatEl) && kbIatEl.ValueKind == JsonValueKind.Number)
            {
                var kbIat = DateTimeOffset.FromUnixTimeSeconds(kbIatEl.GetInt64());
                layerState.KbJwtAgeSeconds = (nowUtc - kbIat).TotalSeconds;
                if (kbExp - kbIat > _kbJwtMaxLifetime)
                {
                    errors.Add(
                        $"KB-JWT lifetime ({(kbExp - kbIat).TotalSeconds:0}s) exceeds the maximum " +
                        $"permitted ({_kbJwtMaxLifetime.TotalSeconds:0}s).");
                    _metrics?.PresentationReplayRejected("kbjwt_expired");
                    layerState.LivePresentationFailed = true;
                    return Failure(errors, BuildLayers(false));
                }
            }

            // ── 6. Verify KB-JWT nonce + aud match the session ────────────────────
            var kbNonce = TryGetString(kbPayload, "nonce");
            var kbAudience = TryGetString(kbPayload, "aud");
            layerState.KbNonce = kbNonce;
            layerState.KbAudience = kbAudience;
            if (!string.Equals(kbNonce, session.Nonce, StringComparison.Ordinal))
            {
                errors.Add("KB-JWT nonce does not match session.");
                layerState.LivePresentationFailed = true;
            }
            if (!string.Equals(kbAudience, session.ClientId, StringComparison.Ordinal))
            {
                errors.Add("KB-JWT aud does not match verifier client_id.");
                layerState.LivePresentationFailed = true;
            }

            // ── 7. Anchor disclosures, extract disclosed claims, check required set ──
            // #1195 Phase 2 (Task 6 fix round) — RFC 9901 disclosure anchoring: every
            // disclosure segment MUST be committed by the credential (its SHA-256 digest in an
            // _sd array or an array-element {"...": digest} marker of the payload, or of an
            // already-accepted disclosure's value — nested SD). Without this, a presenter could
            // append fabricated [salt, name, value] segments — or OVERRIDE a legitimate claim's
            // value with a forged duplicate — and have them emitted as verified claims. The
            // KB-JWT cannot protect against this: its signer IS the presenter. Tampering
            // rejects the whole presentation, loudly.
            //
            // Fix round 2 — honour the payload's _sd_alg (RFC 9901 §4.1.1): sha-256 is the
            // default and the only supported algorithm. Any OTHER declared value is its own
            // DISTINCT rejection — digests computed under an unknown algorithm can never
            // anchor, and reporting that as "unanchored disclosure" would accuse a legitimate
            // credential of forgery. The error must tell the truth about why.
            if (disclosureSegments.Count > 0)
            {
                var sdAlg = TryGetString(credentialPayload.Value, "_sd_alg");
                if (sdAlg is not null && !string.Equals(sdAlg, "sha-256", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(
                        $"Unsupported _sd_alg '{sdAlg}' — this verifier supports sha-256 only; " +
                        "disclosure anchoring cannot be evaluated.");
                    layerState.LivePresentationFailed = true;
                    return Failure(errors, BuildLayers(false));
                }
            }

            var unanchored = FindUnanchoredDisclosures(credentialPayload.Value, disclosureSegments);
            if (unanchored.Count > 0)
            {
                foreach (var name in unanchored)
                {
                    errors.Add(
                        $"Disclosure '{name}' is not committed by the credential " +
                        "(no matching _sd digest) — the presentation is tampered or malformed.");
                }
                layerState.LivePresentationFailed = true;
                return Failure(errors, BuildLayers(false));
            }

            disclosed = ParseDisclosures(disclosureSegments);
            foreach (var required in session.RequiredClaims)
            {
                if (!disclosed.ContainsKey(required))
                {
                    errors.Add($"Required claim '{required}' was not disclosed.");
                }
            }

            if (errors.Count > 0) return Failure(errors, BuildLayers(false));

            _logger.LogInformation(
                "Verifier session {SessionId} accepted: vct={Vct}, claims={Claims}",
                session.SessionId, session.RequiredVct, disclosed.Count);

            return new VerificationOutcome
            {
                Accepted = true,
                DisclosedClaims = disclosed,
                Errors = [],
                CompletedAt = _clock.GetUtcNow(),
                IssuerSignature = issuerSignatureVerified
                    ? IssuerSignatureStatus.Verified
                    : IssuerSignatureStatus.NotVerified,
                Layers = BuildLayers(true),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Verifier session {SessionId} threw during validation", session.SessionId);
            errors.Add($"Validator exception: {ex.Message}");
            return Failure(errors, layerState.Build(false, errors, session));
        }
    }

    private VerificationOutcome Failure(
        IReadOnlyList<string> errors,
        IReadOnlyList<ValidationLayerResult>? layers = null) => new()
    {
        Accepted = false,
        DisclosedClaims = new Dictionary<string, object?>(),
        Errors = errors,
        CompletedAt = _clock.GetUtcNow(),
        Layers = layers ?? [],
    };

    // ─────────────────────────── parsing helpers ─────────────────────────────────

    /// <summary>
    /// Split the SD-JWT compact form. Format: <c>jwt~disclosure~...~kbjwt</c>
    /// where every disclosure is a single base64url segment and the trailing KB-JWT
    /// is itself a three-part JWT. The terminating <c>~</c> (no KB-JWT) is supported
    /// but rejected upstream by this caller.
    /// </summary>
    internal static (string? Credential, IReadOnlyList<string> Disclosures, string? KbJwt) SplitSdJwt(string vp)
    {
        var segments = vp.Split('~');
        if (segments.Length < 2) return (null, [], null);

        var credential = segments[0];
        // Last segment is either empty (no KB-JWT) or the KB-JWT itself (contains two dots).
        var last = segments[^1];
        var hasKbJwt = !string.IsNullOrEmpty(last) && last.Count(c => c == '.') == 2;

        var disclosureCount = segments.Length - 1 - (hasKbJwt ? 1 : 0);
        var disclosures = new List<string>(Math.Max(disclosureCount, 0));
        for (int i = 1; i <= disclosureCount; i++)
        {
            if (!string.IsNullOrEmpty(segments[i])) disclosures.Add(segments[i]);
        }
        return (credential, disclosures, hasKbJwt ? last : null);
    }

    /// <summary>Parse a JWT's payload to a JsonElement; returns null if malformed.</summary>
    internal static JsonElement? TryParseJwtPayload(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return null;
            var bytes = Base64Url.DecodeFromChars(parts[1]);
            return JsonSerializer.Deserialize<JsonElement>(bytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Parse a JWT's protected header to a JsonElement; returns null if malformed.</summary>
    internal static JsonElement? TryParseJwtHeader(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return null;
            var bytes = Base64Url.DecodeFromChars(parts[0]);
            return JsonSerializer.Deserialize<JsonElement>(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetString(JsonElement obj, string name)
        => obj.ValueKind == JsonValueKind.Object
           && obj.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static bool TryExtractCnfJwk(JsonElement payload, out JsonElement jwk)
    {
        jwk = default;
        if (payload.ValueKind != JsonValueKind.Object) return false;
        if (!payload.TryGetProperty("cnf", out var cnf)) return false;
        if (cnf.ValueKind != JsonValueKind.Object) return false;
        if (!cnf.TryGetProperty("jwk", out var inner)) return false;
        if (inner.ValueKind != JsonValueKind.Object) return false;
        jwk = inner;
        return true;
    }

    /// <summary>
    /// RFC 9901 disclosure anchoring (#1195 Phase 2, Task 6 fix rounds 1+2). Returns the
    /// display names of every disclosure segment whose SHA-256 digest is NOT committed by the
    /// credential — i.e. not present in any <c>_sd</c> array OR any array-element
    /// <c>{"...": "&lt;digest&gt;"}</c> marker of the credential payload or of an
    /// already-anchored disclosure's value (nested selective disclosure). Runs to fixpoint so
    /// nested disclosures anchor regardless of segment order. An empty result means every
    /// presented disclosure is issuer-committed.
    /// </summary>
    internal static IReadOnlyList<string> FindUnanchoredDisclosures(
        JsonElement credentialPayload,
        IReadOnlyList<string> segments)
    {
        if (segments.Count == 0) return [];

        var committed = new HashSet<string>(StringComparer.Ordinal);
        CollectSdDigests(credentialPayload, committed);

        var anchored = new bool[segments.Count];
        bool progressed = true;
        while (progressed)
        {
            progressed = false;
            for (var i = 0; i < segments.Count; i++)
            {
                if (anchored[i]) continue;
                var digest = Base64Url.EncodeToString(
                    SHA256.HashData(Encoding.ASCII.GetBytes(segments[i])));
                if (!committed.Contains(digest)) continue;

                anchored[i] = true;
                progressed = true;

                // Nested SD: the disclosed value may itself carry _sd arrays that commit
                // further disclosures (e.g. address sub-fields).
                try
                {
                    using var doc = JsonDocument.Parse(Base64Url.DecodeFromChars(segments[i]));
                    if (doc.RootElement.ValueKind == JsonValueKind.Array &&
                        doc.RootElement.GetArrayLength() is 2 or 3)
                    {
                        CollectSdDigests(doc.RootElement[doc.RootElement.GetArrayLength() - 1], committed);
                    }
                }
                catch
                {
                    // A segment that anchors but doesn't parse contributes no nested digests;
                    // ParseDisclosures drops it downstream.
                }
            }
        }

        var unanchoredNames = new List<string>();
        for (var i = 0; i < segments.Count; i++)
        {
            if (anchored[i]) continue;
            unanchoredNames.Add(ReadDisclosureDisplayName(segments[i]));
        }
        return unanchoredNames;
    }

    /// <summary>
    /// Recursively collect every digest the credential commits: string entries of <c>_sd</c>
    /// arrays (object-property SD) and the values of <c>{"...": "&lt;digest&gt;"}</c>
    /// array-element markers (RFC 9901 array SD — fix round 2). Harvesting a marker anywhere
    /// in an anchored subtree is deliberate lenience: digests are unguessable SHA-256 values,
    /// so over-collection can never anchor a forged disclosure.
    /// </summary>
    private static void CollectSdDigests(JsonElement element, HashSet<string> sink)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("_sd") && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var digest in property.Value.EnumerateArray())
                        {
                            if (digest.ValueKind == JsonValueKind.String)
                            {
                                sink.Add(digest.GetString()!);
                            }
                        }
                    }
                    else if (property.NameEquals("...") && property.Value.ValueKind == JsonValueKind.String)
                    {
                        // RFC 9901 array-element marker: {"...": "<digest>"} commits a
                        // 2-element [salt, value] disclosure for that array position.
                        sink.Add(property.Value.GetString()!);
                    }
                    else
                    {
                        CollectSdDigests(property.Value, sink);
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectSdDigests(item, sink);
                }
                break;
        }
    }

    /// <summary>Best-effort claim name of a disclosure segment for error messages; never throws.</summary>
    private static string ReadDisclosureDisplayName(string segment)
    {
        try
        {
            using var doc = JsonDocument.Parse(Base64Url.DecodeFromChars(segment));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return "<malformed>";
            return doc.RootElement.GetArrayLength() switch
            {
                3 => doc.RootElement[1].GetString() ?? "<unnamed>",
                2 => doc.RootElement[0].GetString() ?? "<unnamed>",
                _ => "<malformed>",
            };
        }
        catch
        {
            return "<malformed>";
        }
    }

    /// <summary>
    /// Parse the SD-JWT disclosure segments into a flat name→value dictionary.
    /// Each disclosure is base64url(JSON array) of the form <c>[salt, name, value]</c>.
    /// Mandatory-disclosure (no salt) form <c>[name, value]</c> is also tolerated.
    /// </summary>
    internal static Dictionary<string, object?> ParseDisclosures(IReadOnlyList<string> segments)
    {
        var disclosed = new Dictionary<string, object?>(segments.Count);
        foreach (var seg in segments)
        {
            try
            {
                var bytes = Base64Url.DecodeFromChars(seg);
                using var doc = JsonDocument.Parse(bytes);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
                var array = doc.RootElement;
                string? name;
                JsonElement value;
                if (array.GetArrayLength() == 3)
                {
                    name = array[1].GetString();
                    value = array[2];
                }
                else if (array.GetArrayLength() == 2)
                {
                    name = array[0].GetString();
                    value = array[1];
                }
                else continue;
                if (string.IsNullOrEmpty(name)) continue;
                disclosed[name] = value.Clone();
            }
            catch
            {
                // Malformed disclosures are dropped; the required-claim check downstream
                // will surface the problem to the caller.
            }
        }
        return disclosed;
    }

    // ─────────────────────────── delegation chain ────────────────────────────────

    private async Task<List<string>> ValidateDelegationAsync(
        string delegationCredential,
        JsonElement holderJwk,
        LayerState layerState,
        CancellationToken ct)
    {
        var errors = new List<string>();

        var payload = TryParseJwtPayload(delegationCredential);
        if (payload is null)
        {
            errors.Add("Delegation credential is malformed.");
            return errors;
        }

        // Verify expiry
        var now = _clock.GetUtcNow().ToUnixTimeSeconds();
        var exp = payload.Value.TryGetProperty("exp", out var expEl) && expEl.ValueKind == JsonValueKind.Number
            ? expEl.GetInt64() : (long?)null;
        if (exp is null || exp <= now)
        {
            errors.Add("Delegation credential is expired or missing exp claim.");
            return errors;
        }

        // Verify signature with holder key
        if (!VerifyJwsSignature(delegationCredential, holderJwk, out _))
        {
            errors.Add("Delegation credential signature verification failed against holder key.");
            return errors;
        }

        // Check status list bit if present. Feature 138 US1 — the status list MUST authenticate against
        // the issuing org's sealed-state key, pinned to the credential's own iss, and freshness-checked;
        // anything other than a verified "Active" fails closed (Revoked OR Unverifiable both reject).
        if (payload.Value.TryGetProperty("status", out var status)
            && status.ValueKind == JsonValueKind.Object
            && status.TryGetProperty("status_list", out var sl)
            && sl.ValueKind == JsonValueKind.Object
            && sl.TryGetProperty("uri", out var uriEl) && uriEl.ValueKind == JsonValueKind.String
            && sl.TryGetProperty("idx", out var idxEl) && idxEl.ValueKind == JsonValueKind.Number)
        {
            // Feature 155 — record the status reference + verdict for the Revocation layer. The presence
            // of a status_list block is what makes the Revocation layer surface at all (a credential with
            // no status reference omits the layer entirely rather than fabricating a Pass).
            layerState.StatusListUri = uriEl.GetString();
            layerState.StatusListIndex = idxEl.GetInt32();

            var expectedIssuer = TryGetString(payload.Value, "iss");
            if (string.IsNullOrEmpty(expectedIssuer))
            {
                // No issuer to pin the status list to — cannot authenticate revocation. Fail closed.
                errors.Add("Delegation credential is missing iss; cannot authenticate its status list.");
                _metrics?.PresentationReplayRejected("revoked_at_verify");
                layerState.Revocation = StatusListVerdict.Unverifiable;
                return errors;
            }

            var verdict = await _statusListCache.CheckAsync(
                uriEl.GetString()!, idxEl.GetInt32(), expectedIssuer, ct);
            layerState.Revocation = verdict;
            switch (verdict)
            {
                case StatusListVerdict.Revoked:
                    errors.Add("Delegation credential has been revoked via status list.");
                    _metrics?.PresentationReplayRejected("revoked_at_verify");
                    break;
                case StatusListVerdict.Unverifiable:
                    errors.Add("Delegation credential status list could not be authenticated; failing closed.");
                    _metrics?.PresentationReplayRejected("revoked_at_verify");
                    break;
                case StatusListVerdict.Active:
                default:
                    break;
            }
        }

        return errors;
    }

    // ─────────────────────────── JWS verification ────────────────────────────────

    /// <summary>
    /// Verify a compact JWS using a JWK, dispatching on the protected-header <c>alg</c>:
    /// <c>ES256</c> over an EC P-256 (<c>kty:"EC"</c>) key, or <c>EdDSA</c> over an Ed25519
    /// (<c>kty:"OKP"</c>, <c>crv:"Ed25519"</c>) key. Returns true and the deserialised payload
    /// on success; false otherwise.
    ///
    /// <para>Both algorithms are needed because every key in the chain can be either curve:
    /// the device/KB-JWT key is always WebCrypto P-256, but the holder key (delegation signer)
    /// and the issuer key derive from the underlying wallet algorithm — and the default Sorcha
    /// wallet is Ed25519. An ES256-only verifier rejected every Ed25519-holder presentation with
    /// "signature verification failed against holder key" even though the chain was sound.</para>
    /// </summary>
    internal static bool VerifyJwsSignature(string compactJws, JsonElement publicJwk, out JsonElement payload)
    {
        payload = default;
        try
        {
            var parts = compactJws.Split('.');
            if (parts.Length != 3) return false;

            var headerBytes = Base64Url.DecodeFromChars(parts[0]);
            var header = JsonSerializer.Deserialize<JsonElement>(headerBytes);
            var alg = header.TryGetProperty("alg", out var a) && a.ValueKind == JsonValueKind.String
                ? a.GetString() : null;

            var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            var signature = Base64Url.DecodeFromChars(parts[2]);

            bool verified = alg switch
            {
                "ES256" => VerifyEs256(publicJwk, signingInput, signature),
                "ES256K" => VerifyEs256k(publicJwk, signingInput, signature),
                "EdDSA" => VerifyEdDsa(publicJwk, signingInput, signature),
                _ => false,
            };
            if (!verified) return false;

            var payloadBytes = Base64Url.DecodeFromChars(parts[1]);
            payload = JsonSerializer.Deserialize<JsonElement>(payloadBytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Verify an ES256 (ECDSA P-256, SHA-256) JWS against an EC JWK.</summary>
    private static bool VerifyEs256(JsonElement publicJwk, byte[] signingInput, byte[] signature)
    {
        var x = publicJwk.TryGetProperty("x", out var xe) ? xe.GetString() : null;
        var y = publicJwk.TryGetProperty("y", out var ye) ? ye.GetString() : null;
        if (x is null || y is null) return false;

        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Base64Url.DecodeFromChars(x),
                Y = Base64Url.DecodeFromChars(y),
            },
        });
        return ecdsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256);
    }

    /// <summary>
    /// Verify an ES256K (ECDSA secp256k1, SHA-256) JWS. When the JWK carries an <c>x</c>/<c>y</c> key
    /// it is a direct key-match (Phase 1). When it carries only a <c>blockchainAccountId</c> — an
    /// address-form issuer (<c>did:pkh</c> / address-form <c>did:ethr</c>, Feature 178) with no
    /// published key — verification is by public-key recovery against that address.
    /// </summary>
    private static bool VerifyEs256k(JsonElement publicJwk, byte[] signingInput, byte[] signature)
    {
        if (Secp256k1Jwk.TryParse(publicJwk, out var key))
        {
            return Secp256k1Verifier.VerifyEs256k(signingInput, signature, key!);
        }

        if (publicJwk.ValueKind == JsonValueKind.Object
            && publicJwk.TryGetProperty("blockchainAccountId", out var accountId)
            && accountId.ValueKind == JsonValueKind.String
            && accountId.GetString() is { Length: > 0 } address)
        {
            return Secp256k1Verifier.VerifyByAddressStatic(signingInput, signature, address);
        }

        return false;
    }

    /// <summary>
    /// Verify an EdDSA (Ed25519) JWS against an OKP JWK. The JOSE EdDSA signature is the raw
    /// 64-byte Ed25519 signature and the JWK <c>x</c> is the raw 32-byte public key — exactly
    /// what BouncyCastle's pure-managed <see cref="Ed25519Signer"/> consumes (WASM-safe; no
    /// libsodium P/Invoke).
    /// </summary>
    private static bool VerifyEdDsa(JsonElement publicJwk, byte[] signingInput, byte[] signature)
    {
        var crv = publicJwk.TryGetProperty("crv", out var ce) ? ce.GetString() : null;
        if (!string.Equals(crv, "Ed25519", StringComparison.Ordinal)) return false;
        var x = publicJwk.TryGetProperty("x", out var xe) ? xe.GetString() : null;
        if (x is null) return false;

        var publicKey = Base64Url.DecodeFromChars(x);
        if (publicKey.Length != Ed25519PublicKeyParameters.KeySize) return false;

        var verifier = new Ed25519Signer();
        verifier.Init(forSigning: false, new Ed25519PublicKeyParameters(publicKey, 0));
        verifier.BlockUpdate(signingInput, 0, signingInput.Length);
        return verifier.VerifySignature(signature);
    }

    /// <summary>Reads the protected-header <c>alg</c> from a compact JWS; null if unavailable.</summary>
    private static string? HeaderAlg(string jwt)
        => TryParseJwtHeader(jwt) is { } h ? TryGetString(h, "alg") : null;

    /// <summary>
    /// Summarise a public JWK as a compact "kty/crv" label for the verdict-trail Detail. Tolerates
    /// both EC (P-256) and OKP (Ed25519); returns null members it cannot read rather than throwing —
    /// the verdict trail must never break verification.
    /// </summary>
    private static string? DescribeJwk(JsonElement jwk)
    {
        if (jwk.ValueKind != JsonValueKind.Object) return null;
        var kty = jwk.TryGetProperty("kty", out var k) ? k.GetString() : null;
        var crv = jwk.TryGetProperty("crv", out var c) ? c.GetString() : null;
        return (kty, crv) switch
        {
            (null, null) => null,
            (_, null) => kty,
            _ => $"{kty} / {crv}",
        };
    }
}

/// <summary>
/// Outcome of the issuer-signature check, captured for the IssuerSignature validation layer
/// (Feature 155). Distinguishes "resolved + verified" / "resolved + failed" from the two
/// unresolved-key dispositions so the layer can map to Pass / Fail / Unverified.
/// </summary>
internal enum IssuerLayer
{
    /// <summary>The issuer-signature check was not reached (earlier reject).</summary>
    NotChecked,

    /// <summary>The issuer key resolved and the credential's JWS verified against it → Pass.</summary>
    ResolvedVerified,

    /// <summary>A key resolved but the JWS failed to verify against it → Fail.</summary>
    ResolvedFailed,

    /// <summary>No key resolved and the verifier requires the issuer signature → Fail (rejects).</summary>
    UnresolvedRequired,

    /// <summary>No key resolved and the verifier does not require it → Unverified (v1 offline path).</summary>
    UnresolvedNotRequired,
}

/// <summary>
/// Mutable accumulator for the per-layer verdict trail (Feature 155). The validator fills these raw
/// inputs as <c>ValidateAsync</c> progresses, then <see cref="Build"/> turns them into the structured
/// <see cref="ValidationLayerResult"/> list attached to every <see cref="VerificationOutcome"/>. It
/// records only what was actually determined before a return — fields left unset cause their layer to be
/// omitted (the contract forbids fabricating a Pass for a check that never ran).
/// </summary>
internal sealed class LayerState
{
    // LivePresentation
    public bool KbJwtVerified;
    public bool LivePresentationFailed;
    public string? KbNonce;
    public string? KbAudience;
    public string? KbJwtAlg;
    public double? KbJwtAgeSeconds;
    public string? HolderKey;     // credential cnf.jwk "kty / crv" (e.g. "OKP / Ed25519")
    public string? DelegationAlg; // device-delegation JWS header alg (e.g. "EdDSA")
    public string? DeviceKey;     // delegation cnf.jwk "kty / crv" (always "EC / P-256" today)
    public bool ServerCustody;    // #1195 Phase 2 — no delegation; the credential's holder key signs the KB-JWT directly

    // IssuerSignature
    public IssuerLayer IssuerSignature = IssuerLayer.NotChecked;
    public string? Issuer;
    public string? IssuerKid;
    public string? IssuerAlg;
    public string? CredentialId; // the credential's jti — used by the verifier app's register-anchor lookup

    // Revocation — null when the credential/delegation carries no status reference (layer omitted).
    public StatusListVerdict? Revocation;
    public string? StatusListUri;
    public int? StatusListIndex;

    public IReadOnlyList<ValidationLayerResult> Build(
        bool accepted, IReadOnlyList<string> errors, VerifierSession session)
    {
        var layers = new List<ValidationLayerResult>(3);

        // ── LivePresentation ─────────────────────────────────────────────────────
        //   The KB-JWT signature + delegation chain held (KbJwtVerified) and no nonce/aud/freshness
        //   problem was recorded ⇒ Pass; otherwise Fail. If we never reached the KB-JWT verification the
        //   layer is omitted (an earlier structural reject — there is no live presentation to describe).
        if (KbJwtVerified)
        {
            var live = !LivePresentationFailed;
            var detail = new Dictionary<string, string>
            {
                ["protocol"] = "OpenID4VP · direct_post",
                ["nonce"] = string.Equals(KbNonce, session.Nonce, StringComparison.Ordinal)
                    ? "matches request"
                    : $"mismatch (expected '{session.Nonce}', got '{KbNonce ?? "(none)"}')",
                ["aud"] = KbAudience ?? session.ClientId,
            };
            var kbJwtNote = $"{KbJwtAlg ?? "ES256"} · holder key bound";
            if (KbJwtAgeSeconds is { } age)
            {
                kbJwtNote += $" · age {age:0}s";
            }
            detail["kb-jwt"] = kbJwtNote;
            // Surface the holder/device key curves + the delegation algorithm. The holder key curve is
            // the diagnostic that explains Ed25519-vs-P-256 behaviour (the default Sorcha wallet is
            // Ed25519, so its credential cnf.jwk is OKP / Ed25519 and the delegation is EdDSA-signed).
            if (!string.IsNullOrEmpty(HolderKey)) detail["holder-key"] = HolderKey;
            if (ServerCustody)
            {
                // Honest verdict trail — there is genuinely no delegation in server-custody. Say so
                // rather than fabricate a delegation line; the holder key (above) signed the KB-JWT.
                detail["delegation"] = "server-custody (none) · holder cnf signs KB-JWT";
            }
            else if (!string.IsNullOrEmpty(DelegationAlg) || !string.IsNullOrEmpty(DeviceKey))
            {
                detail["delegation"] =
                    $"{DelegationAlg ?? "?"} · device key {DeviceKey ?? "?"}";
            }

            layers.Add(new ValidationLayerResult
            {
                Layer = ValidationLayer.LivePresentation,
                Status = live ? LayerStatus.Pass : LayerStatus.Fail,
                Headline = live ? "Live holder presentation verified" : "Live presentation check failed",
                Detail = detail,
            });
        }

        // ── IssuerSignature ──────────────────────────────────────────────────────
        if (IssuerSignature != IssuerLayer.NotChecked)
        {
            var (status, headline, note) = IssuerSignature switch
            {
                IssuerLayer.ResolvedVerified =>
                    (LayerStatus.Pass, "Issuer signature verified", "issuer key resolved; JWS verified"),
                IssuerLayer.ResolvedFailed =>
                    (LayerStatus.Fail, "Issuer signature invalid", "issuer key resolved; JWS verification failed"),
                IssuerLayer.UnresolvedRequired =>
                    (LayerStatus.Fail, "Issuer key could not be resolved",
                        "issuer key unresolved and RequireIssuerSignature is enabled"),
                IssuerLayer.UnresolvedNotRequired =>
                    (LayerStatus.Unverified, "Issuer signature not verified",
                        "issuer key unresolved; accepted on holder→device chain (RequireIssuerSignature off)"),
                _ => (LayerStatus.Unverified, "Issuer signature not verified", "not checked"),
            };

            var detail = new Dictionary<string, string>
            {
                ["iss"] = Issuer ?? "(unknown)",
                ["resolution"] = note,
            };
            if (!string.IsNullOrEmpty(IssuerKid)) detail["kid"] = IssuerKid;
            detail["alg"] = IssuerAlg ?? "ES256";
            if (!string.IsNullOrEmpty(CredentialId)) detail["jti"] = CredentialId;

            layers.Add(new ValidationLayerResult
            {
                Layer = ValidationLayer.IssuerSignature,
                Status = status,
                Headline = headline,
                Detail = detail,
            });
        }

        // ── Revocation ───────────────────────────────────────────────────────────
        //   Only surfaced when the credential/delegation carried a status reference. Active→Pass,
        //   Revoked→Fail, Unverifiable→Unverified.
        if (Revocation is { } verdict)
        {
            var (status, headline) = verdict switch
            {
                StatusListVerdict.Active => (LayerStatus.Pass, "Not revoked"),
                StatusListVerdict.Revoked => (LayerStatus.Fail, "Credential revoked"),
                StatusListVerdict.Unverifiable => (LayerStatus.Unverified, "Revocation status unverifiable"),
                _ => (LayerStatus.Unverified, "Revocation status unverifiable"),
            };

            var detail = new Dictionary<string, string>
            {
                ["statusList"] = StatusListUri ?? "(none)",
                ["idx"] = StatusListIndex?.ToString() ?? "(none)",
                ["result"] = verdict.ToString(),
            };

            layers.Add(new ValidationLayerResult
            {
                Layer = ValidationLayer.Revocation,
                Status = status,
                Headline = headline,
                Detail = detail,
            });
        }

        return layers;
    }
}
