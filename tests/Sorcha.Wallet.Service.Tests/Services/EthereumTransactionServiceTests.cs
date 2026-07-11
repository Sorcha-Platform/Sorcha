// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Numerics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.ServiceClients.Evm;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Encryption.Providers;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Implementation;
using Sorcha.Wallet.Service.Configuration;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using Xunit;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Feature 182 (Phase 4) — the transacting orchestrator: US1 (send + status), US3 (guardrails), US2
/// (preview). Uses a fake <see cref="IEvmRpcClient"/> and the real Wallet-Core signer; asserts fail-closed
/// (nothing broadcast) on every refusal.
/// </summary>
public class EthereumTransactionServiceTests
{
    private const string WalletAddress = "ws1qtestwallet0000000000000000000000000000";
    private const string Recipient = "0x1122334455667788990011223344556677889900";
    private const long Sepolia = 11155111;
    private const long Mainnet = 1;
    private static readonly BigInteger SmallValue = new(1_000_000_000_000_000); // 0.001 ETH

    // ── US1: send happy path + status ─────────────────────────────────────────

    [Fact]
    public async Task Send_ValidTransfer_BroadcastsAndReturnsHash()
    {
        var rpc = HappyRpc();
        var service = NewService(rpc);

        var outcome = await service.SendAsync(WalletAddress, Sepolia, Recipient, SmallValue, 0);

        outcome.Ok.Should().BeTrue();
        outcome.TxHash.Should().Be("0xbroadcasthash");
        outcome.From.Should().MatchRegex("^0x[0-9a-fA-F]{40}$");
        outcome.Nonce.Should().Be(5);
        rpc.LastRawTx.Should().StartWith("0x02"); // a type-2 tx was broadcast
    }

    [Theory]
    [InlineData(true, "success")]
    [InlineData(false, "reverted")]
    public async Task Status_MinedReceipt_MapsSuccessOrReverted(bool success, string expected)
    {
        var rpc = HappyRpc();
        rpc.Receipt = EvmReceiptResult.Mined(new EvmReceipt(success, 1250, 21000));
        var service = NewService(rpc);

        var status = await service.GetStatusAsync(Sepolia, "0xdead");

        status.Ok.Should().BeTrue();
        status.Status.Should().Be(expected);
        status.BlockNumber.Should().Be(1250);
    }

    [Fact]
    public async Task Status_PendingReceipt_MapsPending()
    {
        var rpc = HappyRpc();
        rpc.Receipt = EvmReceiptResult.Pending;
        var status = await NewService(rpc).GetStatusAsync(Sepolia, "0xdead");
        status.Status.Should().Be("pending");
    }

    [Fact]
    public async Task Status_DisabledChain_Refused()
    {
        var status = await NewService(HappyRpc()).GetStatusAsync(999, "0xdead");
        status.Ok.Should().BeFalse();
        status.Reason.Should().Be(TransferRefusalReason.ChainNotEnabled);
    }

    // ── US3: guardrails (each refuses with no broadcast) ──────────────────────

