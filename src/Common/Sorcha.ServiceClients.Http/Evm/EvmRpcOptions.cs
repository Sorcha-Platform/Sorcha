// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Evm;

/// <summary>
/// Operator configuration for read-only EVM RPC used by <c>did:ethr</c> on-chain resolution
/// (Feature 179). Bound from <c>DidResolver:Ethr</c> (plus the shared
/// <c>DidResolver:AllowPrivateAddresses</c>). A chain with no <see cref="Rpc"/> entry stays offline.
/// </summary>
public sealed class EvmRpcOptions
{
    /// <summary>The canonical ERC-1056 <c>EthereumDIDRegistry</c> address (mainnet + most networks).</summary>
    public const string DefaultRegistry = "0xdca7ef03e98e0dc2b855be647c39abe984fcf21b";

    /// <summary>chainId → read-only JSON-RPC endpoint URL. Absent ⇒ offline default document for that chain.</summary>
    public Dictionary<string, string> Rpc { get; set; } = new();

    /// <summary>chainId → registry-address override. Absent ⇒ <see cref="DefaultRegistry"/>.</summary>
    public Dictionary<string, string> RegistryAddress { get; set; } = new();

    /// <summary>Maximum <c>previousChange</c> hops to walk before failing closed (defensive bound).</summary>
    public int MaxHistoryHops { get; set; } = 128;

    /// <summary>Permit private/reserved RPC hosts (local dev nodes only). Shared with <c>WebDidResolver</c>.</summary>
    public bool AllowPrivateAddresses { get; set; }

    /// <summary>The configured RPC URL for a chain, or null when the chain is unconfigured (stay offline).</summary>
    public string? RpcFor(long chainId)
        => Rpc.TryGetValue(chainId.ToString(), out var url) && !string.IsNullOrWhiteSpace(url) ? url : null;

    /// <summary>The registry address for a chain (override or the canonical default).</summary>
    public string RegistryFor(long chainId)
        => RegistryAddress.TryGetValue(chainId.ToString(), out var addr) && !string.IsNullOrWhiteSpace(addr)
            ? addr
            : DefaultRegistry;
}
