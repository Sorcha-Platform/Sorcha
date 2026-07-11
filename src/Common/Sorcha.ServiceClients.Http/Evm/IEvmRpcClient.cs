// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Numerics;

namespace Sorcha.ServiceClients.Evm;

/// <summary>The three outcomes of a read-only EVM RPC call — the distinction that drives fail-closed
/// vs. offline behaviour (Feature 179 FR-006/FR-007).</summary>
public enum EvmRpcOutcome
{
    /// <summary>No RPC URL is configured for the chain — the caller uses the offline default document.</summary>
    NotConfigured,

    /// <summary>Configured but the call failed (timeout / network / SSRF-blocked / malformed) — the caller fails closed.</summary>
    Error,

    /// <summary>The call succeeded.</summary>
    Ok
}

/// <summary>Result of an <c>eth_call</c>: an outcome plus the hex result when <see cref="EvmRpcOutcome.Ok"/>.</summary>
public sealed class EvmCallResult
{
    private EvmCallResult(EvmRpcOutcome outcome, string? value)
    {
        Outcome = outcome;
        Value = value;
    }

    /// <summary>The call outcome.</summary>
    public EvmRpcOutcome Outcome { get; }

    /// <summary>The <c>0x</c>-prefixed hex result when <see cref="Outcome"/> is <see cref="EvmRpcOutcome.Ok"/>.</summary>
    public string? Value { get; }

    /// <summary>The chain has no configured RPC endpoint.</summary>
    public static readonly EvmCallResult NotConfigured = new(EvmRpcOutcome.NotConfigured, null);

    /// <summary>The configured call failed.</summary>
    public static readonly EvmCallResult Error = new(EvmRpcOutcome.Error, null);

    /// <summary>A successful call carrying its hex result.</summary>
    public static EvmCallResult Ok(string value) => new(EvmRpcOutcome.Ok, value);
}

/// <summary>A single decoded log entry (<c>topics</c> + hex <c>data</c>).</summary>
public sealed record EvmLog(IReadOnlyList<string> Topics, string Data);

/// <summary>Result of an <c>eth_getLogs</c>: an outcome plus the logs when <see cref="EvmRpcOutcome.Ok"/>.</summary>
public sealed class EvmLogsResult
{
    private EvmLogsResult(EvmRpcOutcome outcome, IReadOnlyList<EvmLog>? logs)
    {
        Outcome = outcome;
        Logs = logs;
    }

    /// <summary>The call outcome.</summary>
    public EvmRpcOutcome Outcome { get; }

    /// <summary>The decoded logs when <see cref="Outcome"/> is <see cref="EvmRpcOutcome.Ok"/>.</summary>
    public IReadOnlyList<EvmLog>? Logs { get; }

    /// <summary>The chain has no configured RPC endpoint.</summary>
    public static readonly EvmLogsResult NotConfigured = new(EvmRpcOutcome.NotConfigured, null);

    /// <summary>The configured call failed.</summary>
    public static readonly EvmLogsResult Error = new(EvmRpcOutcome.Error, null);

    /// <summary>A successful call carrying its logs.</summary>
    public static EvmLogsResult Ok(IReadOnlyList<EvmLog> logs) => new(EvmRpcOutcome.Ok, logs);
}

/// <summary>Result of a JSON-RPC quantity call (nonce / gas / fee / chainId) — an outcome plus a
/// <see cref="BigInteger"/> when <see cref="EvmRpcOutcome.Ok"/>.</summary>
public sealed class EvmUIntResult
{
    private EvmUIntResult(EvmRpcOutcome outcome, BigInteger value)
    {
        Outcome = outcome;
        Value = value;
    }

    /// <summary>The call outcome.</summary>
    public EvmRpcOutcome Outcome { get; }

    /// <summary>The decoded quantity when <see cref="Outcome"/> is <see cref="EvmRpcOutcome.Ok"/>.</summary>
    public BigInteger Value { get; }

    /// <summary>The chain has no configured RPC endpoint.</summary>
    public static readonly EvmUIntResult NotConfigured = new(EvmRpcOutcome.NotConfigured, BigInteger.Zero);

    /// <summary>The configured call failed.</summary>
    public static readonly EvmUIntResult Error = new(EvmRpcOutcome.Error, BigInteger.Zero);

    /// <summary>A successful call carrying its quantity.</summary>
    public static EvmUIntResult Ok(BigInteger value) => new(EvmRpcOutcome.Ok, value);
}

/// <summary>Result of <c>eth_sendRawTransaction</c> — an outcome plus the tx hash when
/// <see cref="EvmRpcOutcome.Ok"/>.</summary>
public sealed class EvmSendResult
{
    private EvmSendResult(EvmRpcOutcome outcome, string? txHash)
    {
        Outcome = outcome;
        TxHash = txHash;
    }

    /// <summary>The call outcome.</summary>
    public EvmRpcOutcome Outcome { get; }

    /// <summary>The broadcast transaction hash when <see cref="Outcome"/> is <see cref="EvmRpcOutcome.Ok"/>.</summary>
    public string? TxHash { get; }

