// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;

namespace Sorcha.ServiceClients.Did;

/// <summary>
/// Resolves address-form <c>did:ethr</c> DIDs (ERC-1056) to their <b>default document</b>, entirely
/// offline. The default document — the state with no on-chain registry events — commits only to the
/// controller address, so the resolved verification method is an <c>EcdsaSecp256k1RecoveryMethod2020</c>
/// carrying a <c>blockchainAccountId</c> (no public key); verification is by public-key recovery
/// (Feature 178). Supported forms: <c>did:ethr:0x{addr}</c> (mainnet), <c>did:ethr:{network}:0x{addr}</c>,
/// and <c>did:ethr:0x{chainIdHex}:0x{addr}</c>.
/// </summary>
/// <remarks>
/// On-chain key rotation / delegates / service endpoints (reading ERC-1056 registry events) are
/// deferred to a later phase behind an optional <c>IEvmRpcClient</c> seam; this resolver ships only the
/// offline default document. Anything requiring a registry read returns null.
/// </remarks>
public class EthrDidResolver : IDidResolver
{
    private const string Method = "ethr";
    private const string DidEthrPrefix = "did:ethr:";

    private readonly ILogger<EthrDidResolver> _logger;

    public EthrDidResolver(ILogger<EthrDidResolver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool CanResolve(string didMethod) =>
        string.Equals(didMethod, Method, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(did) || !did.StartsWith(DidEthrPrefix, StringComparison.Ordinal))
            return Task.FromResult<DidDocument?>(null);

        var segments = did[DidEthrPrefix.Length..].Split(':');

        // did:ethr:0x{addr}  |  did:ethr:{network|0xchainId}:0x{addr}
        string address;
        long chainId;
        switch (segments.Length)
        {
            case 1 when EvmDid.IsAddress(segments[0]):
                address = segments[0];
                chainId = 1; // mainnet default
                break;
            case 2 when EvmDid.IsAddress(segments[1]) && EvmDid.ResolveNetworkChainId(segments[0]) is { } resolved:
                address = segments[1];
                chainId = resolved;
                break;
            default:
                _logger.LogWarning("Unsupported or malformed address-form did:ethr: {Did}", did);
                return Task.FromResult<DidDocument?>(null);
        }

        var blockchainAccountId = $"eip155:{chainId}:{address}";
        var keyId = $"{did}#controller";

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
