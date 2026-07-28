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

        // 3. The single credential ask (the SorchaWallet consumer is single-ask today).
        var dcql = DcqlRequestParser.ParseFromRequestObjectPayload(root);
        if (dcql.Credentials.Count == 0) return null;
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

    // PresentAsync arrives in Task 2.
    /// <inheritdoc />
    public Task<LocalPresentResult> PresentAsync(
        LocalPresentationCandidate candidate,
        IReadOnlyCollection<string> consentedClaims,
        CancellationToken ct = default)
        => throw new NotImplementedException();
}
