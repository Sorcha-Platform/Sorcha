// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.Net.Http.Json;
using System.Text.Json;
using Sorcha.Agent.Haip;
using Sorcha.Cryptography.SdJwt;

namespace Sorcha.Agent.Commands;

/// <summary>
/// The "haip present" command — presents a stored credential via the OID4VP direct_post flow.
/// </summary>
public class HaipPresentCommand : Command
{
    public HaipPresentCommand() : base("present", "Present a credential to a HAIP verifier via direct_post")
    {
        var requestUriOption = new Option<string>("--request-uri")
        {
            Description = "OpenID4VP Authorization Request URI",
            Required = true
        };
        var credentialOption = new Option<string>("--credential")
        {
            Description = "Credential type to present (e.g., VerifiedIdentityCredential)",
            Required = true
        };
        var discloseOption = new Option<string>("--disclose")
        {
            Description = "Comma-separated claim names to disclose",
            Required = true
        };
        var walletDirOption = new Option<string>("--wallet-dir")
        {
            Description = "Wallet directory for keys and credentials",
            DefaultValueFactory = _ => "./wallet"
        };

        Options.Add(requestUriOption);
        Options.Add(credentialOption);
        Options.Add(discloseOption);
        Options.Add(walletDirOption);

        this.SetAction(async (parseResult, cancellationToken) =>
        {
            var requestUri = parseResult.GetValue(requestUriOption)!;
            var credentialType = parseResult.GetValue(credentialOption)!;
            var disclose = parseResult.GetValue(discloseOption)!;
            var walletDir = parseResult.GetValue(walletDirOption) ?? "./wallet";

            return await ExecuteAsync(requestUri, credentialType, disclose, walletDir, cancellationToken);
        });
    }

    private static async Task<int> ExecuteAsync(
        string requestUri, string credentialType, string disclose,
        string walletDir, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"[haip present] Wallet dir: {walletDir}");
            Console.WriteLine($"[haip present] Credential: {credentialType}");
            Console.WriteLine($"[haip present] Disclosing: {disclose}");

            // Step 1: Load credential from wallet
            var wallet = new CredentialWallet(walletDir);
            var rawCredential = await wallet.LoadAsync(credentialType);
            if (rawCredential == null)
            {
                Console.Error.WriteLine($"[ERROR] Credential '{credentialType}' not found in wallet");
                Console.Error.WriteLine($"  Available: {string.Join(", ", wallet.ListTypes())}");
                return 1;
            }

            // Step 2: Load holder key
            var keyManager = new HolderKeyManager(walletDir);
            var holderKey = keyManager.GetOrCreateKey();
            Console.WriteLine("[haip present] Holder key loaded");

            using var httpClient = new HttpClient();

            // Step 3: Fetch the request object
            Console.WriteLine($"[haip present] Fetching request object from {requestUri}");
            JsonElement requestObject;
            try
            {
                requestObject = await httpClient.GetFromJsonAsync<JsonElement>(requestUri, ct);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Failed to fetch request object: {ex.Message}");
                return 2;
            }

            var nonce = requestObject.GetProperty("nonce").GetString()!;
            var audience = requestObject.GetProperty("client_id").GetString()!;
            var responseUri = requestObject.GetProperty("response_uri").GetString()!;
            var state = requestObject.GetProperty("state").GetString()!;

            Console.WriteLine($"[haip present] Nonce: {nonce[..8]}..., Audience: {audience}");

            // Step 4: Build presentation with selective disclosure
            var claimsToDisclose = disclose.Split(',', StringSplitOptions.TrimEntries);
            var sdJwtService = new SdJwtService();

            // Create presentation with KB-JWT
            var presentation = await sdJwtService.CreatePresentationAsync(
                rawCredential,
                claimsToDisclose,
                kbJwtSigner: (data, _) =>
                {
                    var sig = holderKey.SignData(data, System.Security.Cryptography.HashAlgorithmName.SHA256);
                    return Task.FromResult(sig);
                },
                holderAlgorithm: "ES256",
                audience: audience,
                nonce: nonce);

            Console.WriteLine($"[haip present] Presentation built, {presentation.SelectedDisclosures.Count} disclosures, KB-JWT present");

            // Step 5: Submit via direct_post
            var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("vp_token", presentation.RawPresentation),
                new KeyValuePair<string, string>("state", state),
                new KeyValuePair<string, string>("presentation_submission", JsonSerializer.Serialize(new
                {
                    id = Guid.NewGuid().ToString(),
                    definition_id = $"pd-{state}",
                    descriptor_map = new[]
                    {
                        new { id = credentialType, format = "vc+sd-jwt", path = "$" }
                    }
                }))
            });

            Console.WriteLine($"[haip present] Submitting via direct_post to {responseUri}");
            var response = await httpClient.PostAsync(responseUri, formContent, ct);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine();
                Console.WriteLine("=== Presentation Accepted ===");
                Console.WriteLine($"  Credential: {credentialType}");
                Console.WriteLine($"  Disclosed:  {disclose}");
                Console.WriteLine($"  Verifier:   {audience}");
                Console.WriteLine("=============================");
                return 0;
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                Console.Error.WriteLine($"[ERROR] Presentation rejected ({response.StatusCode}): {errorBody}");
                return 3;
            }
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
}
