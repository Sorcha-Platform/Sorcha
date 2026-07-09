// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;

namespace Sorcha.ServiceClients.Did;

/// <summary>
/// Resolves <c>did:pkh</c> DIDs (CAIP-10) for the EVM (<c>eip155</c>) namespace, entirely offline —
/// the DID commits only to a blockchain address, so the resolved verification method is an
/// <c>EcdsaSecp256k1RecoveryMethod2020</c> carrying a <c>blockchainAccountId</c> (no public key).
/// Verification is by public-key recovery (Feature 178). Only <c>eip155</c> is supported; other
/// CAIP-2 namespaces return null. No network calls are made.
/// </summary>
public class PkhDidResolver : IDidResolver
{
    private const string Method = "pkh";
    private const string DidPkhPrefix = "did:pkh:";

    private readonly ILogger<PkhDidResolver> _logger;

    public PkhDidResolver(ILogger<PkhDidResolver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool CanResolve(string didMethod) =>
        string.Equals(didMethod, Method, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(did) || !did.StartsWith(DidPkhPrefix, StringComparison.Ordinal))
            return Task.FromResult<DidDocument?>(null);

        // did:pkh:eip155:{chainId}:0x{40hex}
        var segments = did[DidPkhPrefix.Length..].Split(':');
        if (segments.Length != 3
            || !string.Equals(segments[0], "eip155", StringComparison.Ordinal)
            || !EvmDid.IsChainId(segments[1])
            || !EvmDid.IsAddress(segments[2]))
        {
            _logger.LogWarning("Unsupported or malformed did:pkh: {Did}", did);
            return Task.FromResult<DidDocument?>(null);
        }

        var blockchainAccountId = $"eip155:{segments[1]}:{segments[2]}";
        var keyId = $"{did}#blockchainAccountId";

        var doc = new DidDocument
        {
            Id = did,
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = keyId,
                    Type = EvmDid.RecoveryMethodType,
                    Controller = did,
                    BlockchainAccountId = blockchainAccountId
                }
            ],
            Authentication = [keyId],
            AssertionMethod = [keyId]
        };

        return Task.FromResult<DidDocument?>(doc);
    }
}
