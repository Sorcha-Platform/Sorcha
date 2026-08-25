// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.CommandLine;
using System.Text;
using System.Text.Json;
using Sorcha.Agent.Haip;
using Sorcha.Cryptography.SdJwt;
using Sorcha.Verifier.Engine;

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
        // #1538 — the verifier authenticates with an X.509 certificate (OpenID4VP 1.0 / HAIP 1.0,
        // Feature 181 US6), NOT a self-asserted embedded jwk. The old --verifier-jwk-thumbprint option
        // is gone: it pinned a key in a header the platform no longer emits, so it could only ever
        // refuse. These options are its x5c-era replacements.
        var verifierClientIdOption = new Option<string?>("--verifier-client-id")
        {
            Description = "Expected verifier client_id in x509_san_dns:{host} form. The certificate's SAN " +
                          "dNSName must match its host or the request is refused. When omitted the client_id " +
                          "is taken from the request object itself, which proves only internal consistency."
        };
        var verifierAnchorOption = new Option<string[]>("--verifier-anchor")
        {
            Description = "Path to a trusted root certificate (PEM or DER) the verifier's chain must reach. " +
                          "Repeatable. With no anchors the best attainable verdict is authentic-but-untrusted.",
            AllowMultipleArgumentsPerToken = true
        };
        var requireTrustedOption = new Option<bool>("--require-trusted-verifier")
        {
            Description = "Refuse unless the verifier certificate chains to one of --verifier-anchor."
        };
        var allowUnverifiedOption = new Option<bool>("--allow-unverified-verifier")
        {
            Description = "Proceed even when the verifier cannot be authenticated at all (no x5c, unsupported " +
                          "alg, or an unsigned request object). Off by default — an unattended agent has no " +
                          "human to weigh the risk."
        };

        Options.Add(requestUriOption);
        Options.Add(credentialOption);
        Options.Add(discloseOption);
        Options.Add(walletDirOption);
        Options.Add(verifierClientIdOption);
        Options.Add(verifierAnchorOption);
        Options.Add(requireTrustedOption);
        Options.Add(allowUnverifiedOption);

        this.SetAction(async (parseResult, cancellationToken) =>
        {
            var requestUri = parseResult.GetValue(requestUriOption)!;
            var credentialType = parseResult.GetValue(credentialOption)!;
            var disclose = parseResult.GetValue(discloseOption)!;
            var walletDir = parseResult.GetValue(walletDirOption) ?? "./wallet";

            RequestObjectTrustPolicy policy;
            try
            {
                policy = new RequestObjectTrustPolicy(
                    ExpectedClientId: parseResult.GetValue(verifierClientIdOption),
                    Anchors: LoadAnchors(parseResult.GetValue(verifierAnchorOption)),
                    RequireTrusted: parseResult.GetValue(requireTrustedOption),
                    AllowUnverified: parseResult.GetValue(allowUnverifiedOption));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.Message}");
                return 2;
            }

            return await ExecuteAsync(requestUri, credentialType, disclose, walletDir, policy, cancellationToken);
        });
    }

    private static async Task<int> ExecuteAsync(
        string requestUri, string credentialType, string disclose,
        string walletDir, RequestObjectTrustPolicy policy, CancellationToken ct)
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

            // Step 3: Fetch and AUTHENTICATE the request object before acting on any claim in it
            // (RFC 9101 §4). Since Feature 181 US6 the verifier signs it with an X.509 certificate and
            // an x5c chain, so authentication is a certificate check — see ParseRequestObjectPayload.
            Console.WriteLine($"[haip present] Fetching request object from {requestUri}");
            JsonElement requestObject;
            try
            {
                var responseText = await httpClient.GetStringAsync(requestUri, ct);
                requestObject = ParseRequestObjectPayload(responseText, policy);
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
    /// Parses AND authenticates the verifier's RFC 9101 §4 request-object response before any claim is used.
    ///
    /// <para>Authentication is delegated to <see cref="RequestObjectValidator"/> — the same implementation the
    /// citizen wallet uses — so the agent and the wallet cannot drift on what counts as an authentic verifier.
    /// It verifies the ES256 JWS against the <c>x5c</c> leaf, matches the leaf SAN dNSName to the
    /// <c>x509_san_dns:</c> client_id host, and walks the chain to a supplied anchor.</para>
    ///
    /// <para><b>Why not the embedded <c>jwk</c> this used to require (#1538):</b> a key the signer puts in its
    /// own header is self-asserted — it proves the payload was not altered after signing, and nothing about
    /// WHO signed it. Feature 181 US6 moved the platform's verifier to X.509, so the header carries an
    /// <c>x5c</c> chain and no <c>jwk</c>; the old check could therefore only ever refuse, which is exactly
    /// how it presented — a fail-closed agent against a correctly-behaving verifier.</para>
    ///
    /// <para><b>Unattended policy.</b> A wallet renders the three-state verdict and lets a human decide; an
    /// agent has no one to ask, so it decides here. A hard refusal (tampered signature, SAN mismatch) always
    /// throws. <c>Unverifiable</c> throws unless <c>--allow-unverified-verifier</c>. <c>AuthenticUntrusted</c>
    /// proceeds with a warning — absent anchors must never block (FR-027) — unless
    /// <c>--require-trusted-verifier</c>.</para>
    /// </summary>
    internal static JsonElement ParseRequestObjectPayload(string responseText, RequestObjectTrustPolicy policy)
    {
        var trimmed = responseText.Trim();

        // Bare JSON object — unsigned. HAIP 1.0 §6.1 mandates a signed request object, and an unsigned one
        // cannot be authenticated at all, so it is refused on the same footing as Unverifiable.
        if (trimmed.StartsWith('{'))
        {
            if (!policy.AllowUnverified)
            {
                throw new InvalidOperationException(
                    "Request object is an unsigned JSON body, so the verifier cannot be authenticated " +
                    "(HAIP 1.0 §6.1 expects a signed application/oauth-authz-req+jwt). Refusing to present. " +
                    "Pass --allow-unverified-verifier to override.");
            }
            Console.Error.WriteLine(
                "[WARN] Request object is an unsigned JSON body — the verifier is UNAUTHENTICATED and " +
                "anything could be on the other end. Proceeding only because --allow-unverified-verifier was set.");
            return JsonSerializer.Deserialize<JsonElement>(trimmed);
        }

        if (!trimmed.StartsWith("eyJ", StringComparison.Ordinal))
        {
            // Anything else — give the caller a quoted preview so they can see what
            // came back (HTML error page, plaintext error, redirect body, etc.).
            var preview = trimmed.Length <= 20 ? trimmed : trimmed[..20];
            throw new InvalidOperationException(
                "Request object is neither a JSON body (starting '{') nor a compact JWT (starting 'eyJ'). " +
                $"First {preview.Length} chars of response: \"{preview}\"");
        }

        // Decode the payload WITHOUT trusting it — the validator needs the client_id to know which host the
        // certificate SAN must match, and it lives in the request object. Nothing acts on this payload until
        // validation below has passed; it is returned only on a successful verdict.
        var payload = DecodeJwsPayload(trimmed);

        var expectedClientId = policy.ExpectedClientId;
        if (string.IsNullOrWhiteSpace(expectedClientId))
        {
            expectedClientId = payload.TryGetProperty("client_id", out var cidEl) ? cidEl.GetString() : null;
            Console.Error.WriteLine(
                $"[WARN] Verifier client_id is UNPINNED — taken from the request object itself " +
                $"('{expectedClientId ?? "<absent>"}'). The certificate SAN is therefore checked against a " +
                "value the signer chose, which proves internal consistency but not identity. Pass " +
                "--verifier-client-id to bind to a verifier you already know.");
        }

        var result = new RequestObjectValidator().Validate(trimmed, expectedClientId, policy.Anchors);

        if (result.Refused)
        {
            var detail = result.RefusalCode == RequestObjectErrorCodes.RequestHostMismatch
                ? $"the certificate's SAN does not match the client_id host ('{expectedClientId}') — this is " +
                  "what a substituted verifier looks like"
                : "the request object is malformed or its signature does not verify (tampering)";
            throw new InvalidOperationException(
                $"Verifier authentication FAILED [{result.RefusalCode}]: {detail}. Refusing to present.");
        }

        var state = result.AuthState!;
        switch (state.Status)
        {
            case VerifierAuthStatus.TrustedListVerified:
                Console.WriteLine(
                    $"[haip present] Verifier authenticated: {state.VerifierHost} " +
                    $"(chains to anchor set '{state.AnchorSetId}')");
                break;

            case VerifierAuthStatus.AuthenticUntrusted when policy.RequireTrusted:
                throw new InvalidOperationException(
                    $"Verifier '{state.VerifierHost}' is authentic but its certificate does not chain to any " +
                    "supplied anchor, and --require-trusted-verifier was set. Refusing to present. " +
                    "Supply the issuing root with --verifier-anchor.");

            case VerifierAuthStatus.AuthenticUntrusted:
                Console.Error.WriteLine(
                    $"[WARN] Verifier '{state.VerifierHost}' is AUTHENTIC BUT UNTRUSTED — its signature and " +
                    "SAN check out, but nothing vouches for the certificate. Supply --verifier-anchor to " +
                    "upgrade, or --require-trusted-verifier to refuse this case outright.");
                break;

            case VerifierAuthStatus.Unverifiable when policy.AllowUnverified:
                Console.Error.WriteLine(
                    $"[WARN] Verifier could NOT be authenticated ({state.Reason}). Proceeding only because " +
                    "--allow-unverified-verifier was set.");
                break;

            case VerifierAuthStatus.Unverifiable:
            default:
                throw new InvalidOperationException(
                    $"Verifier could not be authenticated: {state.Reason}. Refusing to present. " +
                    "Pass --allow-unverified-verifier to override.");
        }

        return payload;
    }

    /// <summary>Decode a compact JWS payload. Parsing is not trusting — the caller must authenticate first.</summary>
    private static JsonElement DecodeJwsPayload(string compactJws)
    {
        var parts = compactJws.Split('.');
        if (parts.Length != 3)
        {
            throw new InvalidOperationException("Request object is not a 3-part compact JWS.");
        }
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(Base64Url.DecodeFromChars(parts[1]));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new InvalidOperationException($"Request object payload is not decodable JSON: {ex.Message}");
        }
    }

    /// <summary>
    /// Load trust anchors from PEM or DER files. Anchors are optional — with none supplied the best
    /// attainable verdict is authentic-but-untrusted, which never blocks (FR-027).
    /// </summary>
    internal static VerifierTrustAnchors? LoadAnchors(string[]? paths)
    {
        if (paths is null || paths.Length == 0) return null;

        var roots = new List<byte[]>();
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Verifier anchor file not found: {path}");
            }

            var bytes = File.ReadAllBytes(path);
            var text = Encoding.ASCII.GetString(bytes);
            if (text.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
            {
                foreach (var block in ExtractPemCertificates(text))
                {
                    roots.Add(block);
                }
            }
            else
            {
                roots.Add(bytes); // DER
            }
        }

        if (roots.Count == 0)
        {
            throw new InvalidOperationException(
                "No certificates could be read from the supplied --verifier-anchor files.");
        }
        return new VerifierTrustAnchors(roots, AnchorSetId: "cli:--verifier-anchor");
    }

    private static IEnumerable<byte[]> ExtractPemCertificates(string pem)
    {
        const string begin = "-----BEGIN CERTIFICATE-----";
        const string end = "-----END CERTIFICATE-----";
        var index = 0;
        while (true)
        {
            var start = pem.IndexOf(begin, index, StringComparison.Ordinal);
            if (start < 0) yield break;
            var stop = pem.IndexOf(end, start, StringComparison.Ordinal);
            if (stop < 0) yield break;

            var body = pem[(start + begin.Length)..stop];
            yield return Convert.FromBase64String(new string(body.Where(c => !char.IsWhiteSpace(c)).ToArray()));
            index = stop + end.Length;
        }
    }
}

/// <summary>
/// Fetch-time trust policy for the verifier's request object (#1538).
/// </summary>
/// <param name="ExpectedClientId">Out-of-band <c>x509_san_dns:{host}</c> client_id to pin, or null.</param>
/// <param name="Anchors">Trusted roots the verifier chain must reach, or null.</param>
/// <param name="RequireTrusted">Refuse anything short of chaining to an anchor.</param>
/// <param name="AllowUnverified">Proceed even when the verifier cannot be authenticated at all.</param>
internal sealed record RequestObjectTrustPolicy(
    string? ExpectedClientId = null,
    VerifierTrustAnchors? Anchors = null,
    bool RequireTrusted = false,
    bool AllowUnverified = false);
