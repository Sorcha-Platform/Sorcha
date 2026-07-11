// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Numerics;

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>Why a transfer was refused (maps to the caller-facing <c>reason</c> and the HTTP status).</summary>
public enum TransferRefusalReason
{
    /// <summary>No refusal — the operation succeeded.</summary>
    None,
    /// <summary>The chain is not in the enabled allowlist.</summary>
    ChainNotEnabled,
    /// <summary>The chain is mainnet-class and the <c>AllowMainnet</c> master switch is off.</summary>
    MainnetNotAllowed,
    /// <summary>The transfer amount exceeds the per-transaction value cap.</summary>
    ValueOverCap,
    /// <summary>The computed max fee per gas exceeds the configured ceiling.</summary>
    FeeOverCeiling,
    /// <summary>The recipient address is malformed.</summary>
    InvalidAddress,
    /// <summary>The transfer amount is negative or otherwise invalid.</summary>
    InvalidAmount,
    /// <summary>A required RPC call failed or the chain has no (write-capable) RPC configured.</summary>
    RpcError,
    /// <summary>Gas estimation failed.</summary>
    EstimateFailed,
    /// <summary>The broadcast (<c>eth_sendRawTransaction</c>) failed.</summary>
    BroadcastFailed,
    /// <summary>The wallet could not be found.</summary>
    WalletNotFound
}

/// <summary>The computed parameters of an intended transfer (the preview projection).</summary>
public sealed record TransferPreparation(
    long ChainId, string From, long Nonce, long GasLimit,
    BigInteger MaxFeePerGasWei, BigInteger MaxPriorityFeePerGasWei, BigInteger ValueWei, BigInteger EstimatedTotalCostWei);

/// <summary>The outcome of a send: a hash on success, or a refusal reason.</summary>
public sealed record SendOutcome(bool Ok, TransferRefusalReason Reason, string? TxHash, string? From, long ChainId, long Nonce)
{
    /// <summary>A refusal.</summary>
    public static SendOutcome Refused(TransferRefusalReason reason) => new(false, reason, null, null, 0, 0);
}

/// <summary>The outcome of a preview: the preparation on success, or a refusal reason.</summary>
public sealed record PreviewOutcome(bool Ok, TransferRefusalReason Reason, TransferPreparation? Preparation)
{
    /// <summary>A refusal.</summary>
    public static PreviewOutcome Refused(TransferRefusalReason reason) => new(false, reason, null);
}

/// <summary>The outcome of a status lookup.</summary>
public sealed record StatusOutcome(bool Ok, TransferRefusalReason Reason, string? Status, long? BlockNumber, long? GasUsed)
{
    /// <summary>A refusal.</summary>
    public static StatusOutcome Refused(TransferRefusalReason reason) => new(false, reason, null, null, null);
}

/// <summary>
/// Orchestrates native ETH transacting (Feature 182, Phase 4): enforces the transacting policy, gathers
/// live chain parameters (nonce, gas, fees) over the write-capable EVM RPC, signs via the Wallet Core
/// signer, broadcasts, and reports receipts. Server-side only. Fail-closed: any policy violation, RPC
/// failure, or gas-estimation failure refuses the transfer with no broadcast.
/// </summary>
public interface IEthereumTransactionService
{
    /// <summary>Prepare, sign, and broadcast a native ETH transfer; returns the hash immediately.</summary>
    Task<SendOutcome> SendAsync(string walletAddress, long chainId, string to, BigInteger valueWei, int index, CancellationToken ct = default);

    /// <summary>Compute the fees/gas/total-cost for an intended transfer without signing or broadcasting.</summary>
    Task<PreviewOutcome> PreviewAsync(string walletAddress, long chainId, string to, BigInteger valueWei, int index, CancellationToken ct = default);

    /// <summary>Look up the receipt status of a broadcast transfer.</summary>
    Task<StatusOutcome> GetStatusAsync(long chainId, string txHash, CancellationToken ct = default);
}
