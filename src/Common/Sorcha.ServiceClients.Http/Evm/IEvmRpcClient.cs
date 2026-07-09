// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

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

/// <summary>
/// Read-only Ethereum JSON-RPC client for ERC-1056 DID resolution (Feature 179). Exactly two methods —
/// <c>eth_call</c> and <c>eth_getLogs</c> — no writes, no wallet, no other RPC. SSRF-guarded and
/// registered <b>server-side only</b>; never throws (all failures surface as
/// <see cref="EvmRpcOutcome.Error"/> / <see cref="EvmRpcOutcome.NotConfigured"/>).
/// </summary>
public interface IEvmRpcClient
{
    /// <summary><c>eth_call</c> at the latest block against <paramref name="to"/> with hex <paramref name="dataHex"/>.</summary>
    Task<EvmCallResult> CallAsync(long chainId, string to, string dataHex, CancellationToken ct = default);

    /// <summary><c>eth_getLogs</c> for a single block, filtered by <paramref name="address"/> and <paramref name="topics"/>.</summary>
    Task<EvmLogsResult> GetLogsAsync(long chainId, string address, string?[] topics, long block, CancellationToken ct = default);
}
