// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Services;
using Xunit;

namespace Sorcha.Register.Service.Tests.Services;

/// <summary>
/// Tests for the create-on-sync genesis control-record extraction used when a peer
/// replicates a regular register's genesis docket to a node that has not created it
/// locally yet.
/// </summary>
public class GenesisControlRecordExtractorTests
{
    private static TransactionModel BuildControlTx(RegisterControlRecord record)
    {
        var json = JsonSerializer.Serialize(record);
        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return new TransactionModel
        {
            TxId = "tx-control",
            MetaData = new TransactionMetaData { TransactionType = TransactionType.Control },
            Payloads = [new PayloadModel { Data = data }]
        };
    }

    private static RegisterControlRecord SampleRecord(string name = "Assured Identity") => new()
    {
        RegisterId = "deccbf4dc9ad4edebe5d6a3651da80b9",
        Name = name,
        Description = "Citizen identity register",
        CreatedAt = DateTimeOffset.UtcNow,
        Attestations =
        [
            new RegisterAttestation
            {
                Role = RegisterRole.Owner,
                Subject = "did:sorcha:org:ws1test",
                PublicKey = "AAAA",
                Signature = "BBBB",
                Algorithm = SignatureAlgorithm.ED25519,
                GrantedAt = DateTimeOffset.UtcNow
            }
        ]
    };

    [Fact]
    public void TryExtract_ControlTransaction_ReturnsRecordWithName()
    {
        var txs = new List<TransactionModel> { BuildControlTx(SampleRecord()) };

        var result = GenesisControlRecordExtractor.TryExtract(txs);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Assured Identity");
        result.Description.Should().Be("Citizen identity register");
    }

    [Fact]
    public void TryExtract_DevModeRegister_PreservesDevModeFromCryptoPolicy()
    {
        // A DevMode register bakes the flag into the genesis crypto policy so a replica reads it
        // from the synced control record and respects the plaintext posture (Feature 137).
        var record = SampleRecord();
        record.CryptoPolicy = CryptoPolicy.CreateDefault();
        record.CryptoPolicy.DevMode = true;
        var txs = new List<TransactionModel> { BuildControlTx(record) };

        var result = GenesisControlRecordExtractor.TryExtract(txs);

        result.Should().NotBeNull();
        result!.CryptoPolicy.Should().NotBeNull();
        result.CryptoPolicy!.DevMode.Should().BeTrue(
            "the replica's auto-create reads controlRecord.CryptoPolicy.DevMode from the synced genesis");
    }

    [Fact]
    public void TryExtract_NormalRegister_DevModeDefaultsFalse()
    {
        // Default (production) register: no DevMode. Fail-safe toward encryption.
        var record = SampleRecord();
        record.CryptoPolicy = CryptoPolicy.CreateDefault();
        var txs = new List<TransactionModel> { BuildControlTx(record) };

        var result = GenesisControlRecordExtractor.TryExtract(txs);

        result.Should().NotBeNull();
        (result!.CryptoPolicy?.DevMode ?? false).Should().BeFalse();
    }

    [Fact]
    public void TryExtract_NoControlTransaction_ReturnsNull()
    {
        var txs = new List<TransactionModel>
        {
            new()
            {
                TxId = "tx-action",
                MetaData = new TransactionMetaData { TransactionType = TransactionType.Action },
                Payloads = [new PayloadModel { Data = "e30=" }] // {}
            }
        };

        GenesisControlRecordExtractor.TryExtract(txs).Should().BeNull();
    }

    [Fact]
    public void TryExtract_NullTransactions_ReturnsNull()
    {
        GenesisControlRecordExtractor.TryExtract(null).Should().BeNull();
    }

    [Fact]
    public void TryExtract_ControlTransactionWithGarbagePayload_ReturnsNullWithoutThrowing()
    {
        var txs = new List<TransactionModel>
        {
            new()
            {
                TxId = "tx-bad",
                MetaData = new TransactionMetaData { TransactionType = TransactionType.Control },
                Payloads = [new PayloadModel { Data = Convert.ToBase64String(Encoding.UTF8.GetBytes("not json")) }]
            }
        };

        GenesisControlRecordExtractor.TryExtract(txs).Should().BeNull();
    }
}
