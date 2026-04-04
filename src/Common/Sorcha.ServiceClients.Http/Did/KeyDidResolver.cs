// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using SimpleBase;

namespace Sorcha.ServiceClients.Did;

/// <summary>
/// Resolves did:key DIDs by decoding the multibase/multicodec public key.
///   - did:key:z6Mk...  (z = base58btc, 0xed01 = ED25519)
///   - did:key:zDn...   (z = base58btc, 0x1200 = P-256)
/// No network calls are required -- the public key is embedded in the DID itself.
/// </summary>
public class KeyDidResolver : IDidResolver
{
    private const string Method = "key";
    private const string DidKeyPrefix = "did:key:";

    // Multicodec prefixes (varint-encoded)
    private const byte Ed25519Byte0 = 0xed;
    private const byte Ed25519Byte1 = 0x01;
    private const byte P256Byte0 = 0x12;
    private const byte P256Byte1 = 0x00;

    private const int Ed25519PublicKeyLength = 32;
    private const int P256CompressedPublicKeyLength = 33;

    private readonly ILogger<KeyDidResolver> _logger;

    public KeyDidResolver(ILogger<KeyDidResolver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool CanResolve(string didMethod) =>
        string.Equals(didMethod, Method, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(did) || !did.StartsWith(DidKeyPrefix, StringComparison.Ordinal))
            return Task.FromResult<DidDocument?>(null);

        var multibaseValue = did[DidKeyPrefix.Length..];
        if (multibaseValue.Length < 2)
        {
            _logger.LogWarning("did:key value too short: {Did}", did);
            return Task.FromResult<DidDocument?>(null);
        }

        // Multibase prefix 'z' = base58btc
        if (multibaseValue[0] != 'z')
        {
            _logger.LogWarning("Unsupported multibase prefix '{Prefix}' in {Did}", multibaseValue[0], did);
            return Task.FromResult<DidDocument?>(null);
        }

        byte[] decoded;
        try
        {
            decoded = Base58.Bitcoin.Decode(multibaseValue.AsSpan(1)).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to base58btc-decode did:key {Did}", did);
            return Task.FromResult<DidDocument?>(null);
        }

        if (decoded.Length < 2)
        {
            _logger.LogWarning("Decoded did:key bytes too short: {Did}", did);
            return Task.FromResult<DidDocument?>(null);
        }

        var doc = BuildDocument(did, multibaseValue, decoded);
        return Task.FromResult(doc);
    }

    private DidDocument? BuildDocument(string did, string multibaseValue, byte[] decoded)
    {
        var codec0 = decoded[0];
        var codec1 = decoded[1];
        var keyBytes = decoded[2..];

        if (codec0 == Ed25519Byte0 && codec1 == Ed25519Byte1)
            return BuildEd25519Document(did, multibaseValue, keyBytes);

        if (codec0 == P256Byte0 && codec1 == P256Byte1)
            return BuildP256Document(did, multibaseValue, keyBytes);

        _logger.LogWarning(
            "Unsupported multicodec prefix 0x{Byte0:x2}{Byte1:x2} in {Did}",
            codec0, codec1, did);
        return null;
    }

    private DidDocument? BuildEd25519Document(string did, string multibaseValue, byte[] keyBytes)
    {
        if (keyBytes.Length != Ed25519PublicKeyLength)
        {
            _logger.LogWarning(
                "ED25519 key length mismatch: expected {Expected}, got {Actual} in {Did}",
                Ed25519PublicKeyLength, keyBytes.Length, did);
            return null;
        }

        var keyId = $"{did}#{did[DidKeyPrefix.Length..]}";

        return new DidDocument
        {
            Id = did,
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = keyId,
                    Type = "Ed25519VerificationKey2020",
                    Controller = did,
                    PublicKeyMultibase = multibaseValue
                }
            ],
            Authentication = [keyId],
            AssertionMethod = [keyId]
        };
    }

    private DidDocument? BuildP256Document(string did, string multibaseValue, byte[] keyBytes)
    {
        if (keyBytes.Length != P256CompressedPublicKeyLength)
        {
            _logger.LogWarning(
                "P-256 key length mismatch: expected {Expected}, got {Actual} in {Did}",
                P256CompressedPublicKeyLength, keyBytes.Length, did);
            return null;
        }

        var keyId = $"{did}#{did[DidKeyPrefix.Length..]}";

        return new DidDocument
        {
            Id = did,
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = keyId,
                    Type = "JsonWebKey2020",
                    Controller = did,
                    PublicKeyMultibase = multibaseValue
                }
            ],
            Authentication = [keyId],
            AssertionMethod = [keyId]
        };
    }
}
