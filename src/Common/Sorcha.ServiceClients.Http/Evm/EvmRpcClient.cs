// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
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
