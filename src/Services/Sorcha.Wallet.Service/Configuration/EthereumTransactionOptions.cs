// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Numerics;

namespace Sorcha.Wallet.Service.Configuration;

/// <summary>
/// Operator policy bounding native ETH transacting (Feature 182, Phase 4). Value transfers are
/// irreversible, so the defaults are deliberately conservative: testnets only, a small per-transaction
/// value cap, and a max-fee ceiling. Bound from <c>Ethereum:Transactions</c>. The per-chain RPC URL is
/// shared with Feature 179's <c>DidResolver:Ethr:Rpc:{chainId}</c> cascade (which must be write-capable
/// for transacting — a read-only public endpoint rejects <c>eth_sendRawTransaction</c>).
/// </summary>
public sealed class EthereumTransactionOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Ethereum:Transactions";

    /// <summary>Chain ids on which transfers are permitted. Default: Sepolia + Holešky testnets.</summary>
    public long[] EnabledChainIds { get; set; } = [11155111, 17000];

    /// <summary>
    /// Master gate: when <c>false</c> (default) a transfer to any chain that is not a recognised testnet is
    /// refused, even if it appears in <see cref="EnabledChainIds"/> — defence against fat-fingering a
    /// mainnet chain id. Set <c>true</c> to permit mainnet-class chains.
    /// </summary>
    public bool AllowMainnet { get; set; }

    /// <summary>Per-transaction maximum value in wei (decimal string). Default 0.1 ETH.</summary>
    public string MaxValueWei { get; set; } = "100000000000000000";

    /// <summary>Ceiling on the computed max fee per gas in wei (decimal string). Default 500 gwei.</summary>
    public string MaxFeePerGasWei { get; set; } = "500000000000";

    /// <summary>Fallback priority fee (tip) in wei when the node does not support <c>eth_maxPriorityFeePerGas</c>. Default 1.5 gwei.</summary>
    public string DefaultPriorityFeeWei { get; set; } = "1500000000";

    // Recognised EVM testnets (chain ids). Anything else is "mainnet-class" for the AllowMainnet gate.
    private static readonly HashSet<long> KnownTestnets =
    [
        11155111, // Sepolia
        17000,    // Holešky
        560048,   // Hoodi
        84532,    // Base Sepolia
        11155420, // Optimism Sepolia
        421614,   // Arbitrum Sepolia
        80002     // Polygon Amoy
    ];

    /// <summary>Whether <paramref name="chainId"/> is a recognised testnet.</summary>
    public static bool IsKnownTestnet(long chainId) => KnownTestnets.Contains(chainId);

    /// <summary>The per-transaction value cap as a <see cref="BigInteger"/>.</summary>
    public BigInteger MaxValue => ParseOrZero(MaxValueWei);

    /// <summary>The max-fee-per-gas ceiling as a <see cref="BigInteger"/>.</summary>
    public BigInteger MaxFeePerGas => ParseOrZero(MaxFeePerGasWei);

    /// <summary>The fallback priority fee as a <see cref="BigInteger"/>.</summary>
    public BigInteger DefaultPriorityFee => ParseOrZero(DefaultPriorityFeeWei);

    private static BigInteger ParseOrZero(string value) =>
        BigInteger.TryParse(value, out var parsed) && parsed.Sign >= 0 ? parsed : BigInteger.Zero;
}
