// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Services.Credentials;
using Sorcha.UI.Core.Services.HolderKeys;
using Sorcha.Verifier.Engine.Dcql;

namespace Sorcha.UI.Core.Services.User.Presentation;

/// <summary>
/// Browser-side port of the proven server-custody presentation flow
/// (demos/AIAS/rehearse.ps1 Complete-SorchaWalletPresentation; PWA Present.razor). Lets a web
/// citizen satisfy a SorchaWallet gate on this device: the holder private key never leaves
/// server custody — the KB-JWT is signed by POST /api/v1/wallet/presentations/sign-kb (#1195
/// Phase 2, Task 6a) and the assembled vp_token is direct_posted to the F127 callback. #1330.
/// </summary>
public sealed class SorchaWalletLocalPresenter : ISorchaWalletLocalPresenter
{
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Api;

    private readonly HttpClient _http;
    private readonly IHolderKeyClient _holderKeys;
    private readonly ICredentialApiService _credentials;
    private readonly TimeProvider _clock;
    private readonly ILogger<SorchaWalletLocalPresenter> _logger;

    /// <summary>Initializes a new instance of <see cref="SorchaWalletLocalPresenter"/>.</summary>
    public SorchaWalletLocalPresenter(
        HttpClient http,
        IHolderKeyClient holderKeys,
        ICredentialApiService credentials,
        TimeProvider clock,
        ILogger<SorchaWalletLocalPresenter> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _holderKeys = holderKeys ?? throw new ArgumentNullException(nameof(holderKeys));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<LocalPresentationCandidate?> ProbeAsync(
        string presentationRequestUri, CancellationToken ct = default)
    {
        // A probe failure of ANY kind degrades to the QR route — it must never block the gate.
        try
        {
            return await ProbeCoreAsync(presentationRequestUri, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Local-presentation probe failed; falling back to QR.");
            return null;
        }
    }

    private async Task<LocalPresentationCandidate?> ProbeCoreAsync(
        string presentationRequestUri, CancellationToken ct)
    {
        // 1. request_uri out of the openid4vp:// deep link.
        var queryStart = presentationRequestUri.IndexOf('?');
        if (queryStart < 0) return null;
        var query = HttpUtility.ParseQueryString(presentationRequestUri[(queryStart + 1)..]);
        var requestUri = query["request_uri"];
        if (string.IsNullOrEmpty(requestUri)) return null;

        // Same-origin or nothing: this client carries the citizen's bearer on every call.
        var requestPath = ToSameOriginRelative(requestUri);
        if (requestPath is null) return null;

        // 2. Fetch + decode the request object (content type application/oauth-authz-req+jwt).
        var requestObjectJwt = await _http.GetStringAsync(requestPath, ct);
        var segments = requestObjectJwt.Split('.');
        if (segments.Length is not (2 or 3) || segments[1].Length == 0) return null;

        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(segments[1]));
        var root = payload.RootElement;
        var clientId = ReadString(root, "client_id");
        var nonce = ReadString(root, "nonce");
        var responseUri = ReadString(root, "response_uri");
        var state = ReadString(root, "state");
        if (clientId is null || nonce is null || responseUri is null || state is null) return null;

        var responsePath = ToSameOriginRelative(responseUri);
        if (responsePath is null)
        {
            _logger.LogWarning("Presentation response_uri is not same-origin; refusing the local route.");
            return null;
        }

        // 3. The single credential ask (the SorchaWallet consumer is single-ask today). A
        //    multi-credential DCQL query (a two-requirement action) cannot be satisfied by local
        //    consent — it only ever presents ONE credential — so refuse the local route entirely
        //    rather than silently verifying just the first (#1330 finding 2, mirror of #1311:
        //    multi-credential local consent is out of scope; degrade to QR, which fails loudly).
        var dcql = DcqlRequestParser.ParseFromRequestObjectPayload(root);
        if (dcql.Credentials.Count != 1) return null;
        var credentialQuery = dcql.Credentials[0];
        var vct = credentialQuery.Meta?.VctValues is { Count: > 0 } vcts ? vcts[0] : null;
        if (string.IsNullOrEmpty(vct)) return null;
        var (required, optional) = DcqlRequestParser.SplitClaims(credentialQuery);

        // 4. The citizen's wallet + algorithm.
        var keys = await _holderKeys.GetHolderKeysAsync(ct);
        if (keys is null) return null;
        var joseAlg = ToJoseAlgorithm(keys.Algorithm);
        if (joseAlg is null) return null;

        // 5. Does the wallet hold a match?
        var requirement = new CredentialRequirement
        {
            Type = vct,
            RequiredClaims = required.Select(n => new ClaimConstraint { ClaimName = n }).ToList(),
        };
        var matches = await _credentials.MatchCredentialsAsync(keys.WalletAddress, [requirement], ct);
        var match = matches.FirstOrDefault(m => m.Matched && !string.IsNullOrEmpty(m.CredentialId));
        if (match is null) return null;

        return new LocalPresentationCandidate
        {
            CredentialId = match.CredentialId!,
            WalletAddress = keys.WalletAddress,
            Vct = vct,
            RequiredClaims = required,
            OptionalClaims = optional,
            Nonce = nonce,
            ClientId = clientId,
            ResponseUri = responsePath,
            QueryId = credentialQuery.Id,
            RequestState = state,
            JoseAlgorithm = joseAlg,
            KidThumbprint = ComputeJwkThumbprint(keys.HolderJwk),
            IssuerDid = match.IssuerDid,
        };
    }

    /// <summary>Absolute same-origin URLs become relative paths; cross-origin returns null.</summary>
    private string? ToSameOriginRelative(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var abs))
            return uri; // already relative — same origin by construction
        var baseAddress = _http.BaseAddress;
        if (baseAddress is null) return null;
        return string.Equals(abs.Authority, baseAddress.Authority, StringComparison.OrdinalIgnoreCase)
            ? abs.PathAndQuery
            : null;
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Wallet algorithm → KB-JWT JOSE alg. Mirrors rehearse.ps1 Get-JoseAlgorithmForWalletAlgorithm.</summary>
    internal static string? ToJoseAlgorithm(string walletAlgorithm) => walletAlgorithm switch
    {
        "ED25519" => "EdDSA",
        "NISTP256" or "NIST-P256" or "P-256" => "ES256",
        _ => null,
    };

