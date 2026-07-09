// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Sorcha.Cryptography.Secp256k1;

namespace Sorcha.Cryptography.SdJwt;

/// <summary>
/// SD-JWT VC implementation per RFC 9901.
/// Creates, verifies, and presents SD-JWT tokens with selective disclosure.
/// </summary>
/// <remarks>
/// Wraps HeroSD-JWT library for core SD-JWT operations.
/// Falls back to manual implementation if the library is unavailable.
/// </remarks>
public class SdJwtService : ISdJwtService
{
    private static readonly TimeSpan DefaultClockSkew = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Records a verification error in both the legacy <see cref="SdJwtVerificationResult.Errors"/>
    /// string list (kept for backward compatibility) and the typed
    /// <see cref="SdJwtVerificationResult.ErrorDetails"/> list (issue #221 item 2).
    /// New consumers should branch on <see cref="SdJwtErrorKind"/> via ErrorDetails;
    /// existing log call sites and substring-asserting tests continue to use Errors.
    /// </summary>
    private static void AddError(SdJwtVerificationResult result, SdJwtErrorKind kind, string message)
    {
        result.Errors.Add(message);
        result.ErrorDetails.Add(new SdJwtError { Kind = kind, Message = message });
    }

    /// <inheritdoc />
    public Task<SdJwtToken> CreateTokenAsync(
        Dictionary<string, object> claims,
        IEnumerable<string>? disclosableClaims,
        string issuer,
        string subject,
        byte[] signingKey,
        string algorithm,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<byte[]>? x5cChain = null,
        string? kid = null)
    {
        return CreateTokenCoreAsync(claims, disclosableClaims, issuer, subject, signingKey,
            algorithm, holderJwk: null, expiresAt, x5cChain, kid, externalSigner: null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SdJwtToken> CreateTokenAsync(
        Dictionary<string, object> claims,
        IEnumerable<string>? disclosableClaims,
        string issuer,
        string subject,
        byte[] signingKey,
        string algorithm,
        JsonElement holderJwk,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<byte[]>? x5cChain = null,
        string? kid = null)
    {
        return CreateTokenCoreAsync(claims, disclosableClaims, issuer, subject, signingKey,
            algorithm, holderJwk, expiresAt, x5cChain, kid, externalSigner: null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SdJwtToken> CreateTokenAsync(
        Dictionary<string, object> claims,
        IEnumerable<string>? disclosableClaims,
        string issuer,
        string subject,
        string algorithm,
        Func<byte[], CancellationToken, Task<byte[]>> externalSigner,
        JsonElement? holderJwk,
        string? kid,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<byte[]>? x5cChain = null)
    {
        ArgumentNullException.ThrowIfNull(externalSigner);
        return CreateTokenCoreAsync(claims, disclosableClaims, issuer, subject,
            signingKey: Array.Empty<byte>(), algorithm, holderJwk, expiresAt, x5cChain, kid,
            externalSigner, cancellationToken);
    }

    private async Task<SdJwtToken> CreateTokenCoreAsync(
        Dictionary<string, object> claims,
        IEnumerable<string>? disclosableClaims,
        string issuer,
        string subject,
        byte[] signingKey,
        string algorithm,
        JsonElement? holderJwk,
        DateTimeOffset? expiresAt,
        IReadOnlyList<byte[]>? x5cChain,
        string? kid,
        Func<byte[], CancellationToken, Task<byte[]>>? externalSigner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);

        // signingKey is required when no externalSigner is supplied; ignored otherwise.
        if (externalSigner is null)
        {
            ArgumentNullException.ThrowIfNull(signingKey);
        }

        var disclosableList = disclosableClaims?.ToList() ?? claims.Keys.ToList();

        List<string> disclosures;
        List<string> sdDigests;
        Dictionary<string, object> payloadClaims;

        if (NestedDisclosure.HasNestedPaths(disclosableList))
        {
            // Mixed top-level + nested JSON Pointer paths — use NestedDisclosure
            var (translated, nestedDisclosures, topSd) = NestedDisclosure.Translate(claims, disclosableList);
            disclosures = nestedDisclosures;
            sdDigests = topSd;
            payloadClaims = translated;
        }
        else
        {
            // Top-level name-keyed only — preserve original byte-identical behaviour (FR-021)
            var disclosableSet = disclosableList.ToHashSet();
            disclosures = new List<string>();
            sdDigests = new List<string>();

            foreach (var claimName in disclosableSet)
            {
                if (!claims.TryGetValue(claimName, out var claimValue))
                    continue;

                var disclosure = CreateDisclosure(claimName, claimValue);
                disclosures.Add(disclosure);

                var digest = ComputeDisclosureDigest(disclosure);
                sdDigests.Add(digest);
            }

            // Non-disclosable claims go directly into the payload
            payloadClaims = new Dictionary<string, object>();
            foreach (var (key, value) in claims)
            {
                if (!disclosableSet.Contains(key))
                    payloadClaims[key] = value;
            }
        }

        // Build the JWT payload
        var payload = new Dictionary<string, object>
        {
            ["iss"] = issuer,
            ["sub"] = subject,
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["_sd_alg"] = "sha-256"
        };

        if (expiresAt.HasValue)
            payload["exp"] = expiresAt.Value.ToUnixTimeSeconds();

        // Add cnf (confirmation) claim with holder's public key if provided (FR-001, FR-002)
        // cnf is always non-disclosable (FR-003)
        if (holderJwk.HasValue)
        {
            var cnf = new Dictionary<string, object>
            {
                ["jwk"] = holderJwk.Value
            };
            payload["cnf"] = cnf;
        }

        // Add processed claims to payload
        foreach (var (key, value) in payloadClaims)
        {
            payload[key] = value;
        }

        // Add SD digests
        if (sdDigests.Count > 0)
            payload["_sd"] = sdDigests;

        // Build the JWT header
        var header = new Dictionary<string, object>
        {
            ["alg"] = MapAlgorithm(algorithm),
            ["typ"] = "vc+sd-jwt"
        };

        // Feature 120 — kid header for issuer-key resolution. Production callers pass
        // the versioned form (`did:sorcha:org:{addr}#vc-issuance-{n}`) so verifiers can
        // resolve the matching VerificationMethod via DidResolverBackedIssuerKeyResolver.
        if (!string.IsNullOrEmpty(kid))
        {
            header["kid"] = kid;
        }

        // Feature 096 US3 — x5c chain, if the caller supplied one. RFC 7515 §4.1.6
        // mandates base64 (NOT base64url) encoding for x5c entries.
        if (x5cChain is { Count: > 0 })
        {
            header["x5c"] = x5cChain.Select(Convert.ToBase64String).ToArray();
        }

        // Sign the JWT
        var headerB64 = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadB64 = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = $"{headerB64}.{payloadB64}";
        var signingInputBytes = Encoding.UTF8.GetBytes(signingInput);
        var signature = externalSigner is not null
            ? await externalSigner(signingInputBytes, cancellationToken).ConfigureAwait(false)
            : Sign(signingInputBytes, signingKey, algorithm);
        var signatureB64 = Base64UrlEncode(signature);

        // Assemble the SD-JWT: header.payload.signature~disclosure1~disclosure2~
        var parts = new List<string> { $"{headerB64}.{payloadB64}.{signatureB64}" };
        parts.AddRange(disclosures);
        var rawToken = string.Join("~", parts) + "~";

        var token = new SdJwtToken
        {
            Header = header,
            Payload = payload,
            Disclosures = disclosures,
            Signature = signatureB64,
            RawToken = rawToken
        };

        return token;
    }

    /// <inheritdoc />
    public Task<SdJwtVerificationResult> VerifyTokenAsync(
        string rawToken,
        byte[] issuerPublicKey,
        string algorithm,
        CancellationToken cancellationToken = default,
        string? issuerRecoveryAddress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        ArgumentNullException.ThrowIfNull(issuerPublicKey);

        var result = new SdJwtVerificationResult();

        try
        {
            var parts = rawToken.TrimEnd('~').Split('~');
            if (parts.Length < 1)
            {
                AddError(result, SdJwtErrorKind.InvalidFormat, "Invalid SD-JWT format: no JWT part found");
                return Task.FromResult(result);
            }

            var jwtPart = parts[0];
            var disclosures = parts.Length > 1 ? parts[1..] : [];

            // Parse and verify JWT signature
            var jwtSegments = jwtPart.Split('.');
            if (jwtSegments.Length != 3)
            {
                AddError(result, SdJwtErrorKind.InvalidFormat, "Invalid JWT format: expected 3 segments");
                return Task.FromResult(result);
            }

            var signingInput = $"{jwtSegments[0]}.{jwtSegments[1]}";
            var signatureBytes = Base64UrlDecode(jwtSegments[2]);

            if (!Verify(Encoding.UTF8.GetBytes(signingInput), signatureBytes, issuerPublicKey, algorithm, issuerRecoveryAddress))
            {
                AddError(result, SdJwtErrorKind.SignatureInvalid, "Invalid signature");
                return Task.FromResult(result);
            }

            // Parse payload
            var payloadJson = Base64UrlDecode(jwtSegments[1]);
            var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson)
                ?? new Dictionary<string, JsonElement>();

            // Extract standard claims
            if (payload.TryGetValue("iss", out var iss))
                result.Issuer = iss.GetString();
            if (payload.TryGetValue("sub", out var sub))
                result.Subject = sub.GetString();
            if (payload.TryGetValue("iat", out var iat))
                result.IssuedAt = DateTimeOffset.FromUnixTimeSeconds(iat.GetInt64());
            if (payload.TryGetValue("exp", out var exp))
                result.ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64());

            // Extract cnf (confirmation) claim if present
            if (payload.TryGetValue("cnf", out var cnf) &&
                cnf.ValueKind == JsonValueKind.Object &&
                cnf.TryGetProperty("jwk", out var jwk))
            {
                result.CnfJwk = jwk.Clone();
            }

            // Extract non-SD claims
            var reservedClaims = new HashSet<string> { "iss", "sub", "iat", "exp", "_sd", "_sd_alg", "cnf" };
            foreach (var (key, value) in payload)
            {
                if (!reservedClaims.Contains(key))
                    result.Claims[key] = ConvertJsonElement(value);
            }

            // Process disclosures
            foreach (var disclosure in disclosures)
            {
                if (string.IsNullOrWhiteSpace(disclosure))
                    continue;

                try
                {
                    var disclosureJson = Base64UrlDecode(disclosure);
                    var disclosureArray = JsonSerializer.Deserialize<JsonElement[]>(disclosureJson);
                    if (disclosureArray is { Length: 3 })
                    {
                        var claimName = disclosureArray[1].GetString() ?? string.Empty;
                        var claimValue = ConvertJsonElement(disclosureArray[2]);
                        result.Claims[claimName] = claimValue;
                    }
                }
                catch
                {
                    AddError(result, SdJwtErrorKind.DisclosureIntegrityFailure,
                        $"Failed to parse disclosure: {disclosure[..Math.Min(20, disclosure.Length)]}...");
                }
            }

            result.IsValid = result.Errors.Count == 0;
        }
        catch (Exception ex)
        {
            AddError(result, SdJwtErrorKind.VerificationException, $"Verification failed: {ex.Message}");
        }

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<SdJwtPresentation> CreatePresentationAsync(
        string rawToken,
        IEnumerable<string> claimsToDisclose,
        byte[]? holderKey = null,
        string? audience = null,
        string? nonce = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        ArgumentNullException.ThrowIfNull(claimsToDisclose);

        var parts = rawToken.TrimEnd('~').Split('~');
        var jwtPart = parts[0];
        var allDisclosures = parts.Length > 1 ? parts[1..] : Array.Empty<string>();

        var selectedDisclosures = SelectDisclosures(jwtPart, allDisclosures, claimsToDisclose);

        // Build presentation: jwt~selected_disclosure1~selected_disclosure2~[kb-jwt]
        var presentationParts = new List<string> { jwtPart };
        presentationParts.AddRange(selectedDisclosures);
        var rawPresentation = string.Join("~", presentationParts) + "~";

        var presentation = new SdJwtPresentation
        {
            Token = new SdJwtToken { RawToken = rawToken },
            SelectedDisclosures = selectedDisclosures,
            RawPresentation = rawPresentation
        };

        return Task.FromResult(presentation);
    }

    /// <inheritdoc />
    public Task<SdJwtVerificationResult> VerifyPresentationAsync(
        string rawPresentation,
        byte[] issuerPublicKey,
        string algorithm,
        CancellationToken cancellationToken = default,
        string? issuerRecoveryAddress = null)
    {
        // A presentation is verified the same way as a token —
        // only the selected disclosures are present, so only those claims are extracted.
        return VerifyTokenAsync(rawPresentation, issuerPublicKey, algorithm, cancellationToken, issuerRecoveryAddress);
    }

    /// <inheritdoc />
    /// <remarks>
    /// This overload always builds a KB-JWT. The caller is responsible for not invoking
    /// this method on legacy credentials that do not contain a <c>cnf</c> claim — a KB-JWT
    /// appended to such a credential is harmless (verifiers ignore it per FR-006) but adds
    /// unnecessary bytes and signing overhead.
    /// </remarks>
    public async Task<SdJwtPresentation> CreatePresentationAsync(
        string rawToken,
        IEnumerable<string> claimsToDisclose,
        Func<byte[], CancellationToken, Task<byte[]>> kbJwtSigner,
        string holderAlgorithm,
        string audience,
        string nonce,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        ArgumentNullException.ThrowIfNull(claimsToDisclose);
        ArgumentNullException.ThrowIfNull(kbJwtSigner);
        ArgumentException.ThrowIfNullOrWhiteSpace(holderAlgorithm);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        // Build the presentation without KB-JWT first
        var parts = rawToken.TrimEnd('~').Split('~');
        var jwtPart = parts[0];
        var allDisclosures = parts.Length > 1 ? parts[1..] : Array.Empty<string>();

        var selectedDisclosures = SelectDisclosures(jwtPart, allDisclosures, claimsToDisclose);

        // Build the presentation prefix (everything before the KB-JWT)
        var presentationParts = new List<string> { jwtPart };
        presentationParts.AddRange(selectedDisclosures);
        var presentationPrefix = string.Join("~", presentationParts) + "~";

        // Compute sd_hash: base64url(sha256(presentationPrefix))
        var sdHash = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(presentationPrefix)));

        // Build the KB-JWT
        var kbHeader = new Dictionary<string, object>
        {
            ["alg"] = MapAlgorithm(holderAlgorithm),
            ["typ"] = "kb+jwt"
        };

        var kbPayload = new Dictionary<string, object>
        {
            ["aud"] = audience,
            ["nonce"] = nonce,
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["sd_hash"] = sdHash
        };

        var kbHeaderB64 = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(kbHeader));
        var kbPayloadB64 = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(kbPayload));
        var kbSigningInput = Encoding.UTF8.GetBytes($"{kbHeaderB64}.{kbPayloadB64}");

        var kbSignature = await kbJwtSigner(kbSigningInput, cancellationToken);
        var kbSignatureB64 = Base64UrlEncode(kbSignature);
        var kbJwt = $"{kbHeaderB64}.{kbPayloadB64}.{kbSignatureB64}";

        // Assemble final presentation: prefix + kb-jwt
        var rawPresentation = presentationPrefix + kbJwt;

        return new SdJwtPresentation
        {
            Token = new SdJwtToken { RawToken = rawToken },
            SelectedDisclosures = selectedDisclosures,
            KeyBindingJwt = kbJwt,
            RawPresentation = rawPresentation
        };
    }

    /// <inheritdoc />
    public async Task<SdJwtVerificationResult> VerifyPresentationAsync(
        string rawPresentation,
        byte[] issuerPublicKey,
        string algorithm,
        string expectedAudience,
        string expectedNonce,
        CancellationToken cancellationToken = default,
        string? issuerRecoveryAddress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPresentation);
        ArgumentNullException.ThrowIfNull(issuerPublicKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAudience);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedNonce);

        // Split presentation into: jwt~disclosures...~[kb-jwt]
        // The KB-JWT is the last non-empty segment that looks like a JWT (has 2 dots)
        var allParts = rawPresentation.TrimEnd('~').Split('~');
        if (allParts.Length < 1)
        {
            return new SdJwtVerificationResult
            {
                Errors = { "Invalid presentation format" }
            };
        }

        // Detect KB-JWT: last segment with exactly 2 dots
        string? kbJwtRaw = null;
        string[] presentationPartsWithoutKbJwt;
        var lastPart = allParts[^1];
        if (lastPart.Count(c => c == '.') == 2 && allParts.Length > 1 && allParts[0].Count(c => c == '.') == 2)
        {
            // Last segment looks like a JWT and isn't the issuer JWT — it's the KB-JWT
            kbJwtRaw = lastPart;
            presentationPartsWithoutKbJwt = allParts[..^1];
        }
        else
        {
            presentationPartsWithoutKbJwt = allParts;
        }

        // Rebuild the presentation prefix (without KB-JWT) for sd_hash verification and issuer verification
        var presentationPrefix = string.Join("~", presentationPartsWithoutKbJwt) + "~";

        // First verify the issuer JWT + disclosures
        var result = await VerifyTokenAsync(presentationPrefix, issuerPublicKey, algorithm, cancellationToken, issuerRecoveryAddress);
        if (!result.IsValid)
            return result;

        // If the credential has cnf, KB-JWT is mandatory (FR-014)
        if (result.CnfJwk.HasValue)
        {
            if (kbJwtRaw == null)
            {
                result.IsValid = false;
                AddError(result, SdJwtErrorKind.KeyBindingFailure,
                    "Missing KB-JWT: credential has cnf claim but presentation has no Key Binding JWT");
                return result;
            }

            // Parse KB-JWT
            var kbSegments = kbJwtRaw.Split('.');
            if (kbSegments.Length != 3)
            {
                result.IsValid = false;
                AddError(result, SdJwtErrorKind.KeyBindingFailure, "Invalid KB-JWT format");
                return result;
            }

            // Verify KB-JWT signature against cnf.jwk
            var kbSigningInput = Encoding.UTF8.GetBytes($"{kbSegments[0]}.{kbSegments[1]}");
            var kbSignatureBytes = Base64UrlDecode(kbSegments[2]);
            var holderKeyBytes = ExportPublicKeyFromJwk(result.CnfJwk.Value, out var holderAlg);

            if (holderKeyBytes == null)
            {
                result.IsValid = false;
                AddError(result, SdJwtErrorKind.KeyBindingFailure,
                    "Key binding mismatch: cannot extract public key from cnf JWK");
                return result;
            }

            if (!Verify(kbSigningInput, kbSignatureBytes, holderKeyBytes, holderAlg))
            {
                result.IsValid = false;
                AddError(result, SdJwtErrorKind.KeyBindingFailure,
                    "Key binding mismatch: KB-JWT signature invalid against cnf public key");
                return result;
            }

            // Parse KB-JWT payload and validate claims
            var kbPayloadJson = Base64UrlDecode(kbSegments[1]);
            var kbPayload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(kbPayloadJson)
                ?? new Dictionary<string, JsonElement>();

            // Check aud
            var audValue = kbPayload.TryGetValue("aud", out var aud) ? aud.GetString() : null;
            if (audValue != expectedAudience)
            {
                result.IsValid = false;
                AddError(result, SdJwtErrorKind.AudienceMismatch,
                    $"Audience mismatch: KB-JWT aud '{audValue ?? "<missing>"}' does not match expected '{expectedAudience}'");
                return result;
            }

            // Check nonce
            if (!kbPayload.TryGetValue("nonce", out var nonceEl) || nonceEl.GetString() != expectedNonce)
            {
                result.IsValid = false;
                AddError(result, SdJwtErrorKind.NonceMismatch,
                    "Nonce mismatch: KB-JWT nonce does not match expected value");
                return result;
            }

            // Check iat (clock skew ±60s)
            if (!kbPayload.TryGetValue("iat", out var kbIat))
            {
                result.IsValid = false;
                AddError(result, SdJwtErrorKind.ClockSkew, "Clock skew: KB-JWT missing iat claim");
                return result;
            }

            var kbIssuedAt = DateTimeOffset.FromUnixTimeSeconds(kbIat.GetInt64());
            var now = DateTimeOffset.UtcNow;
            if (kbIssuedAt < now - DefaultClockSkew || kbIssuedAt > now + DefaultClockSkew)
            {
                result.IsValid = false;
                AddError(result, SdJwtErrorKind.ClockSkew,
                    $"Clock skew: KB-JWT iat {kbIssuedAt:O} is outside the ±{DefaultClockSkew.TotalSeconds}s window");
                return result;
            }

            // Check sd_hash
            if (!kbPayload.TryGetValue("sd_hash", out var sdHashEl))
            {
                result.IsValid = false;
                AddError(result, SdJwtErrorKind.SdHashMismatch, "sd_hash mismatch: KB-JWT missing sd_hash claim");
                return result;
            }

            var expectedSdHash = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(presentationPrefix)));
            if (sdHashEl.GetString() != expectedSdHash)
            {
                result.IsValid = false;
                AddError(result, SdJwtErrorKind.SdHashMismatch,
                    "sd_hash mismatch: KB-JWT sd_hash does not match presentation content");
                return result;
            }

            result.HolderKeyVerified = true;
        }
        else if (kbJwtRaw != null)
        {
            // Legacy credential without cnf — ignore trailing KB-JWT if present (FR-006 compat)
        }

        return result;
    }

    // --- Internal helpers ---

    /// <summary>
    /// Selects disclosures that match the caller's disclosure request.
    /// Handles three shapes:
    /// <list type="bullet">
    ///   <item>Top-level names like <c>name</c> → match against the claim-name
    ///   field of 3-element disclosures.</item>
    ///   <item>Nested object JSON Pointers like <c>/address/locality</c> →
    ///   match leaf segment against 3-element disclosure names.</item>
    ///   <item>Array-element JSON Pointers like <c>/qualifications/1</c> →
    ///   navigate the JWT payload to the placeholder slot, extract the digest,
    ///   then select the single 2-element disclosure whose hash matches. This
    ///   closes the TODO(094) that previously dumped every array-element
    ///   disclosure into the presentation (full-array disclosure by accident).</item>
    /// </list>
    /// </summary>
    private static List<string> SelectDisclosures(
        string jwtPart,
        string[] allDisclosures,
        IEnumerable<string> claimsToDisclose)
    {
        // Split the requests into those that refer to object fields (by name or
        // nested pointer) and those that refer to array elements (trailing numeric
        // segment pointing at an array slot). We can't just inspect the leaf
        // segment — an object key could be numeric ("2024" as a property name) —
        // so we resolve each path against the JWT payload below.
        var objectFieldNames = new HashSet<string>(StringComparer.Ordinal);
        var pointerPaths = new List<string>();
        foreach (var entry in claimsToDisclose)
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;
            if (entry.StartsWith('/'))
                pointerPaths.Add(entry);
            else
                objectFieldNames.Add(entry);
        }

        // Hash every disclosure once so we can look up by digest without
        // rehashing per request. Two-element disclosures need this to correlate
        // with array placeholders; three-element ones fall through to the name
        // path but still benefit from the pre-computed hash when the caller
        // passes the same path in multiple forms.
        var digestToDisclosure = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var disclosure in allDisclosures)
        {
            if (string.IsNullOrWhiteSpace(disclosure))
                continue;
            var digest = ComputeDisclosureDigest(disclosure);
            digestToDisclosure.TryAdd(digest, disclosure);
        }

        // Resolve every pointer path against the JWT payload. If it lands on an
        // array-element placeholder, record the digest; if it lands on an object
        // field, fall back to leaf-name matching (for 3-element disclosures).
        var arrayDigestRequests = new HashSet<string>(StringComparer.Ordinal);
        if (pointerPaths.Count > 0)
        {
            var payloadRoot = TryExtractPayloadRoot(jwtPart);
            foreach (var path in pointerPaths)
            {
                var segments = NestedDisclosure.ParsePointer(path);
                if (segments.Length == 0)
                    continue;

                var placeholderDigest = payloadRoot.HasValue
                    ? ResolveArrayPlaceholderDigest(payloadRoot.Value, segments)
                    : null;
                if (placeholderDigest is not null)
                {
                    arrayDigestRequests.Add(placeholderDigest);
                }
                else
                {
                    // Object-field disclosure — match by leaf name against
                    // 3-element [salt, name, value] disclosures below.
                    objectFieldNames.Add(segments[^1]);
                }
            }
        }

        var selected = new List<string>();
        foreach (var disclosure in allDisclosures)
        {
            if (string.IsNullOrWhiteSpace(disclosure))
                continue;

            try
            {
                var disclosureJson = Base64UrlDecode(disclosure);
                var disclosureArray = JsonSerializer.Deserialize<JsonElement[]>(disclosureJson);

                if (disclosureArray is { Length: 3 })
                {
                    // Object-field disclosure [salt, name, value].
                    var claimName = disclosureArray[1].GetString();
                    if (claimName is not null && objectFieldNames.Contains(claimName))
                        selected.Add(disclosure);
                }
                else if (disclosureArray is { Length: 2 })
                {
                    // Array-element disclosure [salt, value]. Only include if the
                    // caller explicitly asked for this element's placeholder slot.
                    var digest = ComputeDisclosureDigest(disclosure);
                    if (arrayDigestRequests.Contains(digest))
                        selected.Add(disclosure);
                }
            }
            catch
            {
                // Skip malformed disclosures
            }
        }

        return selected;
    }

    /// <summary>
    /// Decodes the JWT payload segment from a compact-serialised JWT prefix.
    /// Returns null on any parse failure — callers fall back to name-only
    /// disclosure matching when the payload is unreadable.
    /// </summary>
    private static JsonElement? TryExtractPayloadRoot(string jwtPart)
    {
        try
        {
            var segments = jwtPart.Split('.');
            if (segments.Length < 2)
                return null;
            var payloadBytes = Base64UrlDecode(segments[1]);
            var doc = JsonDocument.Parse(payloadBytes);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Walks the payload following <paramref name="segments"/>. Returns the
    /// <c>"..."</c> digest if the final step lands on an array element that
    /// is an SD-JWT placeholder object <c>{"...": "&lt;digest&gt;"}</c>. Returns
    /// null for any other terminal shape (object field, scalar, missing path).
    /// </summary>
    private static string? ResolveArrayPlaceholderDigest(JsonElement root, string[] segments)
    {
        var current = root;
        for (var i = 0; i < segments.Length; i++)
        {
            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(segments[i], out var next))
                    return null;
                current = next;
            }
            else if (current.ValueKind == JsonValueKind.Array)
            {
                if (!int.TryParse(segments[i], System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var idx))
                    return null;
                var length = current.GetArrayLength();
                if (idx < 0 || idx >= length)
                    return null;
                // Navigate to the indexed element.
                var arrIdx = 0;
                JsonElement? child = null;
                foreach (var item in current.EnumerateArray())
                {
                    if (arrIdx == idx)
                    {
                        child = item;
                        break;
                    }
                    arrIdx++;
                }
                if (child is null)
                    return null;
                current = child.Value;
            }
            else
            {
                return null;
            }
        }

        if (current.ValueKind != JsonValueKind.Object)
            return null;

        // Exactly one property named "..." whose value is a string digest.
        string? digest = null;
        var propertyCount = 0;
        foreach (var prop in current.EnumerateObject())
        {
            propertyCount++;
            if (propertyCount > 1)
                return null;
            if (prop.Name != "...")
                return null;
            if (prop.Value.ValueKind != JsonValueKind.String)
                return null;
            digest = prop.Value.GetString();
        }
        return string.IsNullOrEmpty(digest) ? null : digest;
    }

    /// <summary>
    /// Extracts the public key bytes from a JWK JsonElement and determines the algorithm.
    /// Only supports P-256 (EC) and Ed25519 (OKP) — the two algorithms valid for
    /// HAIP holder binding keys.
    /// </summary>
    private static byte[]? ExportPublicKeyFromJwk(JsonElement jwk, out string algorithm)
    {
        if (!jwk.TryGetProperty("kty", out var kty))
        {
            algorithm = string.Empty;
            return null;
        }

        var keyType = kty.GetString();
        switch (keyType)
        {
            case "EC":
            {
                var crv = jwk.TryGetProperty("crv", out var crvEl) ? crvEl.GetString() : null;
                if (crv == "secp256k1")
                {
                    // secp256k1 holder key-binding: return the 65-byte SEC1 point that the ES256K
                    // branch of Verify parses (System.Security.Cryptography cannot import it on Windows).
                    if (Secp256k1Jwk.TryParse(jwk, out var secp))
                    {
                        algorithm = "ES256K";
                        return secp!.ToSec1Uncompressed();
                    }

                    algorithm = string.Empty;
                    return null;
                }

                if (crv != "P-256")
                {
                    algorithm = string.Empty;
                    return null; // Only P-256 and secp256k1 supported for holder binding
                }

                algorithm = "ES256";
                var x = Base64UrlDecode(jwk.GetProperty("x").GetString()!);
                var y = Base64UrlDecode(jwk.GetProperty("y").GetString()!);

                using var ecdsa = ECDsa.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint { X = x, Y = y }
                });
                return ecdsa.ExportSubjectPublicKeyInfo();
            }
            case "OKP":
            {
                var crv = jwk.TryGetProperty("crv", out var crvEl) ? crvEl.GetString() : null;
                if (crv != "Ed25519")
                {
                    algorithm = string.Empty;
                    return null; // Only Ed25519 supported for holder binding
                }

                algorithm = "EdDSA";
                return Base64UrlDecode(jwk.GetProperty("x").GetString()!);
            }
            default:
                algorithm = string.Empty;
                return null;
        }
    }

    /// <summary>
    /// Verifies a compact JWS whose JOSE header embeds the signing public key as a <c>jwk</c> — e.g. an
    /// RFC 9101 §4 request object served as <c>application/oauth-authz-req+jwt</c>. Rejects <c>alg:none</c>,
    /// headers with no embedded key, alg/key-type confusion, and any bad or tampered signature. The payload
    /// is returned ONLY when the signature verifies against the embedded key.
    /// <para>
    /// This proves <b>integrity</b> (the payload was not altered after signing), NOT <b>authenticity</b>:
    /// a substituted-key attacker can re-sign with their own key and it will still verify. Callers MUST pin
    /// the returned <paramref name="jwkThumbprint"/> (RFC 7638) to a known verifier key to close that gap.
    /// </para>
    /// </summary>
    /// <returns><c>true</c> (and sets <paramref name="payload"/> + <paramref name="jwkThumbprint"/>) when the
    /// embedded-key signature is valid; otherwise <c>false</c> with <paramref name="error"/> set.</returns>
    public static bool TryVerifyCompactJwsWithEmbeddedJwk(
        string compactJws, out JsonElement payload, out string? jwkThumbprint, out string? error)
    {
        payload = default;
        jwkThumbprint = null;
        error = null;

        if (string.IsNullOrWhiteSpace(compactJws)) { error = "empty token"; return false; }
        var parts = compactJws.Split('.');
        if (parts.Length != 3) { error = "not a 3-part compact JWS"; return false; }

        JsonElement header;
        try { header = JsonSerializer.Deserialize<JsonElement>(Base64UrlDecode(parts[0])); }
        catch (Exception ex) { error = $"invalid JOSE header: {ex.Message}"; return false; }

        var alg = header.TryGetProperty("alg", out var algEl) ? algEl.GetString() : null;
        if (string.IsNullOrEmpty(alg) || string.Equals(alg, "none", StringComparison.OrdinalIgnoreCase))
        {
            error = $"unsigned or 'alg:none' request object rejected (alg='{alg ?? "<missing>"}')";
            return false;
        }

        if (!header.TryGetProperty("jwk", out var jwk) || jwk.ValueKind != JsonValueKind.Object)
        {
            error = "JOSE header carries no embedded 'jwk' to verify against";
            return false;
        }

        var keyBytes = ExportPublicKeyFromJwk(jwk, out var keyAlg);
        if (keyBytes == null)
        {
            error = "unsupported or invalid embedded jwk (only P-256 EC and Ed25519 OKP are accepted)";
            return false;
        }

        // The declared JOSE alg must match the key we resolved — block alg/key-type confusion.
        if (!string.Equals(MapAlgorithm(alg), keyAlg, StringComparison.Ordinal))
        {
            error = $"header alg '{alg}' does not match embedded key type '{keyAlg}'";
            return false;
        }

        byte[] signature;
        try { signature = Base64UrlDecode(parts[2]); }
        catch (Exception ex) { error = $"invalid signature encoding: {ex.Message}"; return false; }

        var signingInput = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        bool ok;
        try { ok = Verify(signingInput, signature, keyBytes, keyAlg); }
        catch (Exception ex) { error = $"signature verification error: {ex.Message}"; return false; }
        if (!ok) { error = "request object signature is invalid"; return false; }

        try { payload = JsonSerializer.Deserialize<JsonElement>(Base64UrlDecode(parts[1])); }
        catch (Exception ex) { error = $"invalid payload: {ex.Message}"; return false; }

        jwkThumbprint = ComputeJwkThumbprint(jwk);
        return true;
    }

    /// <summary>RFC 7638 JWK thumbprint — base64url SHA-256 of the canonical required members.</summary>
    private static string? ComputeJwkThumbprint(JsonElement jwk)
    {
        if (!jwk.TryGetProperty("kty", out var ktyEl)) return null;
        var canonical = ktyEl.GetString() switch
        {
            "OKP" => $"{{\"crv\":\"{jwk.GetProperty("crv").GetString()}\",\"kty\":\"OKP\",\"x\":\"{jwk.GetProperty("x").GetString()}\"}}",
            "EC" => $"{{\"crv\":\"{jwk.GetProperty("crv").GetString()}\",\"kty\":\"EC\",\"x\":\"{jwk.GetProperty("x").GetString()}\",\"y\":\"{jwk.GetProperty("y").GetString()}\"}}",
            _ => null,
        };
        return canonical is null ? null : Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string CreateDisclosure(string claimName, object claimValue)
    {
        var salt = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        var disclosure = JsonSerializer.Serialize(new object[] { salt, claimName, claimValue });
        return Base64UrlEncode(Encoding.UTF8.GetBytes(disclosure));
    }

    private static string ComputeDisclosureDigest(string disclosure)
    {
        var bytes = Encoding.ASCII.GetBytes(disclosure);
        var hash = SHA256.HashData(bytes);
        return Base64UrlEncode(hash);
    }

    private static string MapAlgorithm(string algorithm)
    {
        return algorithm.ToUpperInvariant() switch
        {
            "EDDSA" or "ED25519" => "EdDSA",
            "ES256" or "P-256" or "P256" => "ES256",
            "ES256K" or "SECP256K1" => "ES256K",
            "RS256" or "RSA" or "RSA-4096" => "RS256",
            _ => algorithm
        };
    }

    private static byte[] Sign(byte[] data, byte[] privateKey, string algorithm)
    {
        var alg = algorithm.ToUpperInvariant();

        if (alg is "EDDSA" or "ED25519")
        {
            // Use libsodium Ed25519 signing via Sodium.Core
            return Sodium.PublicKeyAuth.SignDetached(data, privateKey);
        }

        if (alg is "ES256" or "P-256" or "P256")
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportECPrivateKey(privateKey, out _);
            return ecdsa.SignData(data, HashAlgorithmName.SHA256);
        }

        if (alg is "RS256" or "RSA" or "RSA-4096")
        {
            using var rsa = RSA.Create();
            rsa.ImportRSAPrivateKey(privateKey, out _);
            return rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        throw new NotSupportedException($"Unsupported signing algorithm: {algorithm}");
    }

    private static bool Verify(byte[] data, byte[] signature, byte[] publicKey, string algorithm,
        string? issuerRecoveryAddress = null)
    {
        var alg = algorithm.ToUpperInvariant();

        if (alg is "EDDSA" or "ED25519")
        {
            return Sodium.PublicKeyAuth.VerifyDetached(signature, data, publicKey);
        }

        if (alg is "ES256" or "P-256" or "P256")
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }

        if (alg is "ES256K" or "SECP256K1")
        {
            // Address-form issuer (did:pkh / address-form did:ethr, Feature 178): no published key —
            // verify the ES256K signature by public-key recovery against the DID's Ethereum address.
            if (!string.IsNullOrEmpty(issuerRecoveryAddress))
            {
                return Secp256k1Verifier.VerifyByAddressStatic(data, signature, issuerRecoveryAddress);
            }

            // secp256k1 is not reliably supported by System.Security.Cryptography.ECDsa on Windows/WASM,
            // so verification is delegated to the pure-managed BouncyCastle primitive. The public key is
            // the 65-byte SEC1 uncompressed point produced by the issuer-key resolver for secp256k1.
            try
            {
                return Secp256k1Verifier.VerifyEs256k(data, signature, Secp256k1PublicKey.FromSec1(publicKey));
            }
            catch
            {
                return false;
            }
        }

        if (alg is "RS256" or "RSA" or "RSA-4096")
        {
            using var rsa = RSA.Create();
            rsa.ImportRSAPublicKey(publicKey, out _);
            return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        throw new NotSupportedException($"Unsupported verification algorithm: {algorithm}");
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Base64Url.EncodeToString(data);
    }

    private static byte[] Base64UrlDecode(string base64Url)
    {
        return Base64Url.DecodeFromChars(base64Url);
    }

    private static object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => element.GetRawText()
        };
    }
}