    /// <summary>The chain has no configured RPC endpoint.</summary>
    public static readonly EvmSendResult NotConfigured = new(EvmRpcOutcome.NotConfigured, null);

    /// <summary>The configured broadcast failed.</summary>
    public static readonly EvmSendResult Error = new(EvmRpcOutcome.Error, null);

    /// <summary>A successful broadcast carrying its tx hash.</summary>
    public static EvmSendResult Ok(string txHash) => new(EvmRpcOutcome.Ok, txHash);
}

/// <summary>A mined transaction receipt — success flag plus block number and gas used.</summary>
public sealed record EvmReceipt(bool Success, long BlockNumber, long GasUsed);

/// <summary>Result of <c>eth_getTransactionReceipt</c>. <see cref="EvmRpcOutcome.Ok"/> with a null
/// <see cref="Receipt"/> means the transaction is not yet mined (pending); a non-null receipt means mined.</summary>
public sealed class EvmReceiptResult
{
    private EvmReceiptResult(EvmRpcOutcome outcome, EvmReceipt? receipt)
    {
        Outcome = outcome;
        Receipt = receipt;
    }

    /// <summary>The call outcome.</summary>
    public EvmRpcOutcome Outcome { get; }

    /// <summary>The mined receipt, or null when the tx is still pending (with <see cref="EvmRpcOutcome.Ok"/>).</summary>
    public EvmReceipt? Receipt { get; }

    /// <summary>The chain has no configured RPC endpoint.</summary>
    public static readonly EvmReceiptResult NotConfigured = new(EvmRpcOutcome.NotConfigured, null);

    /// <summary>The configured call failed.</summary>
    public static readonly EvmReceiptResult Error = new(EvmRpcOutcome.Error, null);

    /// <summary>The transaction is not yet mined.</summary>
    public static readonly EvmReceiptResult Pending = new(EvmRpcOutcome.Ok, null);

    /// <summary>A successful call carrying a mined receipt.</summary>
    public static EvmReceiptResult Mined(EvmReceipt receipt) => new(EvmRpcOutcome.Ok, receipt);
}

/// <summary>
/// Ethereum JSON-RPC client. The read-only methods (<c>eth_call</c>, <c>eth_getLogs</c>) serve ERC-1056
/// DID resolution (Feature 179); the write/query methods serve native ETH transacting (Feature 182,
/// Phase 4). SSRF-guarded and registered <b>server-side only</b>; never throws — all failures surface as
/// <see cref="EvmRpcOutcome.Error"/> / <see cref="EvmRpcOutcome.NotConfigured"/>.
/// </summary>
public interface IEvmRpcClient
{
    /// <summary><c>eth_call</c> at the latest block against <paramref name="to"/> with hex <paramref name="dataHex"/>.</summary>
    Task<EvmCallResult> CallAsync(long chainId, string to, string dataHex, CancellationToken ct = default);

    /// <summary><c>eth_getLogs</c> for a single block, filtered by <paramref name="address"/> and <paramref name="topics"/>.</summary>
    Task<EvmLogsResult> GetLogsAsync(long chainId, string address, string?[] topics, long block, CancellationToken ct = default);

    // ---- Feature 182 (Phase 4) — write + query methods for native ETH transacting ----

    /// <summary><c>eth_sendRawTransaction</c> — broadcast a signed raw transaction; returns its hash.</summary>
    Task<EvmSendResult> SendRawTransactionAsync(long chainId, string rawTxHex, CancellationToken ct = default);

    /// <summary><c>eth_getTransactionCount(address, "pending")</c> — the next nonce for <paramref name="address"/>.</summary>
    Task<EvmUIntResult> GetTransactionCountAsync(long chainId, string address, CancellationToken ct = default);

    /// <summary><c>eth_estimateGas</c> for a transfer <paramref name="from"/> → <paramref name="to"/> of <paramref name="valueHex"/> with <paramref name="dataHex"/>.</summary>
    Task<EvmUIntResult> EstimateGasAsync(long chainId, string from, string to, string valueHex, string dataHex, CancellationToken ct = default);

    /// <summary><c>eth_maxPriorityFeePerGas</c> — the suggested EIP-1559 priority fee (tip), in wei.</summary>
    Task<EvmUIntResult> GetMaxPriorityFeePerGasAsync(long chainId, CancellationToken ct = default);

    /// <summary>The pending block's <c>baseFeePerGas</c> (via <c>eth_getBlockByNumber("pending", false)</c>), in wei.</summary>
    Task<EvmUIntResult> GetBaseFeePerGasAsync(long chainId, CancellationToken ct = default);

    /// <summary><c>eth_getTransactionReceipt</c> — pending (null receipt) or mined (success + block + gas used).</summary>
    Task<EvmReceiptResult> GetTransactionReceiptAsync(long chainId, string txHash, CancellationToken ct = default);

    /// <summary><c>eth_chainId</c> — the chain id the configured endpoint actually serves (a safety cross-check).</summary>
    Task<EvmUIntResult> GetChainIdAsync(long chainId, CancellationToken ct = default);
}
