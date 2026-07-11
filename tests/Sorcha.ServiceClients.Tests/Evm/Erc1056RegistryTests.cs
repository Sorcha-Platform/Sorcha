// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.ServiceClients.Evm;

namespace Sorcha.ServiceClients.Tests.Evm;

public class Erc1056RegistryTests
{
    private const string Identity = "0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B";
    private const string NewOwner = "0x1111111111111111111111111111111111111111";
    private const string DelegateAddr = "0x2222222222222222222222222222222222222222";
    private const long Now = 1_800_000_000; // fixed clock
    private const long Future = Now + 3600;
    private const long Past = Now - 3600;

    private static readonly string ChangedSel = AbiCodec.Selector("changed(address)");
    private static readonly string OwnerSel = AbiCodec.Selector("identityOwner(address)");
    private static readonly string TopicOwner = AbiCodec.EventTopic("DIDOwnerChanged(address,address,uint256)");
    private static readonly string TopicDelegate = AbiCodec.EventTopic("DIDDelegateChanged(address,bytes32,address,uint256,uint256)");
    private static readonly string TopicAttr = AbiCodec.EventTopic("DIDAttributeChanged(address,bytes32,bytes,uint256,uint256)");

    [Fact]
    public async Task ReadAsync_ChangedZero_ReturnsNoHistory()
    {
        var rpc = new FakeRpc { Changed = Ok(UintWord(0)) };
        var state = await Registry(rpc).ReadAsync(1, Identity);

        state.NoHistory.Should().BeTrue();
        state.RpcError.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_UnconfiguredChain_ReturnsNoHistory()
    {
        var rpc = new FakeRpc { Changed = EvmCallResult.NotConfigured };
        (await Registry(rpc).ReadAsync(1, Identity)).NoHistory.Should().BeTrue();
    }

    [Fact]
    public async Task ReadAsync_ChangedError_ReturnsRpcError()
    {
        var rpc = new FakeRpc { Changed = EvmCallResult.Error };
        (await Registry(rpc).ReadAsync(1, Identity)).RpcError.Should().BeTrue();
    }

    [Fact]
    public async Task ReadAsync_OwnerRotation_ReturnsNewOwner()
    {
        var rpc = new FakeRpc
        {
            Changed = Ok(UintWord(100)),
            Owner = Ok(AddrWord(NewOwner)),
            Logs = { [100] = Logs(OwnerChanged(NewOwner, previous: 0)) }
        };

        var state = await Registry(rpc).ReadAsync(1, Identity);

        state.OwnerAddress.Should().Be(NewOwner.ToLowerInvariant());
        state.RpcError.Should().BeFalse();
        state.NoHistory.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_ActiveVeriKeyDelegate_Included_ExpiredExcluded()
    {
        var active = new FakeRpc
        {
            Changed = Ok(UintWord(100)),
            Owner = Ok(AddrWord(Identity)),
            Logs = { [100] = Logs(DelegateChanged("veriKey", DelegateAddr, Future, previous: 0)) }
        };
        var expired = new FakeRpc
        {
            Changed = Ok(UintWord(100)),
            Owner = Ok(AddrWord(Identity)),
            Logs = { [100] = Logs(DelegateChanged("veriKey", DelegateAddr, Past, previous: 0)) }
        };

        (await Registry(active).ReadAsync(1, Identity)).Delegates.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new Erc1056Delegate("veriKey", DelegateAddr.ToLowerInvariant()));
        (await Registry(expired).ReadAsync(1, Identity)).Delegates.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_KeyAttributes_Secp256k1AndEd25519_Included_SvcIgnored()
    {
        var secpKey = Convert.FromHexString("02" + new string('a', 64)); // 33-byte compressed-shaped
        var edKey = new byte[32];
        edKey[0] = 0xED;

        var rpc = new FakeRpc
        {
            Changed = Ok(UintWord(100)),
            Owner = Ok(AddrWord(Identity)),
            Logs =
            {
                [100] = Logs(
                    AttributeChanged("did/pub/Secp256k1/veriKey/hex", secpKey, Future, previous: 90)),
                [90] = Logs(
                    AttributeChanged("did/pub/Ed25519/veriKey/base64", edKey, Future, previous: 80)),
                [80] = Logs(
                    AttributeChanged("did/svc/MessagingService", Encoding.ASCII.GetBytes("https://x"), Future, previous: 0))
            }
        };

        var state = await Registry(rpc).ReadAsync(1, Identity);

        state.Attributes.Should().HaveCount(2);
        state.Attributes.Select(a => a.Algorithm).Should().BeEquivalentTo(["Secp256k1", "Ed25519"]);
        state.Attributes.Single(a => a.Algorithm == "Secp256k1").Key.Should().Equal(secpKey);
    }

    [Fact]
    public async Task ReadAsync_Supersession_KeepsNewestPerDelegate()
    {
        // Newest (block 100) revokes (validTo past); older (block 50) was valid — newest-first wins → excluded.
        var rpc = new FakeRpc
        {
            Changed = Ok(UintWord(100)),
            Owner = Ok(AddrWord(Identity)),
            Logs =
            {
                [100] = Logs(DelegateChanged("veriKey", DelegateAddr, Past, previous: 50)),
                [50] = Logs(DelegateChanged("veriKey", DelegateAddr, Future, previous: 0))
            }
        };

        (await Registry(rpc).ReadAsync(1, Identity)).Delegates.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_GetLogsError_ReturnsRpcError()
    {
        var rpc = new FakeRpc
        {
            Changed = Ok(UintWord(100)),
            Owner = Ok(AddrWord(Identity)),
            LogsError = true
        };

        (await Registry(rpc).ReadAsync(1, Identity)).RpcError.Should().BeTrue();
    }

    [Fact]
    public async Task ReadAsync_ExceedsMaxHistoryHops_FailsClosed()
    {
        // A self-referential previousChange loop that never reaches 0.
        var rpc = new FakeRpc { Changed = Ok(UintWord(100)), Owner = Ok(AddrWord(Identity)) };
        for (long b = 100; b > 0; b--)
        {
            rpc.Logs[b] = Logs(DelegateChanged("veriKey", DelegateAddr, Future, previous: b == 1 ? 200 : b - 1));
        }
        rpc.Logs[200] = Logs(DelegateChanged("veriKey", DelegateAddr, Future, previous: 199));

        var options = new EvmRpcOptions { MaxHistoryHops = 5, AllowPrivateAddresses = true };
        var registry = new Erc1056Registry(rpc, options, NullLogger.Instance, () => Now);

        (await registry.ReadAsync(1, Identity)).RpcError.Should().BeTrue();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Erc1056Registry Registry(FakeRpc rpc)
        => new(rpc, new EvmRpcOptions { AllowPrivateAddresses = true }, NullLogger.Instance, () => Now);

    private static EvmCallResult Ok(string word) => EvmCallResult.Ok("0x" + word);
    private static string UintWord(long v) => v.ToString("x").PadLeft(64, '0');
    private static string AddrWord(string addr) => addr.Replace("0x", "").ToLowerInvariant().PadLeft(64, '0');

    private static string Bytes32Ascii(string s)
    {
        var w = new byte[32];
        Encoding.ASCII.GetBytes(s).CopyTo(w, 0);
        return Convert.ToHexStringLower(w);
    }

    private static EvmLog OwnerChanged(string owner, long previous)
        => new([TopicOwner], "0x" + AddrWord(owner) + UintWord(previous));

    private static EvmLog DelegateChanged(string type, string addr, long validTo, long previous)
        => new([TopicDelegate], "0x" + Bytes32Ascii(type) + AddrWord(addr) + UintWord(validTo) + UintWord(previous));

    private static EvmLog AttributeChanged(string name, byte[] value, long validTo, long previous)
    {
        // head: name(0) offset(1)=0x80 validTo(2) previousChange(3); tail: length + value(padded)
        var paddedLen = (value.Length + 31) / 32 * 32;
        var valueHex = Convert.ToHexStringLower(value).PadRight(paddedLen * 2, '0');
        var data = Bytes32Ascii(name) + UintWord(128) + UintWord(validTo) + UintWord(previous)
                   + UintWord(value.Length) + valueHex;
        return new EvmLog([TopicAttr], "0x" + data);
    }

    private static EvmLogsResult Logs(params EvmLog[] logs) => EvmLogsResult.Ok(logs);

    private sealed class FakeRpc : IEvmRpcClient
    {
        public EvmCallResult Changed { get; set; } = EvmCallResult.Error;
        public EvmCallResult Owner { get; set; } = EvmCallResult.Error;
        public Dictionary<long, EvmLogsResult> Logs { get; } = new();
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
            return Task.FromResult(Logs.TryGetValue(block, out var l) ? l : EvmLogsResult.Ok([]));
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
