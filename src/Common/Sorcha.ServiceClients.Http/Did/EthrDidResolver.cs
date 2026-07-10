// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Cryptography.Secp256k1;
using Sorcha.ServiceClients.Evm;

namespace Sorcha.ServiceClients.Did;

/// <summary>
/// Resolves address-form <c>did:ethr</c> DIDs (ERC-1056). Without an <see cref="Erc1056Registry"/>
/// (the WASM / offline composition) it returns the <b>default document</b> — the controller address,
/// entirely offline (Feature 178). With a registry (server hosts only, Feature 179) it reads ERC-1056
/// state over read-only EVM RPC and returns the <b>current document</b> — the current owner (after
/// rotation), unexpired <c>veriKey</c>/<c>sigAuth</c> delegates, and unexpired <c>did/pub/*</c> key
/// attributes. Verification is by public-key recovery (address VMs) / key-match (key VMs) through the
/// existing pipeline. A configured-but-errored RPC fails closed (null) rather than trusting a stale doc.
/// </summary>
public class EthrDidResolver : IDidResolver
{
    private const string Method = "ethr";
    private const string DidEthrPrefix = "did:ethr:";
    private const string RecoveryMethodType = "EcdsaSecp256k1RecoveryMethod2020";

    private readonly ILogger<EthrDidResolver> _logger;
    private readonly Erc1056Registry? _registry;

    public EthrDidResolver(ILogger<EthrDidResolver> logger, Erc1056Registry? registry = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _registry = registry;
    }

    /// <inheritdoc />
    public bool CanResolve(string didMethod) =>
        string.Equals(didMethod, Method, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(did) || !did.StartsWith(DidEthrPrefix, StringComparison.Ordinal))
            return null;

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
                return null;
        }

        // Offline composition (no registry) or no on-chain history → the default document.
        if (_registry is null)
            return BuildDefaultDocument(did, chainId, address);

        var state = await _registry.ReadAsync(chainId, address, ct).ConfigureAwait(false);

        if (state.RpcError)
        {
            // Configured RPC errored — fail closed. Do NOT return the (possibly stale) default document.
            _logger.LogWarning("did:ethr on-chain resolution failed for {Did}; failing closed", did);
            return null;
        }

        if (state.NoHistory)
            return BuildDefaultDocument(did, chainId, address);

        return BuildCurrentDocument(did, chainId, state);
    }

    private static DidDocument BuildDefaultDocument(string did, long chainId, string address)
    {
        var keyId = $"{did}#controller";
        return new DidDocument
        {
            Id = did,
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = keyId,
                    Type = RecoveryMethodType,
                    Controller = did,
                    BlockchainAccountId = $"eip155:{chainId}:{address}"
                }
            ],
            Authentication = [keyId],
            AssertionMethod = [keyId]
        };
    }

    private DidDocument BuildCurrentDocument(string did, long chainId, Erc1056State state)
    {
        var vms = new List<VerificationMethod>();
        var authentication = new List<string>();
        var assertion = new List<string>();

        // Current owner → #controller recovery VM (both relationships).
        var ownerId = $"{did}#controller";
        vms.Add(new VerificationMethod
        {
            Id = ownerId,
            Type = RecoveryMethodType,
            Controller = did,
            BlockchainAccountId = $"eip155:{chainId}:{state.OwnerAddress}"
        });
        authentication.Add(ownerId);
        assertion.Add(ownerId);

        var index = 0;
        foreach (var del in state.Delegates)
        {
            var id = $"{did}#delegate-{++index}";
            vms.Add(new VerificationMethod
            {
                Id = id,
                Type = RecoveryMethodType,
                Controller = did,
                BlockchainAccountId = $"eip155:{chainId}:{del.Address}"
            });
            AddRelationship(del.DelegateType, id, authentication, assertion);
        }

        foreach (var attr in state.Attributes)
        {
            var jwk = BuildAttributeJwk(attr);
            if (jwk is null)
                continue;

            var id = $"{did}#delegate-{++index}";
            vms.Add(new VerificationMethod
            {
                Id = id,
                Type = "JsonWebKey2020",
                Controller = did,
                PublicKeyJwk = jwk
            });
            AddRelationship(attr.Purpose, id, authentication, assertion);
        }

        return new DidDocument
        {
            Id = did,
            VerificationMethod = vms,
            Authentication = authentication,
            AssertionMethod = assertion
        };
    }

    private static void AddRelationship(string purposeOrType, string id, List<string> authentication, List<string> assertion)
    {
        // veriKey → assertionMethod; sigAuth → authentication (ERC-1056 / did-ethr).
        if (string.Equals(purposeOrType, "sigAuth", StringComparison.Ordinal))
            authentication.Add(id);
        else
            assertion.Add(id);
    }

    private JsonElement? BuildAttributeJwk(Erc1056KeyAttribute attr)
    {
        try
        {
            if (string.Equals(attr.Algorithm, "Secp256k1", StringComparison.Ordinal))
            {
                var key = Secp256k1PublicKey.FromSec1(attr.Key);
                var (x, y) = Secp256k1Jwk.ToCoordinates(key);
                return JsonSerializer.SerializeToElement(new { kty = "EC", crv = "secp256k1", x, y });
            }

            if (string.Equals(attr.Algorithm, "Ed25519", StringComparison.Ordinal) && attr.Key.Length == 32)
            {
                return JsonSerializer.SerializeToElement(new
                {
                    kty = "OKP",
                    crv = "Ed25519",
                    x = Base64Url.EncodeToString(attr.Key)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Skipping malformed did:ethr key attribute ({Algo})", attr.Algorithm);
        }

        return null;
    }
}
