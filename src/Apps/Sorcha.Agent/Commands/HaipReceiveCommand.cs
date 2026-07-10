// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Sorcha.Agent.Haip;

namespace Sorcha.Agent.Commands;

/// <summary>
/// The "haip receive" command — receives a credential via the OID4VCI pre-authorized code flow.
/// </summary>
public class HaipReceiveCommand : Command
{
    public HaipReceiveCommand() : base("receive", "Receive a credential from a HAIP issuer via pre-authorized code flow")
    {
        var offerUriOption = new Option<string>("--offer-uri")
        {
            Description = "OpenID4VCI Credential Offer URI",
            Required = true
        };
        var keyFileOption = new Option<string?>("--key-file")
        {
            Description = "Path to holder key PEM file (default: ./wallet/holder-key.pem)"
        };
        var walletDirOption = new Option<string>("--wallet-dir")
        {
            Description = "Wallet directory for keys and credentials",
            DefaultValueFactory = _ => "./wallet"
        };

        Options.Add(offerUriOption);
        Options.Add(keyFileOption);
        Options.Add(walletDirOption);

        this.SetAction(async (parseResult, cancellationToken) =>
        {
            var offerUri = parseResult.GetValue(offerUriOption)!;
            var walletDir = parseResult.GetValue(walletDirOption) ?? "./wallet";

            return await ExecuteAsync(offerUri, walletDir, cancellationToken);
        });
    }

    private static async Task<int> ExecuteAsync(string offerUri, string walletDir, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"[haip receive] Wallet dir: {walletDir}");

            // Step 1: Parse the credential offer URI
            var offerJson = ExtractOfferJson(offerUri);
            if (offerJson == null)
            {
                Console.Error.WriteLine("[ERROR] Invalid credential offer URI");
                return 1;
            }

            var offer = JsonSerializer.Deserialize<JsonElement>(offerJson);
            var issuerUrl = offer.GetProperty("credential_issuer").GetString()!;
            Console.WriteLine($"[haip receive] Issuer: {issuerUrl}");

            // Extract pre-authorized code from grants
            string? preAuthCode = null;
            if (offer.TryGetProperty("grants", out var grants))
            {
                foreach (var grant in grants.EnumerateObject())
                {
                    if (grant.Name.Contains("pre-authorized_code") &&
                        grant.Value.TryGetProperty("pre-authorized_code", out var code))
                    {
                        preAuthCode = code.GetString();
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(preAuthCode))
            {
                Console.Error.WriteLine("[ERROR] No pre-authorized code found in offer");
                return 1;
            }

            // Step 2: Generate or load holder key
            var keyManager = new HolderKeyManager(walletDir);
            var holderKey = keyManager.GetOrCreateKey();
            var holderJwk = keyManager.GetPublicJwk();
            Console.WriteLine("[haip receive] Holder key ready");

            using var httpClient = new HttpClient();

            // Step 3: Fetch issuer metadata
            var metadataUrl = $"{issuerUrl}/.well-known/openid-credential-issuer";
            Console.WriteLine($"[haip receive] Fetching metadata from {metadataUrl}");

            JsonElement metadata;
            try
            {
                metadata = await httpClient.GetFromJsonAsync<JsonElement>(metadataUrl, ct);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Failed to fetch issuer metadata: {ex.Message}");
                return 4;
            }

            var tokenEndpoint = metadata.GetProperty("token_endpoint").GetString()!;

            // Step 4: Exchange pre-authorized code for access token
            Console.WriteLine("[haip receive] Exchanging pre-authorized code...");
            var tokenContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:pre-authorized_code"),
                new KeyValuePair<string, string>("pre-authorized_code", preAuthCode)
            });

            var tokenResponse = await httpClient.PostAsync(tokenEndpoint, tokenContent, ct);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                var errorBody = await tokenResponse.Content.ReadAsStringAsync(ct);
                Console.Error.WriteLine($"[ERROR] Token exchange failed ({tokenResponse.StatusCode}): {errorBody}");
                return 2;
            }

            var tokenResult = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
            var accessToken = tokenResult.GetProperty("access_token").GetString()!;
            var cNonce = tokenResult.GetProperty("c_nonce").GetString()!;
            Console.WriteLine("[haip receive] Access token received");

            // Step 5: Build JWT proof of possession
            var credentialIssuer = metadata.GetProperty("credential_issuer").GetString()!;
            var jwtProof = JwtProofBuilder.Build(holderKey, holderJwk, credentialIssuer, cNonce);
            Console.WriteLine("[haip receive] JWT proof constructed");

            // Step 6: Request credential
            var credentialEndpoint = metadata.GetProperty("credential_endpoint").GetString()!;
            var credentialRequest = new
            {
                format = "dc+sd-jwt",
                proof = new
                {
                    proof_type = "jwt",
                    jwt = jwtProof
                }
            };

            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var credentialResponse = await httpClient.PostAsJsonAsync(credentialEndpoint, credentialRequest, ct);
            if (!credentialResponse.IsSuccessStatusCode)
            {
                var errorBody = await credentialResponse.Content.ReadAsStringAsync(ct);
                Console.Error.WriteLine($"[ERROR] Credential request failed ({credentialResponse.StatusCode}): {errorBody}");
                return 3;
            }

            var credResult = await credentialResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
            var credential = credResult.GetProperty("credential").GetString()!;

            // Step 7: Store credential
            var credentialType = "SorchaCredential"; // Default; could be extracted from vct
            if (offer.TryGetProperty("credentials", out var creds) &&
                creds.GetArrayLength() > 0)
            {
                credentialType = creds[0].GetString() ?? credentialType;
            }

            var wallet = new CredentialWallet(walletDir);
            await wallet.StoreAsync(credentialType, credential);

            // Print summary
            Console.WriteLine();
            Console.WriteLine("=== Credential Received ===");
            Console.WriteLine($"  Type:      {credentialType}");
            Console.WriteLine($"  Issuer:    {credentialIssuer}");
            Console.WriteLine($"  Stored:    {walletDir}/credentials/{credentialType}.sdjwt");
            Console.WriteLine($"  Token len: {credential.Length} chars");

            // Check for cnf
            var parts = credential.TrimEnd('~').Split('~');
            var jwtParts = parts[0].Split('.');
            if (jwtParts.Length >= 2)
            {
                var payloadBytes = System.Buffers.Text.Base64Url.DecodeFromChars(jwtParts[1]);
                var payload = JsonSerializer.Deserialize<JsonElement>(payloadBytes);
                var hasCnf = payload.TryGetProperty("cnf", out _);
                Console.WriteLine($"  cnf:       {(hasCnf ? "present" : "absent")}");
            }

            Console.WriteLine("===========================");
            return 0;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"[ERROR] Network error: {ex.Message}");
            return 4;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Unexpected error: {ex.Message}");
            return 1;
        }
    }

    private static string? ExtractOfferJson(string offerUri)
    {
        // Parse openid-credential-offer://?credential_offer=<json>
        // or openid-credential-offer://?credential_offer_uri=<url>
        if (offerUri.Contains("credential_offer="))
        {
            var start = offerUri.IndexOf("credential_offer=") + "credential_offer=".Length;
            var encoded = offerUri[start..];
            // Remove credential_offer_uri if it follows
            var ampersand = encoded.IndexOf('&');
            if (ampersand >= 0) encoded = encoded[..ampersand];
            return Uri.UnescapeDataString(encoded);
        }

        return null;
    }
}
