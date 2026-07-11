// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.ServiceClients.Did;

namespace Sorcha.ServiceClients.Evm;

/// <summary>
/// Read-only Ethereum JSON-RPC client over <see cref="HttpClient"/> (Feature 179). SSRF-guarded like
/// <see cref="WebDidResolver"/> (reuses its private/reserved-address check), 5s per-request timeout,
/// and never throws — every failure is <see cref="EvmRpcOutcome.Error"/>. Registered server-side only.
/// </summary>
public sealed class EvmRpcClient : IEvmRpcClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _httpClient;
    private readonly EvmRpcOptions _options;
    private readonly ILogger<EvmRpcClient> _logger;

    public EvmRpcClient(HttpClient httpClient, IOptions<EvmRpcOptions> options, ILogger<EvmRpcClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<EvmCallResult> CallAsync(long chainId, string to, string dataHex, CancellationToken ct = default)
    {
        var result = await SendAsync(chainId, "eth_call",
            [new { to, data = dataHex }, "latest"], ct).ConfigureAwait(false);

        return result.Outcome switch
        {
            EvmRpcOutcome.NotConfigured => EvmCallResult.NotConfigured,
            EvmRpcOutcome.Ok when result.Result.ValueKind == JsonValueKind.String
                => EvmCallResult.Ok(result.Result.GetString()!),
            _ => EvmCallResult.Error
        };
    }

    /// <inheritdoc />
    public async Task<EvmLogsResult> GetLogsAsync(long chainId, string address, string?[] topics, long block, CancellationToken ct = default)
    {
        var blockHex = "0x" + block.ToString("x");
        var result = await SendAsync(chainId, "eth_getLogs",
            [new { address, fromBlock = blockHex, toBlock = blockHex, topics }], ct).ConfigureAwait(false);

        if (result.Outcome == EvmRpcOutcome.NotConfigured)
        {
            return EvmLogsResult.NotConfigured;
        }

        if (result.Outcome != EvmRpcOutcome.Ok || result.Result.ValueKind != JsonValueKind.Array)
        {
            return EvmLogsResult.Error;
        }

        try
        {
            var logs = new List<EvmLog>();
            foreach (var entry in result.Result.EnumerateArray())
            {
                var topicList = entry.TryGetProperty("topics", out var t) && t.ValueKind == JsonValueKind.Array
                    ? t.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray()
                    : [];
                var data = entry.TryGetProperty("data", out var d) ? d.GetString() ?? "0x" : "0x";
                logs.Add(new EvmLog(topicList, data));
            }

            return EvmLogsResult.Ok(logs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EVM eth_getLogs response could not be parsed for chain {ChainId}", chainId);
            return EvmLogsResult.Error;
        }
    }

    /// <inheritdoc />
    public async Task<EvmSendResult> SendRawTransactionAsync(long chainId, string rawTxHex, CancellationToken ct = default)
    {
        var result = await SendAsync(chainId, "eth_sendRawTransaction", [rawTxHex], ct).ConfigureAwait(false);
        return result.Outcome switch
        {
            EvmRpcOutcome.NotConfigured => EvmSendResult.NotConfigured,
            EvmRpcOutcome.Ok when result.Result.ValueKind == JsonValueKind.String
                => EvmSendResult.Ok(result.Result.GetString()!),
            _ => EvmSendResult.Error
        };
    }

    /// <inheritdoc />
    public Task<EvmUIntResult> GetTransactionCountAsync(long chainId, string address, CancellationToken ct = default)
        => QuantityAsync(chainId, "eth_getTransactionCount", [address, "pending"], ct);

    /// <inheritdoc />
    public Task<EvmUIntResult> EstimateGasAsync(long chainId, string from, string to, string valueHex, string dataHex, CancellationToken ct = default)
        => QuantityAsync(chainId, "eth_estimateGas", [new { from, to, value = valueHex, data = dataHex }], ct);

    /// <inheritdoc />
    public Task<EvmUIntResult> GetMaxPriorityFeePerGasAsync(long chainId, CancellationToken ct = default)
        => QuantityAsync(chainId, "eth_maxPriorityFeePerGas", [], ct);

    /// <inheritdoc />
    public Task<EvmUIntResult> GetChainIdAsync(long chainId, CancellationToken ct = default)
        => QuantityAsync(chainId, "eth_chainId", [], ct);

    /// <inheritdoc />
    public async Task<EvmUIntResult> GetBaseFeePerGasAsync(long chainId, CancellationToken ct = default)
    {
        var result = await SendAsync(chainId, "eth_getBlockByNumber", ["pending", false], ct).ConfigureAwait(false);
        if (result.Outcome == EvmRpcOutcome.NotConfigured)
        {
            return EvmUIntResult.NotConfigured;
        }

        if (result.Outcome != EvmRpcOutcome.Ok
            || result.Result.ValueKind != JsonValueKind.Object
            || !result.Result.TryGetProperty("baseFeePerGas", out var baseFee)
            || baseFee.ValueKind != JsonValueKind.String
            || !TryParseQuantity(baseFee.GetString(), out var value))
        {
            return EvmUIntResult.Error;
        }

        return EvmUIntResult.Ok(value);
    }

    /// <inheritdoc />
    public async Task<EvmReceiptResult> GetTransactionReceiptAsync(long chainId, string txHash, CancellationToken ct = default)
    {
        var result = await SendAsync(chainId, "eth_getTransactionReceipt", [txHash], ct).ConfigureAwait(false);
        if (result.Outcome == EvmRpcOutcome.NotConfigured)
        {
            return EvmReceiptResult.NotConfigured;
        }

        if (result.Outcome != EvmRpcOutcome.Ok)
        {
            return EvmReceiptResult.Error;
        }

        // null result ⇒ not yet mined (pending).
        if (result.Result.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return EvmReceiptResult.Pending;
        }

        try
        {
            var success = result.Result.TryGetProperty("status", out var status)
                && TryParseQuantity(status.GetString(), out var s) && s == BigInteger.One;
            var blockNumber = result.Result.TryGetProperty("blockNumber", out var bn)
                && TryParseQuantity(bn.GetString(), out var b) ? (long)b : 0L;
            var gasUsed = result.Result.TryGetProperty("gasUsed", out var gu)
                && TryParseQuantity(gu.GetString(), out var g) ? (long)g : 0L;
            return EvmReceiptResult.Mined(new EvmReceipt(success, blockNumber, gasUsed));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EVM receipt for chain {ChainId} could not be parsed", chainId);
            return EvmReceiptResult.Error;
        }
    }

    private async Task<EvmUIntResult> QuantityAsync(long chainId, string method, object[] parameters, CancellationToken ct)
    {
        var result = await SendAsync(chainId, method, parameters, ct).ConfigureAwait(false);
        return result.Outcome switch
        {
            EvmRpcOutcome.NotConfigured => EvmUIntResult.NotConfigured,
            EvmRpcOutcome.Ok when result.Result.ValueKind == JsonValueKind.String
                && TryParseQuantity(result.Result.GetString(), out var value) => EvmUIntResult.Ok(value),
            _ => EvmUIntResult.Error
        };
    }

    /// <summary>Parse a <c>0x</c>-prefixed JSON-RPC quantity into a non-negative <see cref="BigInteger"/>.</summary>
    private static bool TryParseQuantity(string? hex, out BigInteger value)
    {
        value = BigInteger.Zero;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var span = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex.AsSpan(2) : hex.AsSpan();
        if (span.IsEmpty)
        {
            return false;
        }

        // Left-pad to even length and prefix 0x00 so BigInteger.Parse treats it as unsigned.
        var normalized = (span.Length % 2 == 1 ? "0" + span.ToString() : span.ToString());
        return BigInteger.TryParse("0" + normalized, System.Globalization.NumberStyles.HexNumber, null, out value)
            && value.Sign >= 0;
    }

    private async Task<(EvmRpcOutcome Outcome, JsonElement Result)> SendAsync(
        long chainId, string method, object[] parameters, CancellationToken ct)
    {
        var url = _options.RpcFor(chainId);
        if (url is null)
        {
            return (EvmRpcOutcome.NotConfigured, default);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            _logger.LogWarning("EVM RPC endpoint for chain {ChainId} is not a valid HTTPS URL", chainId);
            return (EvmRpcOutcome.Error, default);
        }

        if (!_options.AllowPrivateAddresses && !await IsHostAllowedAsync(uri.Host).ConfigureAwait(false))
        {
            _logger.LogWarning("EVM RPC call blocked by SSRF protection for chain {ChainId} (host {Host})", chainId, uri.Host);
            return (EvmRpcOutcome.Error, default);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestTimeout);

        try
        {
            var request = new { jsonrpc = "2.0", id = 1, method, @params = parameters };
            using var response = await _httpClient.PostAsJsonAsync(uri, request, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("EVM RPC {Method} for chain {ChainId} returned HTTP {Status}", method, chainId, (int)response.StatusCode);
                return (EvmRpcOutcome.Error, default);
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var body = doc.RootElement;
            if (body.TryGetProperty("error", out var error))
            {
                _logger.LogWarning("EVM RPC {Method} for chain {ChainId} returned error {Error}", method, chainId, error.ToString());
                return (EvmRpcOutcome.Error, default);
            }

            if (!body.TryGetProperty("result", out var resultEl))
            {
                return (EvmRpcOutcome.Error, default);
            }

            // Clone so the element outlives the disposed JsonDocument.
            return (EvmRpcOutcome.Ok, resultEl.Clone());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EVM RPC {Method} for chain {ChainId} failed", method, chainId);
            return (EvmRpcOutcome.Error, default);
        }
    }

    private async Task<bool> IsHostAllowedAsync(string host)
    {
        try
        {
            if (IPAddress.TryParse(host, out var directIp))
            {
                return !WebDidResolver.IsPrivateOrReservedAddress(directIp);
            }

            var addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
            return addresses.Length > 0 && !addresses.Any(WebDidResolver.IsPrivateOrReservedAddress);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DNS resolution failed for EVM RPC host {Host}", host);
            return false;
        }
    }
}
