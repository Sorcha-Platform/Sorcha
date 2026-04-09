// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SimpleBase;
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

    public SorchaDidResolver(IWalletServiceClient walletClient, ILogger<SorchaDidResolver> logger)
    {
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
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

        WalletInfo? wallet;
        try
        {
            wallet = await _walletClient.GetWalletAsync(address, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve organization DID {Did}", did);
            return null;
        }

        if (wallet is null)
        {
            _logger.LogWarning("Wallet not found for organization DID {Did}", did);
            return null;
        }

        return BuildDidDocument(did, wallet);
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
    /// multicodec identifier fall back to <c>publicKeyJwk</c>.
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

        var multibase = TryEncodeMultibase(wallet.Algorithm, wallet.PublicKey);
        if (multibase is not null)
        {
            verificationMethod.PublicKeyMultibase = multibase;
        }
        else
        {
            // Algorithm not in the multicodec table (for example PQC): fall back to a
            // minimal JWK carrying the raw key so external consumers still see a key.
            verificationMethod.PublicKeyJwk = BuildFallbackJwk(wallet.Algorithm, wallet.PublicKey);
            _logger.LogDebug(
                "DID {Did} uses algorithm {Algorithm} which has no multicodec prefix — emitting publicKeyJwk fallback",
                did, wallet.Algorithm);
        }

        return new DidDocument
        {
            Id = did,
            VerificationMethod = [verificationMethod],
            Authentication = [keyId],
            AssertionMethod = [keyId]
        };
    }

    // --- Multibase encoding helpers ---

    // Multicodec identifiers from https://github.com/multiformats/multicodec/blob/master/table.csv
    private const int Ed25519PubCodec = 0xed;   // varint: 0xed 0x01
    private const int P256PubCodec = 0x1200;    // varint: 0x80 0x24
    private const int RsaPubCodec = 0x1205;     // varint: 0x85 0x24

    /// <summary>
    /// Produces a W3C-valid <c>publicKeyMultibase</c> value, or null if the algorithm has
    /// no assigned multicodec identifier. The stored <c>wallet.PublicKey</c> can be either
    /// base64 (production path, see <c>WalletEndpoints.cs</c>) or hex (legacy). The helper
    /// tries base64 first and falls back to hex parsing.
    /// </summary>
    private static string? TryEncodeMultibase(string algorithm, string? publicKey)
    {
        if (string.IsNullOrWhiteSpace(publicKey)) return null;

        var codec = algorithm?.ToUpperInvariant() switch
        {
            "ED25519" => (int?)Ed25519PubCodec,
            "NISTP256" or "NIST-P256" or "P-256" or "P256" or "ECDSA-P256" => P256PubCodec,
            "RSA" or "RSA4096" or "RSA-4096" => RsaPubCodec,
            _ => null
        };

        if (codec is null) return null;

        var rawKey = DecodePublicKeyBytes(publicKey);
        if (rawKey is null) return null;

        var varintPrefix = EncodeUnsignedVarint(codec.Value);
        var prefixed = new byte[varintPrefix.Length + rawKey.Length];
        Buffer.BlockCopy(varintPrefix, 0, prefixed, 0, varintPrefix.Length);
        Buffer.BlockCopy(rawKey, 0, prefixed, varintPrefix.Length, rawKey.Length);

        // SimpleBase.Base58.Bitcoin uses the Bitcoin base58 alphabet, identical to base58btc.
        return "z" + Base58.Bitcoin.Encode(prefixed);
    }

    /// <summary>
    /// Decodes <c>wallet.PublicKey</c> to raw bytes. Tries base64 first (canonical format
    /// since the <c>WalletEndpoints</c> rewrite); falls back to hex.
    /// </summary>
    private static byte[]? DecodePublicKeyBytes(string publicKey)
    {
        // Base64 first — it's the canonical storage format per WalletEndpoints.cs.
        try
        {
            return Convert.FromBase64String(publicKey);
        }
        catch (FormatException)
        {
            // Fall through.
        }

        // Hex fallback — old wallets may still have hex-encoded public keys.
        try
        {
            return Convert.FromHexString(publicKey);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Encodes an unsigned integer as an unsigned LEB128 / multiformats varint.
    /// </summary>
    private static byte[] EncodeUnsignedVarint(int value)
    {
        Span<byte> buffer = stackalloc byte[5];
        var length = 0;
        var remaining = (uint)value;
        while (remaining >= 0x80)
        {
            buffer[length++] = (byte)((remaining & 0x7f) | 0x80);
            remaining >>= 7;
        }
        buffer[length++] = (byte)remaining;
        return buffer[..length].ToArray();
    }

    /// <summary>
    /// Builds a minimal <c>publicKeyJwk</c> for algorithms without a multicodec identifier.
    /// </summary>
    private static JsonElement BuildFallbackJwk(string algorithm, string? publicKey)
    {
        var kty = algorithm?.ToUpperInvariant() switch
        {
            "ED25519" => "OKP",
            "NISTP256" or "NIST-P256" or "P-256" or "P256" or "ECDSA-P256" => "EC",
            "RSA" or "RSA4096" or "RSA-4096" => "RSA",
            _ => "oct"
        };

        var jwk = new Dictionary<string, object?>
        {
            ["kty"] = kty,
            ["alg"] = algorithm ?? "unknown",
            ["k"] = publicKey ?? string.Empty
        };

        return JsonSerializer.SerializeToElement(jwk);
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
