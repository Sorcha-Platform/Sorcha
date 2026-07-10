// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
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
        var verifierThumbprintOption = new Option<string?>("--verifier-jwk-thumbprint")
        {
            Description = "RFC 7638 thumbprint of the trusted verifier's signing jwk. When set, the signed " +
                          "request object's key is pinned to it (RFC 9101 §4 trust anchor); a mismatch is refused. " +
                          "When omitted, the signature is still verified for integrity but the key is unpinned."
        };

        Options.Add(requestUriOption);
        Options.Add(credentialOption);
        Options.Add(discloseOption);
        Options.Add(walletDirOption);
        Options.Add(verifierThumbprintOption);

        this.SetAction(async (parseResult, cancellationToken) =>
        {
            var requestUri = parseResult.GetValue(requestUriOption)!;
            var credentialType = parseResult.GetValue(credentialOption)!;
            var disclose = parseResult.GetValue(discloseOption)!;
            var walletDir = parseResult.GetValue(walletDirOption) ?? "./wallet";
            var verifierThumbprint = parseResult.GetValue(verifierThumbprintOption);

            return await ExecuteAsync(requestUri, credentialType, disclose, walletDir, verifierThumbprint, cancellationToken);
        });
    }

    private static async Task<int> ExecuteAsync(
        string requestUri, string credentialType, string disclose,
        string walletDir, string? expectedVerifierJwkThumbprint, CancellationToken ct)
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

            // Step 3: Fetch the request object. Per RFC 9101 §4 the verifier returns a signed JWT
            // (application/oauth-authz-req+jwt). We VERIFY its signature against the JOSE-header-embedded
            // jwk before acting on any claim, and pin to --verifier-jwk-thumbprint when supplied (#344).
            Console.WriteLine($"[haip present] Fetching request object from {requestUri}");
            JsonElement requestObject;
            try
            {
                var responseText = await httpClient.GetStringAsync(requestUri, ct);
                requestObject = ParseRequestObjectPayload(responseText, expectedVerifierJwkThumbprint);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Failed to fetch/verify request object: {ex.Message}");
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

            // Step 5: Submit via direct_post. Feature 181 (T017) — the vp_token is the
            // OpenID4VP 1.0 object envelope keyed by the DCQL query id; the retired
            // presentation_submission is no longer sent (the verifier 400s on it).
            var vpTokenEnvelope = JsonSerializer.Serialize(new Dictionary<string, string[]>
            {
                ["credential"] = [presentation.RawPresentation]
            });
            var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("vp_token", vpTokenEnvelope),
                new KeyValuePair<string, string>("state", state)
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

    /// <summary>
    /// Parses AND verifies the verifier's RFC 9101 §4 request-object response before any claim is used.
    /// A signed JWT (starts with <c>eyJ</c>, <c>application/oauth-authz-req+jwt</c>) has its signature
    /// verified against the JOSE-header-embedded <c>jwk</c> (rejecting <c>alg:none</c> / tampering); when
    /// <paramref name="expectedVerifierJwkThumbprint"/> is supplied the signing key is additionally pinned
    /// to that RFC 7638 thumbprint (else it verifies integrity only and warns that the key is unpinned).
    /// A bare JSON object is accepted as an unsigned legacy body with a warning. Issue #344.
    /// </summary>
    internal static JsonElement ParseRequestObjectPayload(string responseText, string? expectedVerifierJwkThumbprint = null)
    {
        var trimmed = responseText.Trim();

        // Bare JSON object — unsigned. HAIP 1.0 §6.1 mandates a signed request object; accept unsigned
        // only for legacy/local callers, and say so loudly.
        if (trimmed.StartsWith('{'))
        {
            Console.Error.WriteLine(
                "[WARN] Request object is an unsigned JSON body — there is no signature to verify " +
                "(HAIP 1.0 §6.1 expects a signed application/oauth-authz-req+jwt).");
            return JsonSerializer.Deserialize<JsonElement>(trimmed);
        }

        // Compact JWS (base64url JOSE header ⇒ starts "eyJ"). RFC 9101 §4: verify the request-object
        // signature against its embedded jwk BEFORE acting on any claim.
        if (trimmed.StartsWith("eyJ", StringComparison.Ordinal))
        {
            if (!SdJwtService.TryVerifyCompactJwsWithEmbeddedJwk(
                    trimmed, out var payload, out var thumbprint, out var verifyError))
            {
                throw new InvalidOperationException($"Request object signature verification failed: {verifyError}");
            }

            if (!string.IsNullOrWhiteSpace(expectedVerifierJwkThumbprint))
            {
                if (!string.Equals(expectedVerifierJwkThumbprint, thumbprint, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Request object signing key is not the pinned verifier key (expected thumbprint " +
                        $"'{expectedVerifierJwkThumbprint}', got '{thumbprint}'). Refusing to present.");
                }
            }
            else
            {
                Console.Error.WriteLine(
                    $"[WARN] Request object signature verified but the signing key is UNPINNED (jwk thumbprint " +
                    $"'{thumbprint}'). Pass --verifier-jwk-thumbprint to bind to a known verifier (RFC 9101 §4 " +
                    "trust anchor); without it a substituted-key MITM cannot be detected.");
            }

            return payload;
        }

        // Anything else — give the caller a quoted preview so they can see what
        // came back (HTML error page, plaintext error, redirect body, etc.).
        var preview = trimmed.Length <= 20 ? trimmed : trimmed[..20];
        throw new InvalidOperationException(
            $"Request object is neither a JSON body (starting '{{') nor a compact JWT (starting 'eyJ'). " +
            $"First {preview.Length} chars of response: \"{preview}\"");
    }
}
