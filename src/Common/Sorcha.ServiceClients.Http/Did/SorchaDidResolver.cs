// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Http.Utilities;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.ServiceClients.Did;

/// <summary>
/// Resolves Sorcha-native DIDs:
///   - did:sorcha:org:{walletAddress}     — organization identity
///   - did:sorcha:w:{walletAddress}       — wallet identity
///   - did:sorcha:r:{registerId}:t:{txId} — register transaction reference
/// </summary>
public class SorchaDidResolver : IDidResolver
{
    private const string Method = "sorcha";
    private const string WalletPrefix = "did:sorcha:w:";
    private const string RegisterPrefix = "did:sorcha:r:";
    private const string OrgPrefix = "did:sorcha:org:";

    private readonly IWalletServiceClient _walletClient;
    private readonly ILogger<SorchaDidResolver> _logger;

    private readonly HttpClient? _publicDidHttp;

    public SorchaDidResolver(IWalletServiceClient walletClient, ILogger<SorchaDidResolver> logger)
    {
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Constructor variant with an injected <see cref="HttpClient"/> for anonymous DID
    /// resolution via wallet's public <c>/api/v1/wallets/{addr}/did-document</c> endpoint
    /// (Feature 120). Used by services that don't have the service-auth bearer needed
    /// for the auth-gated wallet GET (notably Sorcha.Haip.Service's verifier path).
    /// </summary>
    public SorchaDidResolver(IWalletServiceClient walletClient, HttpClient publicDidHttp, ILogger<SorchaDidResolver> logger)
    {
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _publicDidHttp = publicDidHttp;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool CanResolve(string didMethod) =>
        string.Equals(didMethod, Method, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(did))
            return null;

        if (did.StartsWith(OrgPrefix, StringComparison.OrdinalIgnoreCase))
            return await ResolveOrgDidAsync(did, ct);

        if (did.StartsWith(WalletPrefix, StringComparison.OrdinalIgnoreCase))
            return await ResolveWalletDidAsync(did, ct);

        if (did.StartsWith(RegisterPrefix, StringComparison.OrdinalIgnoreCase))
            return ResolveRegisterDid(did);

        _logger.LogWarning("Unrecognised Sorcha DID format: {Did}", did);
        return null;
    }

    private async Task<DidDocument?> ResolveOrgDidAsync(string did, CancellationToken ct)
    {
        var address = did[OrgPrefix.Length..];
        if (string.IsNullOrWhiteSpace(address))
        {
            _logger.LogWarning("Organization DID has empty address: {Did}", did);
            return null;
        }

        // Feature 149: an org's issuer DID is anchored on its CANONICAL operational wallet (A),
        // but credentials are signed by a DERIVED vc-issuance sub-key (C). The only authoritative
        // {kid -> C's key} mapping is the Tenant-published did.json. Resolve it BY DID. We must NOT
        // rebuild from the wallet row — that would synthesise A's own key under #vc-issuance-{n},
        // which never matches a signature made by C (and would miss rotated keys). No published
        // document -> return null (fail closed).
        if (_publicDidHttp is null)
        {
            _logger.LogWarning(
                "Organization DID {Did} cannot be resolved without a published-DID client (Feature 149); " +
                "returning null (fail closed).", did);
            return null;
        }

        try
        {
            using var resp = await _publicDidHttp
                .GetAsync($"/orgs/by-did/{Uri.EscapeDataString(did)}/did.json", ct)
                .ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var doc = await resp.Content.ReadFromJsonAsync<DidDocument>(ct).ConfigureAwait(false);
                if (doc is null)
                    _logger.LogWarning("Published DID document for {Did} parsed to null", did);
                return doc;
            }

            _logger.LogWarning(
                "Published DID resolution for {Did} returned {Status}", did, resp.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Published DID resolution failed for {Did}", did);
            return null;
        }
    }

    private async Task<DidDocument?> ResolveWalletDidAsync(string did, CancellationToken ct)
    {
        var address = did[WalletPrefix.Length..];
        if (string.IsNullOrWhiteSpace(address))
        {
            _logger.LogWarning("Wallet DID has empty address: {Did}", did);
            return null;
        }

        WalletInfo? wallet;
        try
        {
            wallet = await _walletClient.GetWalletAsync(address, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve wallet DID {Did}", did);
            return null;
        }

        if (wallet is null)
        {
            _logger.LogWarning("Wallet not found for DID {Did}", did);
            return null;
        }

        return BuildDidDocument(did, wallet);
    }

    /// <summary>
    /// Builds a W3C DID Document for a resolved Sorcha wallet. Feature 093 US3 fixes
    /// the previously malformed multibase emission (was <c>"z" + hex</c>, now
    /// <c>"z" + Base58btc(multicodec || rawKey)</c>). Algorithms without an assigned
    /// multicodec identifier fail closed — per FR-014 the resolver MUST NOT emit
    /// malformed multibase OR an incorrectly-shaped publicKeyJwk. Callers receive
    /// a verification method with neither key material field populated and a clear
    /// warning in logs.
    /// </summary>
    private DidDocument BuildDidDocument(string did, WalletInfo wallet)
    {
        var keyId = $"{did}#key-1";
        var keyType = MapAlgorithmToKeyType(wallet.Algorithm);

        var verificationMethod = new VerificationMethod
        {
            Id = keyId,
            Type = keyType,
            Controller = did
        };

        var rawKey = Multicodec.DecodePublicKeyBytes(wallet.PublicKey);
        if (rawKey is null)
        {
            _logger.LogWarning(
                "DID {Did} wallet public key could not be decoded as base64 or hex — emitting DID document with no key material",
                did);
        }
        else
        {
            var multibase = Multicodec.ToMultibasePublicKey(wallet.Algorithm, rawKey);
            if (multibase is not null)
            {
                verificationMethod.PublicKeyMultibase = multibase;
            }

            // Feature 120 — also emit publicKeyJwk so verifiers that only support the
            // JWK form (notably Sorcha.Haip.Service.HaipPresentationVerifier) can read
            // the key material. Wallet-derived multibase + JWK reference the same bytes.
            var jwkJson = TryBuildJwk(wallet.Algorithm, rawKey);
            if (jwkJson is not null)
            {
                using var jwkDoc = System.Text.Json.JsonDocument.Parse(jwkJson);
                verificationMethod.PublicKeyJwk = jwkDoc.RootElement.Clone();
            }
            else
            {
                // Algorithm not in the multicodec table (for example PQC): fail closed.
                // Emitting a synthesised publicKeyJwk here would require per-algorithm
                // JWK construction (OKP x, EC x+y, RSA n+e) that the current resolver
                // cannot do safely from just the raw key bytes without knowing the
                // algorithm's specific encoding rules. Per FR-014, fail-closed is the
                // correct behaviour for unsupported algorithms.
                //
                // LogError (not LogWarning) because a resolved DID document with no key
                // material is unusable for any downstream verification — callers will see
                // "resolution succeeded" but be unable to verify anything. Operators need
                // to know when this happens, not just see it buried in warning noise.
                _logger.LogError(
                    "DID {Did} uses algorithm {Algorithm} which has no multicodec prefix — " +
                    "emitting DID document with no key material. Callers that need the public " +
                    "key for this algorithm should look it up via IWalletServiceClient directly.",
                    did, wallet.Algorithm);
            }
        }

        // Feature 149: BuildDidDocument now serves only wallet DIDs (did:sorcha:w:*).
        // Org issuer keys (#vc-issuance-{n}) are resolved exclusively from the Tenant-published
        // did.json — never synthesised from the wallet row, which holds the wrong key (the
        // operational wallet A's key, not the derived vc-issuance key C that signs credentials).
        var vms = new List<VerificationMethod> { verificationMethod };

        return new DidDocument
        {
            Id = did,
            VerificationMethod = vms,
            Authentication = vms.Select(v => v.Id).ToList(),
            AssertionMethod = vms.Select(v => v.Id).ToList()
        };
    }

    /// <summary>
    /// Best-effort JWK construction from raw public-key bytes for the algorithms
    /// Sorcha currently supports. Returns null on unsupported algorithms or when the
    /// raw byte shape doesn't match the expected encoding.
    /// </summary>
    private static string? TryBuildJwk(string algorithm, byte[] rawKey)
    {
        var b64u = (byte[] b) =>
            Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var algo = algorithm?.ToUpperInvariant();
        if (algo is "ED25519")
            return $"{{\"kty\":\"OKP\",\"crv\":\"Ed25519\",\"x\":\"{b64u(rawKey)}\"}}";
        if (algo is "NIST-P256" or "NISTP256" or "P-256" or "P256" or "ECDSA-P256"
            && rawKey.Length == 65 && rawKey[0] == 0x04)
        {
            var x = b64u(rawKey.AsSpan(1, 32).ToArray());
            var y = b64u(rawKey.AsSpan(33, 32).ToArray());
            return $"{{\"kty\":\"EC\",\"crv\":\"P-256\",\"x\":\"{x}\",\"y\":\"{y}\"}}";
        }
        return null;
    }

    private DidDocument? ResolveRegisterDid(string did)
    {
        // Expected format: did:sorcha:r:{registerId}:t:{txId}
        // Splits to:       [did, sorcha, r, {registerId}, t, {txId}]
        //                    0    1       2  3             4  5
        var parts = did.Split(':');
        if (parts.Length < 4 || !string.Equals(parts[2], "r", StringComparison.Ordinal))
        {
            _logger.LogWarning("Invalid register DID format: {Did}", did);
            return null;
        }

        var registerId = parts[3];
        var txId = parts.Length >= 6 && string.Equals(parts[4], "t", StringComparison.Ordinal)
            ? parts[5]
            : null;

        if (string.IsNullOrWhiteSpace(registerId))
        {
            _logger.LogWarning("Register DID has empty registerId: {Did}", did);
            return null;
        }

        // Build a minimal document referencing the register transaction.
        // Deep transaction parsing is not needed -- callers can use
        // IRegisterServiceClient.GetTransactionAsync for full details.
        var doc = new DidDocument
        {
            Id = did,
            Service =
            [
                new ServiceEndpoint
                {
                    Id = $"{did}#register",
                    Type = "SorchaRegister",
                    Endpoint = $"sorcha:register:{registerId}"
                }
            ]
        };

        if (!string.IsNullOrWhiteSpace(txId))
        {
            doc.Service =
            [
                ..doc.Service,
                new ServiceEndpoint
                {
                    Id = $"{did}#transaction",
                    Type = "SorchaTransaction",
                    Endpoint = $"sorcha:register:{registerId}:tx:{txId}"
                }
            ];
        }

        return doc;
    }

    private static string MapAlgorithmToKeyType(string algorithm) =>
        algorithm.ToUpperInvariant() switch
        {
            "ED25519" => "Ed25519VerificationKey2020",
            "NIST-P256" or "P-256" => "JsonWebKey2020",
            "RSA-4096" or "RSA4096" => "JsonWebKey2020",
            _ => "JsonWebKey2020"
        };
}
