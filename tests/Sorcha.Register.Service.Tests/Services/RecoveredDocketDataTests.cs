// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Services.Implementation;
using Sorcha.ServiceClients.Register;
using Xunit;

namespace Sorcha.Register.Service.Tests.Services;

/// <summary>
/// #1653 — recovery read every recovered docket's data as a transaction ARRAY, while every producer
/// writes a docket OBJECT. It failed at byte 1 on each docket, logged, and reported "processed N
/// dockets", and no recovered Action transaction was ever routed. These feed the reader bytes made
/// the way the real producers make them — not a hand-built array, which is how it survived.
/// </summary>
public class RecoveredDocketDataTests
{
    private static TransactionModel ActionTx(string id) => new()
    {
        RegisterId = "reg-1653",
        TxId = id,
        PrevTxId = string.Empty,
        Version = 1,
        SenderWallet = "ws1sender",
        RecipientsWallets = ["ws1recipient"],
        TimeStamp = DateTime.UtcNow,
        PayloadCount = 0,
        Payloads = [],
        Signature = "sig",
        MetaData = new TransactionMetaData { TransactionType = TransactionType.Action },
    };

    private static DocketModel Docket() => new()
    {
        DocketId = "d-4",
        RegisterId = "reg-1653",
        DocketNumber = 4,
        DocketHash = "hash-4",
        CreatedAt = DateTimeOffset.UtcNow,
        Transactions = [ActionTx("tx-a"), ActionTx("tx-b")],
        ProposerValidatorId = "local-validator",
        MerkleRoot = "root",
    };

    [Fact]
    public void Reads_TheBytesRelayMessageHandlerProduces()
    {
        // RelayMessageHandler: JsonSerializer.SerializeToUtf8Bytes(docket) — default options.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Docket());

        RecoveredDocketData.TryReadTransactions(bytes, out var transactions).Should().BeTrue();

        transactions.Select(t => t.TxId).Should().Equal("tx-a", "tx-b");
        transactions.Should().OnlyContain(t => t.MetaData!.TransactionType == TransactionType.Action
            && t.RecipientsWallets!.Contains("ws1recipient"));
    }

    [Fact]
    public void Reads_TheBytesRegisterSyncGrpcServiceProduces()
    {
        // RegisterSyncGrpcService: ByteString.CopyFromUtf8(JsonSerializer.Serialize(docket)).
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Docket()));

        RecoveredDocketData.TryReadTransactions(bytes, out var transactions).Should().BeTrue();

        transactions.Should().HaveCount(2);
    }

    [Fact]
    public void StillReads_ABareTransactionArray()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new[] { ActionTx("tx-legacy") });

        RecoveredDocketData.TryReadTransactions(bytes, out var transactions).Should().BeTrue();

        transactions.Should().ContainSingle().Which.TxId.Should().Be("tx-legacy");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("\"a string\"")]
    [InlineData("{\"Transactions\": 5}")]
    public void UnreadableData_IsReportedAsUnreadable_NotAsAnEmptyDocket(string data)
    {
        // A docket that could not be read must not count as processed.
        RecoveredDocketData.TryReadTransactions(Encoding.UTF8.GetBytes(data), out _).Should().BeFalse();
    }
}
