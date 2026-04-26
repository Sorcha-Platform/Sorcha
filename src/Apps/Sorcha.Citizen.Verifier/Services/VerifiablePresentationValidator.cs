// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Citizen.Verifier.Services.Models;

namespace Sorcha.Citizen.Verifier.Services;

/// <summary>
/// v1 reference verifier validator. Performs the offline chain check the citizen
/// wallet PWA design requires:
///
/// <list type="number">
///   <item>Parse the SD-JWT VC compact form (<c>credential.disclosure1.disclosureN.kbjwt</c>).</item>
///   <item>Extract the holder JWK from the credential's <c>cnf.jwk</c> claim.</item>
///   <item>Verify the device delegation credential's signature with that holder JWK,
///         extract the device JWK from its <c>cnf.jwk</c>, and check its status-list bit.</item>
///   <item>Verify the KB-JWT signature with the device JWK.</item>
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
    private readonly IStatusListCache _statusListCache;
    private readonly IIssuerKeyResolver _issuerKeys;
    private readonly TimeProvider _clock;
    private readonly ILogger<VerifiablePresentationValidator> _logger;
    private readonly bool _requireIssuerSignature;

    /// <summary>
    /// Initialises a new instance. <paramref name="issuerKeys"/> defaults to
    /// the opt-out resolver via DI registration; <paramref name="requireIssuerSignature"/>
    /// is read from configuration <c>Verifier:RequireIssuerSignature</c>
    /// (default false in v1, true expected in production hardening pass).
    /// </summary>
    public VerifiablePresentationValidator(
        IStatusListCache statusListCache,
        IIssuerKeyResolver issuerKeys,
        TimeProvider clock,
        ILogger<VerifiablePresentationValidator> logger,
        bool requireIssuerSignature = false)
    {
        _statusListCache = statusListCache ?? throw new ArgumentNullException(nameof(statusListCache));
        _issuerKeys = issuerKeys ?? throw new ArgumentNullException(nameof(issuerKeys));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _requireIssuerSignature = requireIssuerSignature;
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

        try
        {
            // ── 1. Parse the SD-JWT VC compact form ───────────────────────────────
            var (credentialJwt, disclosureSegments, kbJwt) = SplitSdJwt(vpToken);
            if (credentialJwt is null)
            {
                errors.Add("vp_token is not a valid SD-JWT compact serialisation.");
                return Failure(errors);
            }
            if (kbJwt is null)
            {
                errors.Add("vp_token is missing the trailing key-binding JWT.");
                return Failure(errors);
            }

            var credentialPayload = TryParseJwtPayload(credentialJwt);
            if (credentialPayload is null)
            {
                errors.Add("vp_token credential JWT is malformed.");
                return Failure(errors);
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
                return Failure(errors);
            }

            // ── 4. Validate holder→device delegation credential ───────────────────
            JsonElement deviceJwk = default;
            if (!string.IsNullOrWhiteSpace(delegationCredential))
            {
                var delegationErrors = await ValidateDelegationAsync(
                    delegationCredential, holderJwk, ct);
                if (delegationErrors.Count > 0)
                {
                    errors.AddRange(delegationErrors);
                    return Failure(errors);
                }

                var delegationPayload = TryParseJwtPayload(delegationCredential);
                if (delegationPayload is null
                    || !TryExtractCnfJwk(delegationPayload.Value, out deviceJwk))
                {
                    errors.Add("Delegation credential is missing cnf.jwk (device key).");
                    return Failure(errors);
                }
            }
            else
            {
                errors.Add("Delegation credential is required for citizen wallet presentations.");
                return Failure(errors);
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
                return Failure(errors);
            }
            var issuerJwk = await _issuerKeys.ResolveAsync(issuer, ct);
            if (issuerJwk is not null)
            {
                if (!VerifyJwsSignature(credentialJwt, issuerJwk.Value, out _))
                {
                    errors.Add($"Credential signature verification failed against issuer '{issuer}' key.");
                    return Failure(errors);
                }
            }
            else if (_requireIssuerSignature)
            {
                errors.Add(
                    $"No public key available for issuer '{issuer}' and " +
                    "RequireIssuerSignature is enabled. Reject.");
                return Failure(errors);
            }
            else
            {
                _logger.LogWarning(
                    "Issuer '{Issuer}' key unresolved; accepting on holder→device chain only " +
                    "(v1 contract; enable Verifier:RequireIssuerSignature to harden).",
                    issuer);
            }

            // ── 5. Verify KB-JWT signature with the device key ────────────────────
            if (!VerifyJwsSignature(kbJwt, deviceJwk, out var kbPayload))
            {
                errors.Add("KB-JWT signature verification failed against device key.");
                return Failure(errors);
            }

            // ── 6. Verify KB-JWT nonce + aud match the session ────────────────────
            var kbNonce = TryGetString(kbPayload, "nonce");
            var kbAudience = TryGetString(kbPayload, "aud");
            if (!string.Equals(kbNonce, session.Nonce, StringComparison.Ordinal))
            {
                errors.Add("KB-JWT nonce does not match session.");
            }
            if (!string.Equals(kbAudience, session.ClientId, StringComparison.Ordinal))
            {
                errors.Add("KB-JWT aud does not match verifier client_id.");
            }

            // ── 7. Extract disclosed claims and check required set ────────────────
            disclosed = ParseDisclosures(disclosureSegments);
            foreach (var required in session.RequiredClaims)
            {
                if (!disclosed.ContainsKey(required))
                {
                    errors.Add($"Required claim '{required}' was not disclosed.");
                }
            }

            if (errors.Count > 0) return Failure(errors);

            _logger.LogInformation(
                "Verifier session {SessionId} accepted: vct={Vct}, claims={Claims}",
                session.SessionId, session.RequiredVct, disclosed.Count);

            return new VerificationOutcome
            {
                Accepted = true,
                DisclosedClaims = disclosed,
                Errors = [],
                CompletedAt = _clock.GetUtcNow(),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Verifier session {SessionId} threw during validation", session.SessionId);
            errors.Add($"Validator exception: {ex.Message}");
            return Failure(errors);
        }
    }

    private VerificationOutcome Failure(IReadOnlyList<string> errors) => new()
    {
        Accepted = false,
        DisclosedClaims = new Dictionary<string, object?>(),
        Errors = errors,
        CompletedAt = _clock.GetUtcNow(),
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

        // Check status list bit if present
        if (payload.Value.TryGetProperty("status", out var status)
            && status.ValueKind == JsonValueKind.Object
            && status.TryGetProperty("status_list", out var sl)
            && sl.ValueKind == JsonValueKind.Object
            && sl.TryGetProperty("uri", out var uriEl) && uriEl.ValueKind == JsonValueKind.String
            && sl.TryGetProperty("idx", out var idxEl) && idxEl.ValueKind == JsonValueKind.Number)
        {
            var revoked = await _statusListCache.IsRevokedAsync(uriEl.GetString()!, idxEl.GetInt32(), ct);
            if (revoked)
            {
                errors.Add("Delegation credential has been revoked via status list.");
            }
        }

        return errors;
    }

    // ─────────────────────────── JWS verification ────────────────────────────────

    /// <summary>
    /// Verify an ES256 JWS using a JWK. Returns true and the deserialised payload on success;
    /// false otherwise. Only ES256 is supported in v1 (matches the citizen wallet's WebCrypto
    /// non-extractable EC P-256 key).
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
            if (!string.Equals(alg, "ES256", StringComparison.Ordinal)) return false;

            // Reconstruct the EC public key from the JWK
            var x = publicJwk.GetProperty("x").GetString();
            var y = publicJwk.GetProperty("y").GetString();
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

            var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            var signature = Base64Url.DecodeFromChars(parts[2]);

            if (!ecdsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256))
            {
                return false;
            }

            var payloadBytes = Base64Url.DecodeFromChars(parts[1]);
            payload = JsonSerializer.Deserialize<JsonElement>(payloadBytes);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
