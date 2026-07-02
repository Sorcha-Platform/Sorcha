// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.UI.Core.Services.Credentials;

/// <summary>
/// Default <see cref="IHaipLocalReceiveService"/> — drives the OpenID4VCI
/// pre-authorized-code flow with the user's Sorcha wallet acting as
/// holder. Mirrors <c>Sorcha.Agent.Commands.HaipReceiveCommand</c> but
/// signs JWT proofs through the Wallet Service instead of using a
/// throwaway P-256 key.
/// </summary>
public sealed class HaipLocalReceiveService : IHaipLocalReceiveService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HaipLocalReceiveService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public HaipLocalReceiveService(HttpClient httpClient, ILogger<HaipLocalReceiveService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<HaipLocalReceiveResult> ReceiveLocallyAsync(
        string credentialOfferUri,
        string walletAddress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(credentialOfferUri))
        {
            return HaipLocalReceiveResult.Fail("Credential offer URI is required");
        }
        if (string.IsNullOrWhiteSpace(walletAddress))
        {
            return HaipLocalReceiveResult.Fail("Wallet address is required");
        }

        try
        {
            // 1. Parse the offer URI and pull out issuer + pre-auth code.
            var offerJson = ExtractOfferJson(credentialOfferUri);
            if (offerJson is null)
            {
                return HaipLocalReceiveResult.Fail("Invalid credential offer URI");
            }

            using var offerDoc = JsonDocument.Parse(offerJson);
            var offer = offerDoc.RootElement;

            if (!offer.TryGetProperty("credential_issuer", out var issuerProp))
            {
                return HaipLocalReceiveResult.Fail("Credential offer missing credential_issuer");
            }
            var issuerUrl = issuerProp.GetString()!;

            var preAuthCode = ExtractPreAuthorizedCode(offer);
            if (string.IsNullOrEmpty(preAuthCode))
            {
                return HaipLocalReceiveResult.Fail("Credential offer missing pre-authorized_code");
            }

            // 2. Fetch the wallet detail so we know the public key + algorithm.
            var walletDto = await _httpClient.GetFromJsonAsync<WalletPublicKeyDto>(
                $"/api/v1/wallets/{walletAddress}", JsonDefaults.Api, ct);
            if (walletDto is null || string.IsNullOrEmpty(walletDto.PublicKey))
            {
                return HaipLocalReceiveResult.Fail($"Wallet {walletAddress} not found or missing public key");
            }
            var algorithm = (walletDto.Algorithm ?? "ED25519").ToUpperInvariant();
            if (algorithm != "ED25519")
            {
                // The HAIP issuer also supports ES256 / P-256, but the JWT
                // proof shape differs and Sorcha citizen wallets default to
                // ED25519. Refuse other algorithms here so we don't silently
                // produce a proof the issuer can't verify.
                return HaipLocalReceiveResult.Fail(
                    $"Local receive currently supports ED25519 wallets only (this wallet is {algorithm})");
            }

            // 3. Fetch issuer metadata so we know the token + credential endpoints.
            var metadataUrl = $"{issuerUrl.TrimEnd('/')}/.well-known/openid-credential-issuer";
            var metadata = await _httpClient.GetFromJsonAsync<JsonElement>(metadataUrl, JsonDefaults.Api, ct);
            var tokenEndpoint = metadata.GetProperty("token_endpoint").GetString()!;
            var credentialEndpoint = metadata.GetProperty("credential_endpoint").GetString()!;
            var credentialIssuer = metadata.GetProperty("credential_issuer").GetString()!;

            // 4. Exchange the pre-authorized code for an access token + c_nonce.
            var tokenForm = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:pre-authorized_code"),
                new KeyValuePair<string, string>("pre-authorized_code", preAuthCode)
            });
            var tokenResponse = await _httpClient.PostAsync(tokenEndpoint, tokenForm, ct);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                var body = await tokenResponse.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("HAIP token exchange failed {Status}: {Body}",
                    tokenResponse.StatusCode, body);
                return HaipLocalReceiveResult.Fail(
                    $"Token exchange failed ({(int)tokenResponse.StatusCode})", code: "token_exchange_failed");
            }
            var tokenJson = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Api, ct);
            var accessToken = tokenJson.GetProperty("access_token").GetString()!;
            var cNonce = tokenJson.GetProperty("c_nonce").GetString()!;

            // 5. Build the JWT proof (header + payload), get the wallet to
            //    sign the signing input, assemble compact JWS.
            var publicKeyBytes = Convert.FromBase64String(walletDto.PublicKey);
            var holderJwk = new
            {
                kty = "OKP",
                crv = "Ed25519",
                x = Base64Url.EncodeToString(publicKeyBytes)
            };
            var header = new
            {
                alg = "EdDSA",
                typ = "openid4vci-proof+jwt",
                jwk = holderJwk
            };
            var payload = new
            {
                aud = credentialIssuer,
                iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                nonce = cNonce
            };

            var headerB64 = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header));
            var payloadB64 = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
            var signingInput = Encoding.ASCII.GetBytes($"{headerB64}.{payloadB64}");

            // The Wallet Service /sign endpoint takes base64 transaction
            // data and produces a base64 signature. IsPreHashed=true tells
            // the wallet to sign the transactionData bytes directly without
            // applying an additional SHA-256 pre-hash (see
            // TransactionService.SignTransactionAsync lines 53-56). Ed25519's
            // internal hashing (SHA-512 inside EdDSA) handles the raw JWS
            // signing input correctly, and the HAIP verifier uses
            // Sodium.PublicKeyAuth.VerifyDetached against the raw bytes — so
            // we MUST skip the wallet's extra SHA-256 to produce a signature
            // that verifies.
            var signRequest = new SignRequest
            {
                TransactionData = Convert.ToBase64String(signingInput),
                IsPreHashed = true
            };
            var signResponse = await _httpClient.PostAsJsonAsync(
                $"/api/v1/wallets/{walletAddress}/sign", signRequest, JsonOptions, ct);
            if (!signResponse.IsSuccessStatusCode)
            {
                var body = await signResponse.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Wallet sign failed {Status}: {Body}",
                    signResponse.StatusCode, body);
                return HaipLocalReceiveResult.Fail(
                    $"Wallet signing failed ({(int)signResponse.StatusCode})", code: "wallet_sign_failed");
            }
            var signResult = await signResponse.Content.ReadFromJsonAsync<SignResponse>(JsonDefaults.Api, ct);
            if (signResult is null || string.IsNullOrEmpty(signResult.Signature))
            {
                return HaipLocalReceiveResult.Fail("Wallet sign returned no signature");
            }
            var signatureBytes = Convert.FromBase64String(signResult.Signature);
            var signatureB64 = Base64Url.EncodeToString(signatureBytes);
            var jwtProof = $"{headerB64}.{payloadB64}.{signatureB64}";

            // 6. Call the credential endpoint with the JWT proof.
            using var credentialRequest = new HttpRequestMessage(HttpMethod.Post, credentialEndpoint)
            {
                Content = JsonContent.Create(new
                {
                    format = "vc+sd-jwt",
                    proof = new { proof_type = "jwt", jwt = jwtProof }
                }, options: JsonOptions)
            };
            credentialRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var credentialResponse = await _httpClient.SendAsync(credentialRequest, ct);
            if (!credentialResponse.IsSuccessStatusCode)
            {
                var body = await credentialResponse.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("HAIP credential request failed {Status}: {Body}",
                    credentialResponse.StatusCode, body);
                return HaipLocalReceiveResult.Fail(
                    $"Credential request failed ({(int)credentialResponse.StatusCode})",
                    code: "credential_request_failed");
            }
            var credResult = await credentialResponse.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Api, ct);
            var sdJwt = credResult.GetProperty("credential").GetString()!;

            // 7. Determine the credential type from the offer.
            var credentialType = "SorchaCredential";
            if (offer.TryGetProperty("credentials", out var credsList) &&
                credsList.ValueKind == JsonValueKind.Array && credsList.GetArrayLength() > 0)
            {
                var first = credsList[0];
                credentialType = first.ValueKind switch
                {
                    JsonValueKind.String => first.GetString() ?? credentialType,
                    JsonValueKind.Object when first.TryGetProperty("vct", out var vct) => vct.GetString() ?? credentialType,
                    _ => credentialType
                };
            }

            // 8. POST the SD-JWT into the local credential store on the
            //    user's wallet so it shows up in My Credentials.
            var storePayload = new
            {
                credentialId = $"urn:uuid:{Guid.NewGuid()}",
                type = credentialType,
                issuerDid = credentialIssuer,
                subjectDid = $"did:sorcha:wallet:{walletAddress}",
                claimsJson = "{}",
                issuedAt = DateTimeOffset.UtcNow,
                expiresAt = (DateTimeOffset?)null,
                rawToken = sdJwt
            };
            var storeResponse = await _httpClient.PostAsJsonAsync(
                $"/api/v1/wallets/{walletAddress}/credentials", storePayload, JsonOptions, ct);
            if (!storeResponse.IsSuccessStatusCode)
            {
                var body = await storeResponse.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Local credential store failed {Status}: {Body}",
                    storeResponse.StatusCode, body);
                return HaipLocalReceiveResult.Fail(
                    $"Local store failed ({(int)storeResponse.StatusCode})",
                    code: "local_store_failed");
            }

            _logger.LogInformation(
                "Locally received {Type} into wallet {Wallet} from issuer {Issuer}",
                credentialType, walletAddress, credentialIssuer);

            return HaipLocalReceiveResult.Ok(
                credentialId: storePayload.credentialId,
                credentialType: credentialType,
                issuerUrl: credentialIssuer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during local HAIP receive for wallet {Wallet}", walletAddress);
            return HaipLocalReceiveResult.Fail($"Unexpected error: {ex.Message}");
        }
    }

    internal static string? ExtractOfferJson(string offerUri)
    {
        const string marker = "credential_offer=";
        var idx = offerUri.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var encoded = offerUri[(idx + marker.Length)..];
        var ampersand = encoded.IndexOf('&');
        if (ampersand >= 0) encoded = encoded[..ampersand];
        return Uri.UnescapeDataString(encoded);
    }

    internal static string? ExtractPreAuthorizedCode(JsonElement offer)
    {
        if (!offer.TryGetProperty("grants", out var grants)) return null;
        foreach (var grant in grants.EnumerateObject())
        {
            if (grant.Name == "urn:ietf:params:oauth:grant-type:pre-authorized_code" &&
                grant.Value.TryGetProperty("pre-authorized_code", out var code))
            {
                return code.GetString();
            }
        }
        return null;
    }

    private sealed class WalletPublicKeyDto
    {
        public string? PublicKey { get; set; }
        public string? Algorithm { get; set; }
    }

    private sealed class SignRequest
    {
        public string TransactionData { get; set; } = string.Empty;
        public bool IsPreHashed { get; set; }
    }

    private sealed class SignResponse
    {
        public string? Signature { get; set; }
        public string? PublicKey { get; set; }
    }
}