    [Fact]
    public async Task Send_DisabledChain_Refused_NoBroadcast()
    {
        var rpc = HappyRpc();
        var outcome = await NewService(rpc).SendAsync(WalletAddress, 999, Recipient, SmallValue, 0);
        outcome.Reason.Should().Be(TransferRefusalReason.ChainNotEnabled);
        rpc.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task Send_MainnetWithoutAllow_Refused_NoBroadcast()
    {
        var rpc = HappyRpc();
        var outcome = await NewService(rpc, allowMainnet: false).SendAsync(WalletAddress, Mainnet, Recipient, SmallValue, 0);
        outcome.Reason.Should().Be(TransferRefusalReason.MainnetNotAllowed);
        rpc.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task Send_ValueOverCap_Refused()
    {
        var rpc = HappyRpc();
        var over = BigInteger.Parse("200000000000000000"); // 0.2 ETH > 0.1 cap
        var outcome = await NewService(rpc).SendAsync(WalletAddress, Sepolia, Recipient, over, 0);
        outcome.Reason.Should().Be(TransferRefusalReason.ValueOverCap);
        rpc.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task Send_FeeOverCeiling_Refused()
    {
        var rpc = HappyRpc();
        rpc.BaseFee = EvmUIntResult.Ok(BigInteger.Parse("300000000000")); // 300 gwei → maxFee 600+ gwei > 500 ceiling
        var outcome = await NewService(rpc).SendAsync(WalletAddress, Sepolia, Recipient, SmallValue, 0);
        outcome.Reason.Should().Be(TransferRefusalReason.FeeOverCeiling);
        rpc.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task Send_InvalidAddress_Refused()
    {
        var rpc = HappyRpc();
        var outcome = await NewService(rpc).SendAsync(WalletAddress, Sepolia, "0xnothex", SmallValue, 0);
        outcome.Reason.Should().Be(TransferRefusalReason.InvalidAddress);
        rpc.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task Send_EstimateFails_Refused_NoBroadcast()
    {
        var rpc = HappyRpc();
        rpc.Gas = EvmUIntResult.Error;
        var outcome = await NewService(rpc).SendAsync(WalletAddress, Sepolia, Recipient, SmallValue, 0);
        outcome.Reason.Should().Be(TransferRefusalReason.EstimateFailed);
        rpc.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task Send_NonceRpcError_Refused()
    {
        var rpc = HappyRpc();
        rpc.Nonce = EvmUIntResult.Error;
        var outcome = await NewService(rpc).SendAsync(WalletAddress, Sepolia, Recipient, SmallValue, 0);
        outcome.Reason.Should().Be(TransferRefusalReason.RpcError);
        rpc.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task Send_BroadcastFails_ReportsBroadcastFailed()
    {
        var rpc = HappyRpc();
        rpc.Send = EvmSendResult.Error;
        var outcome = await NewService(rpc).SendAsync(WalletAddress, Sepolia, Recipient, SmallValue, 0);
        outcome.Reason.Should().Be(TransferRefusalReason.BroadcastFailed);
    }

    [Fact]
    public async Task Send_MainnetWithAllow_Broadcasts()
    {
        var rpc = HappyRpc();
        var outcome = await NewService(rpc, allowMainnet: true).SendAsync(WalletAddress, Mainnet, Recipient, SmallValue, 0);
        outcome.Ok.Should().BeTrue();
    }

    // ── US2: preview (no signing, no broadcast) ───────────────────────────────

    [Fact]
    public async Task Preview_ReturnsCosts_WithoutBroadcasting()
    {
        var rpc = HappyRpc();
        var outcome = await NewService(rpc).PreviewAsync(WalletAddress, Sepolia, Recipient, SmallValue, 0);

        outcome.Ok.Should().BeTrue();
        var p = outcome.Preparation!;
        p.Nonce.Should().Be(5);
        p.GasLimit.Should().Be(21000);
        // total = value + gas * maxFee ; maxFee = baseFee(10g)*2 + priority(1.5g) = 21.5 gwei
        var maxFee = BigInteger.Parse("10000000000") * 2 + BigInteger.Parse("1500000000");
        p.MaxFeePerGasWei.Should().Be(maxFee);
        p.EstimatedTotalCostWei.Should().Be(SmallValue + 21000 * maxFee);
        rpc.SendCount.Should().Be(0); // nothing broadcast
    }

    [Fact]
    public async Task Preview_PolicyViolation_Refused()
    {
        var outcome = await NewService(HappyRpc()).PreviewAsync(WalletAddress, 999, Recipient, SmallValue, 0);
        outcome.Ok.Should().BeFalse();
        outcome.Reason.Should().Be(TransferRefusalReason.ChainNotEnabled);
    }

    // ── setup ─────────────────────────────────────────────────────────────────

    private static FakeEvmRpc HappyRpc() => new()
    {
        Nonce = EvmUIntResult.Ok(5),
        BaseFee = EvmUIntResult.Ok(BigInteger.Parse("10000000000")),   // 10 gwei
        Priority = EvmUIntResult.Ok(BigInteger.Parse("1500000000")),   // 1.5 gwei
        Gas = EvmUIntResult.Ok(21000),
        Send = EvmSendResult.Ok("0xbroadcasthash"),
        Receipt = EvmReceiptResult.Pending
    };

    private static EthereumTransactionService NewService(FakeEvmRpc rpc, bool allowMainnet = false)
    {
        var identity = NewIdentity();
        var options = Options.Create(new EthereumTransactionOptions
        {
            EnabledChainIds = [Sepolia, Mainnet],
            AllowMainnet = allowMainnet,
            MaxValueWei = "100000000000000000",  // 0.1 ETH
            MaxFeePerGasWei = "500000000000",    // 500 gwei
            DefaultPriorityFeeWei = "1500000000"
        });

        return new EthereumTransactionService(identity, identity, rpc, options,
            Mock.Of<ILogger<EthereumTransactionService>>());
    }

    private static EthereumIdentityService NewIdentity()
    {
        var km = new KeyManagementService(
            (Sorcha.Wallet.Core.Encryption.Interfaces.IKeyProtectionProvider)
                new LocalEncryptionProvider(Mock.Of<ILogger<LocalEncryptionProvider>>()),
            Mock.Of<Sorcha.Cryptography.Interfaces.ICryptoModule>(),
            Mock.Of<Sorcha.Cryptography.Interfaces.IWalletUtilities>(),
            Mock.Of<ILogger<KeyManagementService>>());

        var seed = new byte[64];
        for (var i = 0; i < 64; i++) seed[i] = (byte)(i + 1);
        var (blob, encKeyId) = km.EncryptPrivateKeyAsync(seed, km.GetDefaultKeyId()).GetAwaiter().GetResult();

        var wallet = new WalletEntity
        {
            Address = WalletAddress, EncryptedPrivateKey = blob, EncryptedMasterKeyBlob = blob,
            EncryptionKeyId = encKeyId, RecoveryEnabled = false, Algorithm = "ED25519",
            Owner = "owner", Tenant = "tenant", Name = "test"
        };

        var repo = new Mock<IWalletRepository>();
        repo.Setup(r => r.GetByAddressAsync(WalletAddress, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        return new EthereumIdentityService(repo.Object, km, Mock.Of<ILogger<EthereumIdentityService>>());
    }

    private sealed class FakeEvmRpc : IEvmRpcClient
    {
        public EvmUIntResult Nonce { get; set; } = EvmUIntResult.Error;
        public EvmUIntResult BaseFee { get; set; } = EvmUIntResult.Error;
        public EvmUIntResult Priority { get; set; } = EvmUIntResult.Error;
        public EvmUIntResult Gas { get; set; } = EvmUIntResult.Error;
        public EvmSendResult Send { get; set; } = EvmSendResult.Error;
        public EvmReceiptResult Receipt { get; set; } = EvmReceiptResult.Error;
        public int SendCount { get; private set; }
        public string? LastRawTx { get; private set; }

        public Task<EvmUIntResult> GetTransactionCountAsync(long chainId, string address, CancellationToken ct = default) => Task.FromResult(Nonce);
        public Task<EvmUIntResult> GetBaseFeePerGasAsync(long chainId, CancellationToken ct = default) => Task.FromResult(BaseFee);
        public Task<EvmUIntResult> GetMaxPriorityFeePerGasAsync(long chainId, CancellationToken ct = default) => Task.FromResult(Priority);
        public Task<EvmUIntResult> EstimateGasAsync(long chainId, string from, string to, string valueHex, string dataHex, CancellationToken ct = default) => Task.FromResult(Gas);
        public Task<EvmUIntResult> GetChainIdAsync(long chainId, CancellationToken ct = default) => Task.FromResult(EvmUIntResult.Ok(chainId));

        public Task<EvmSendResult> SendRawTransactionAsync(long chainId, string rawTxHex, CancellationToken ct = default)
        {
            SendCount++;
            LastRawTx = rawTxHex;
            return Task.FromResult(Send);
        }

        public Task<EvmReceiptResult> GetTransactionReceiptAsync(long chainId, string txHash, CancellationToken ct = default) => Task.FromResult(Receipt);

        // Read-only DID methods — unused here.
        public Task<EvmCallResult> CallAsync(long chainId, string to, string dataHex, CancellationToken ct = default) => Task.FromResult(EvmCallResult.Error);
        public Task<EvmLogsResult> GetLogsAsync(long chainId, string address, string?[] topics, long block, CancellationToken ct = default) => Task.FromResult(EvmLogsResult.Error);
    }
}