    /// <summary>
    /// RFC 7638 JWK thumbprint — EC (crv,kty,x,y) and OKP (crv,kty,x). Mirror of the PWA's
    /// PresentationEngine.ComputeJwkThumbprint (no project reference between the apps).
    /// </summary>
    internal static string ComputeJwkThumbprint(JsonElement jwk)
    {
        var crv = jwk.GetProperty("crv").GetString();
        var kty = jwk.GetProperty("kty").GetString();
        var x = jwk.GetProperty("x").GetString();
        var canonical = string.Equals(kty, "OKP", StringComparison.Ordinal)
            ? $"{{\"crv\":\"{crv}\",\"kty\":\"{kty}\",\"x\":\"{x}\"}}"
            : $"{{\"crv\":\"{crv}\",\"kty\":\"{kty}\",\"x\":\"{x}\",\"y\":\"{jwk.GetProperty("y").GetString()}\"}}";
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <inheritdoc />
    public async Task<LocalPresentResult> PresentAsync(
        LocalPresentationCandidate candidate,
        IReadOnlyCollection<string> consentedClaims,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(consentedClaims);
        try
        {
            return await PresentCoreAsync(candidate, consentedClaims, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local presentation failed for request {State}.", candidate.RequestState);
            return LocalPresentResult.Failed(ex.Message);
        }
    }

    private async Task<LocalPresentResult> PresentCoreAsync(
        LocalPresentationCandidate candidate,
        IReadOnlyCollection<string> consentedClaims,
        CancellationToken ct)
    {
        var consented = new HashSet<string>(consentedClaims, StringComparer.Ordinal);

        // Every required claim must be consented — mirrors PresentationEngine's sanity check.
        foreach (var required in candidate.RequiredClaims)
        {
            if (!consented.Contains(required))
                return LocalPresentResult.Failed($"Required claim '{required}' was not consented.");
        }

        // 1. Export the held credential's raw SD-JWT.
        var export = await _http.GetFromJsonAsync<CredentialExportResponse>(
            $"/api/v1/wallets/{Uri.EscapeDataString(candidate.WalletAddress)}/credentials/{Uri.EscapeDataString(candidate.CredentialId)}/export",
            JsonOptions, ct);
        if (export is null || string.IsNullOrEmpty(export.RawToken))
            return LocalPresentResult.Failed("Credential export returned no raw token.");

        var segments = export.RawToken.Split('~');
        var credentialJwt = segments[0];

        // 2. cnf-binding pre-check (#1330 finding 1): a holder-cnf root and a device-cnf copy can
        //    share the same vct, so /credentials/match cannot tell them apart. If this export is
        //    the device copy, signing the KB-JWT with THIS session's holder key produces a
        //    KB-JWT the shared validator declines server-side ("not signed by the credential's
        //    cnf key") — which CONSUMES the presentation request and kills both the local and QR
        //    routes. Refuse locally, before any HTTP call past the export, when the credential
        //    carries a cnf that doesn't match this session's key. No cnf at all ⇒ legacy
        //    unbound credential, verified against holder custody server-side — proceed.
        using var credentialPayload = ParseJwtPayload(credentialJwt);
        if (credentialPayload is not null &&
            credentialPayload.RootElement.TryGetProperty("cnf", out var cnf) &&
            cnf.ValueKind == JsonValueKind.Object &&
            cnf.TryGetProperty("jwk", out var boundJwk))
        {
            var boundThumbprint = ComputeJwkThumbprint(boundJwk);
            if (!string.Equals(boundThumbprint, candidate.KidThumbprint, StringComparison.Ordinal))
            {
                return LocalPresentResult.Failed(
                    "This credential is bound to another device — scan the QR code with that device instead.");
            }
        }

        // 3. Every disclosure name in the export (not just the consented ones) — needed so we can
        //    pre-check that each REQUIRED claim is actually satisfiable before signing anything.
        //    A required claim missing from both the disclosures and the JWT body would sign a
        //    KB-JWT over a vp_token that can never satisfy the requirement — a doomed direct_post
        //    that consumes the request server-side (mirror of rehearse.ps1:563's guard).
        var allDisclosureNames = new HashSet<string>(StringComparer.Ordinal);
        var selected = new List<string>();
        for (var i = 1; i < segments.Length; i++)
        {
            var seg = segments[i];
            if (string.IsNullOrEmpty(seg) || seg.Count(c => c == '.') == 2) continue;
            if (ReadDisclosureName(seg) is not { } name) continue;
            allDisclosureNames.Add(name);
            if (consented.Contains(name)) selected.Add(seg);
        }

        foreach (var required in candidate.RequiredClaims)
        {
            if (allDisclosureNames.Contains(required)) continue;
            if (credentialPayload is not null && credentialPayload.RootElement.TryGetProperty(required, out _)) continue;
            return LocalPresentResult.Failed($"Required claim '{required}' is not present in this credential.");
        }

        // 4. RFC 9901 sd_hash over the exact to-be-presented prefix (order preserved, trailing ~).
        var hashable = credentialJwt + string.Concat(selected.Select(s => "~" + s)) + "~";
        var sdHash = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(hashable)));

        // 5. KB-JWT signed server-custody: the holder key never leaves the Wallet Service.
        var now = _clock.GetUtcNow();
        var header = new Dictionary<string, object>
        {
            ["alg"] = candidate.JoseAlgorithm,
            ["typ"] = "kb+jwt",
            ["kid"] = candidate.KidThumbprint,
        };
        var kbPayload = new Dictionary<string, object>
        {
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddSeconds(120).ToUnixTimeSeconds(), // Feature 138 US5 window
            ["aud"] = candidate.ClientId,
            ["nonce"] = candidate.Nonce,
            ["sd_hash"] = sdHash,
        };
        var signingInput =
            $"{Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header))}." +
            $"{Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(kbPayload))}";

        using var signResponse = await _http.PostAsJsonAsync(
            "/api/v1/wallet/presentations/sign-kb", new { signingInput }, JsonOptions, ct);
        if (!signResponse.IsSuccessStatusCode)
            return LocalPresentResult.Failed($"sign-kb returned {(int)signResponse.StatusCode}.");
        var sign = await signResponse.Content.ReadFromJsonAsync<KbSignResponse>(JsonOptions, ct);
        if (sign is null || string.IsNullOrEmpty(sign.Signature))
            return LocalPresentResult.Failed("sign-kb returned no signature.");
        if (!string.Equals(sign.Algorithm, candidate.JoseAlgorithm, StringComparison.Ordinal))
        {
            // A silently mismatched alg fails verification downstream with no local error
            // (rehearse.ps1:599 carries the same guard).
            return LocalPresentResult.Failed(
                $"sign-kb signed '{sign.Algorithm}' but the KB-JWT header declares '{candidate.JoseAlgorithm}'.");
        }

        // 6. Assemble + direct_post the OpenID4VP 1.0 object-keyed envelope.
        var vpToken = hashable + $"{signingInput}.{sign.Signature}";
        var envelope = JsonSerializer.Serialize(
            new Dictionary<string, string[]> { [candidate.QueryId] = [vpToken] });
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["vp_token"] = envelope,
            ["state"] = candidate.RequestState,
        });
        using var callback = await _http.PostAsync(candidate.ResponseUri, form, ct);
        if (!callback.IsSuccessStatusCode)
            return LocalPresentResult.Failed($"Presentation callback returned {(int)callback.StatusCode}.");

        var body = await callback.Content.ReadAsStringAsync(ct);
        var kind = ReadKind(body);
        return string.Equals(kind, "Success", StringComparison.OrdinalIgnoreCase)
            ? LocalPresentResult.Submitted()
            : LocalPresentResult.Declined(kind ?? "unknown outcome");
    }

    /// <summary>Claim name of a 3-element disclosure ([salt, name, value]). A 2-element
    /// disclosure is an unnamed array element — never claim-selectable, so null.</summary>
    internal static string? ReadDisclosureName(string segment)
    {
        try
        {
            using var doc = JsonDocument.Parse(Base64Url.DecodeFromChars(segment));
            return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() == 3
                ? doc.RootElement[1].GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Decodes a JWT's payload segment (index 1) to JSON. Null on any parse failure —
    /// callers treat "can't tell" the same as "no cnf claim", never as a hard error.</summary>
    private static JsonDocument? ParseJwtPayload(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2 || parts[1].Length == 0) return null;
        try
        {
            return JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadKind(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String
                ? k.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private sealed class CredentialExportResponse
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? RawToken { get; set; }
    }

    private sealed class KbSignResponse
    {
        public string Signature { get; set; } = string.Empty;
        public string Algorithm { get; set; } = string.Empty;
    }
}
