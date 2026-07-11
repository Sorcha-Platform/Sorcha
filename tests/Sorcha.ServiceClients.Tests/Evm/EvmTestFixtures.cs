// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using Sorcha.ServiceClients.Evm;

namespace Sorcha.ServiceClients.Tests.Evm;

/// <summary>Shared fake <see cref="IEvmRpcClient"/> + ABI log/word builders for the Feature 179 tests.</summary>
internal static class EvmTestFixtures
{
    public const long Now = 1_800_000_000;
    public const long Future = Now + 3600;
    public const long Past = Now - 3600;

    public static readonly string ChangedSel = AbiCodec.Selector("changed(address)");
    public static readonly string OwnerSel = AbiCodec.Selector("identityOwner(address)");
    public static readonly string TopicOwner = AbiCodec.EventTopic("DIDOwnerChanged(address,address,uint256)");
    public static readonly string TopicDelegate = AbiCodec.EventTopic("DIDDelegateChanged(address,bytes32,address,uint256,uint256)");
    public static readonly string TopicAttr = AbiCodec.EventTopic("DIDAttributeChanged(address,bytes32,bytes,uint256,uint256)");

    public static EvmCallResult OkWord(string word) => EvmCallResult.Ok("0x" + word);
    public static string UintWord(long v) => v.ToString("x").PadLeft(64, '0');
    public static string AddrWord(string addr) => addr.Replace("0x", "").ToLowerInvariant().PadLeft(64, '0');

    public static string Bytes32Ascii(string s)
    {
        var w = new byte[32];
        Encoding.ASCII.GetBytes(s).CopyTo(w, 0);
        return Convert.ToHexStringLower(w);
    }

    public static EvmLog OwnerChanged(string owner, long previous)
        => new([TopicOwner], "0x" + AddrWord(owner) + UintWord(previous));

    public static EvmLog DelegateChanged(string type, string addr, long validTo, long previous)
        => new([TopicDelegate], "0x" + Bytes32Ascii(type) + AddrWord(addr) + UintWord(validTo) + UintWord(previous));

    public static EvmLog AttributeChanged(string name, byte[] value, long validTo, long previous)
    {
        var paddedLen = (value.Length + 31) / 32 * 32;
        var valueHex = Convert.ToHexStringLower(value).PadRight(paddedLen * 2, '0');
        var data = Bytes32Ascii(name) + UintWord(128) + UintWord(validTo) + UintWord(previous)
                   + UintWord(value.Length) + valueHex;
        return new EvmLog([TopicAttr], "0x" + data);
    }

    public static EvmLogsResult Logs(params EvmLog[] logs) => EvmLogsResult.Ok(logs);

    public sealed class FakeEvmRpc : IEvmRpcClient
    {
        public EvmCallResult Changed { get; set; } = EvmCallResult.Error;
        public EvmCallResult Owner { get; set; } = EvmCallResult.Error;
        public Dictionary<long, EvmLogsResult> LogsByBlock { get; } = new();
        public bool LogsError { get; set; }

        public Task<EvmCallResult> CallAsync(long chainId, string to, string dataHex, CancellationToken ct = default)
        {
            var selector = dataHex[..10];
            if (string.Equals(selector, ChangedSel, StringComparison.OrdinalIgnoreCase)) return Task.FromResult(Changed);
            if (string.Equals(selector, OwnerSel, StringComparison.OrdinalIgnoreCase)) return Task.FromResult(Owner);
            return Task.FromResult(EvmCallResult.Error);
        }

        public Task<EvmLogsResult> GetLogsAsync(long chainId, string address, string?[] topics, long block, CancellationToken ct = default)
        {
            if (LogsError) return Task.FromResult(EvmLogsResult.Error);
            return Task.FromResult(LogsByBlock.TryGetValue(block, out var l) ? l : EvmLogsResult.Ok([]));
        }

        // Feature 182 write/query methods — not exercised by the read-only ERC-1056 resolution tests.
        public Task<EvmSendResult> SendRawTransactionAsync(long chainId, string rawTxHex, CancellationToken ct = default) => Task.FromResult(EvmSendResult.Error);
        public Task<EvmUIntResult> GetTransactionCountAsync(long chainId, string address, CancellationToken ct = default) => Task.FromResult(EvmUIntResult.Error);
        public Task<EvmUIntResult> EstimateGasAsync(long chainId, string from, string to, string valueHex, string dataHex, CancellationToken ct = default) => Task.FromResult(EvmUIntResult.Error);
        public Task<EvmUIntResult> GetMaxPriorityFeePerGasAsync(long chainId, CancellationToken ct = default) => Task.FromResult(EvmUIntResult.Error);
        public Task<EvmUIntResult> GetBaseFeePerGasAsync(long chainId, CancellationToken ct = default) => Task.FromResult(EvmUIntResult.Error);
        public Task<EvmReceiptResult> GetTransactionReceiptAsync(long chainId, string txHash, CancellationToken ct = default) => Task.FromResult(EvmReceiptResult.Error);
        public Task<EvmUIntResult> GetChainIdAsync(long chainId, CancellationToken ct = default) => Task.FromResult(EvmUIntResult.Error);
    }
}
