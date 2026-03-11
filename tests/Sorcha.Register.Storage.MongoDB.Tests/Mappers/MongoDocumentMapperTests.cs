// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using FluentAssertions;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Storage.MongoDB.Mappers;
using Xunit;

namespace Sorcha.Register.Storage.MongoDB.Tests.Mappers;

public class MongoDocumentMapperTests
{
    private static readonly byte[] TestSignatureBytes = new byte[64];
    private static readonly byte[] TestDataBytes = [0x01, 0x02, 0x03, 0xFF, 0xFE, 0xFD];
    private static readonly byte[] TestHashBytes = new byte[32];
    private static readonly byte[] TestIvBytes = [0xAA, 0xBB, 0xCC, 0xDD];
    private static readonly byte[] TestChallengeBytes = [0x10, 0x20, 0x30, 0x40];

    static MongoDocumentMapperTests()
    {
        // Fill with recognizable patterns
        for (var i = 0; i < TestSignatureBytes.Length; i++)
            TestSignatureBytes[i] = (byte)(i + 1);
        for (var i = 0; i < TestHashBytes.Length; i++)
            TestHashBytes[i] = (byte)(i + 100);
    }

    // --- ToMongoDocument ---

    [Fact]
    public void ToMongoDocument_MapsScalarFields()
    {
        var transaction = CreateTestTransaction();

        var result = MongoDocumentMapper.ToMongoDocument(transaction);

        result.Context.Should().Be(transaction.Context);
        result.Type.Should().Be(transaction.Type);
        result.Id.Should().Be(transaction.Id);
        result.RegisterId.Should().Be(transaction.RegisterId);
        result.TxId.Should().Be(transaction.TxId);
        result.PrevTxId.Should().Be(transaction.PrevTxId);
        result.DocketNumber.Should().Be(transaction.DocketNumber);
        result.Version.Should().Be(transaction.Version);
        result.SenderWallet.Should().Be(transaction.SenderWallet);
        result.RecipientsWallets.Should().BeEquivalentTo(transaction.RecipientsWallets);
        result.TimeStamp.Should().Be(transaction.TimeStamp);
        result.PayloadCount.Should().Be(transaction.PayloadCount);
    }

    [Fact]
    public void ToMongoDocument_DecodesSignatureToBytes()
    {
        var transaction = CreateTestTransaction();

        var result = MongoDocumentMapper.ToMongoDocument(transaction);

        result.Signature.Should().Equal(TestSignatureBytes);
    }

    [Fact]
    public void ToMongoDocument_DecodesPayloadDataToBytes()
    {
        var transaction = CreateTestTransaction();

        var result = MongoDocumentMapper.ToMongoDocument(transaction);

        result.Payloads.Should().HaveCount(1);
        result.Payloads[0].Data.Should().Equal(TestDataBytes);
    }

    [Fact]
    public void ToMongoDocument_DecodesPayloadHashToBytes()
    {
        var transaction = CreateTestTransaction();

        var result = MongoDocumentMapper.ToMongoDocument(transaction);

        result.Payloads[0].Hash.Should().Equal(TestHashBytes);
    }

    [Fact]
    public void ToMongoDocument_DecodesIvToBytes()
    {
        var transaction = CreateTestTransaction();

        var result = MongoDocumentMapper.ToMongoDocument(transaction);

        result.Payloads[0].IV.Should().NotBeNull();
        result.Payloads[0].IV!.Data.Should().Equal(TestIvBytes);
        result.Payloads[0].IV!.Address.Should().Be("test-address");
    }

    [Fact]
    public void ToMongoDocument_DecodesChallengeDataToBytes()
    {
        var transaction = CreateTestTransaction();

        var result = MongoDocumentMapper.ToMongoDocument(transaction);

        result.Payloads[0].Challenges.Should().HaveCount(1);
        result.Payloads[0].Challenges![0].Data.Should().Equal(TestChallengeBytes);
        result.Payloads[0].Challenges![0].Address.Should().Be("wallet-1");
    }

    [Fact]
    public void ToMongoDocument_PreservesContentTypeAndEncoding()
    {
        var transaction = CreateTestTransaction();

        var result = MongoDocumentMapper.ToMongoDocument(transaction);

        result.Payloads[0].ContentType.Should().Be("application/json");
        result.Payloads[0].ContentEncoding.Should().Be("base64url");
    }

    [Fact]
    public void ToMongoDocument_PreservesMetaData()
    {
        var transaction = CreateTestTransaction();

        var result = MongoDocumentMapper.ToMongoDocument(transaction);

        result.MetaData.Should().NotBeNull();
        result.MetaData!.TransactionType.Should().Be(TransactionType.Action);
        result.MetaData.BlueprintId.Should().Be("bp-1");
    }

    [Fact]
    public void ToMongoDocument_HandlesNullPayloads()
    {
        var transaction = CreateTestTransaction();
        transaction.Payloads = null!;

        var result = MongoDocumentMapper.ToMongoDocument(transaction);

        result.Payloads.Should().BeEmpty();
    }

    [Fact]
    public void ToMongoDocument_HandlesNullIvAndChallenges()
    {
        var transaction = CreateTestTransaction();
        transaction.Payloads[0].IV = null;
        transaction.Payloads[0].Challenges = null;

        var result = MongoDocumentMapper.ToMongoDocument(transaction);

        result.Payloads[0].IV.Should().BeNull();
        result.Payloads[0].Challenges.Should().BeNull();
    }

    [Fact]
    public void ToMongoDocument_HandlesEmptySignature()
    {
        var transaction = CreateTestTransaction();
        transaction.Signature = string.Empty;

        var result = MongoDocumentMapper.ToMongoDocument(transaction);

        result.Signature.Should().BeEmpty();
    }

    // --- ToTransactionModel ---

    [Fact]
    public void ToTransactionModel_MapsScalarFields()
    {
        var transaction = CreateTestTransaction();
        var mongoDoc = MongoDocumentMapper.ToMongoDocument(transaction);

        var result = MongoDocumentMapper.ToTransactionModel(mongoDoc);

        result.Context.Should().Be(transaction.Context);
        result.Type.Should().Be(transaction.Type);
        result.Id.Should().Be(transaction.Id);
        result.RegisterId.Should().Be(transaction.RegisterId);
        result.TxId.Should().Be(transaction.TxId);
        result.PrevTxId.Should().Be(transaction.PrevTxId);
        result.SenderWallet.Should().Be(transaction.SenderWallet);
        result.Version.Should().Be(transaction.Version);
    }

    [Fact]
    public void ToTransactionModel_EncodesSignatureAsBase64Url()
    {
        var transaction = CreateTestTransaction();
        var mongoDoc = MongoDocumentMapper.ToMongoDocument(transaction);

        var result = MongoDocumentMapper.ToTransactionModel(mongoDoc);

        result.Signature.Should().Be(Base64Url.EncodeToString(TestSignatureBytes));
        // Verify no legacy chars
        result.Signature.Should().NotContainAny("+", "/", "=");
    }

    [Fact]
    public void ToTransactionModel_EncodesPayloadDataAsBase64Url()
    {
        var transaction = CreateTestTransaction();
        var mongoDoc = MongoDocumentMapper.ToMongoDocument(transaction);

        var result = MongoDocumentMapper.ToTransactionModel(mongoDoc);

        result.Payloads[0].Data.Should().Be(Base64Url.EncodeToString(TestDataBytes));
    }

    [Fact]
    public void ToTransactionModel_EncodesPayloadHashAsBase64Url()
    {
        var transaction = CreateTestTransaction();
        var mongoDoc = MongoDocumentMapper.ToMongoDocument(transaction);

        var result = MongoDocumentMapper.ToTransactionModel(mongoDoc);

        result.Payloads[0].Hash.Should().Be(Base64Url.EncodeToString(TestHashBytes));
    }

    [Fact]
    public void ToTransactionModel_PreservesContentTypeAndEncoding()
    {
        var transaction = CreateTestTransaction();
        var mongoDoc = MongoDocumentMapper.ToMongoDocument(transaction);

        var result = MongoDocumentMapper.ToTransactionModel(mongoDoc);

        result.Payloads[0].ContentType.Should().Be("application/json");
        result.Payloads[0].ContentEncoding.Should().Be("base64url");
    }

    // --- Round-trip tests ---

    [Fact]
    public void RoundTrip_TransactionModel_ByteIdentical()
    {
        var original = CreateTestTransaction();
        // Use Base64url for the original (new format)
        original.Signature = Base64Url.EncodeToString(TestSignatureBytes);
        original.Payloads[0].Data = Base64Url.EncodeToString(TestDataBytes);
        original.Payloads[0].Hash = Base64Url.EncodeToString(TestHashBytes);
        original.Payloads[0].IV!.Data = Base64Url.EncodeToString(TestIvBytes);
        original.Payloads[0].Challenges![0].Data = Base64Url.EncodeToString(TestChallengeBytes);

        var mongoDoc = MongoDocumentMapper.ToMongoDocument(original);
        var result = MongoDocumentMapper.ToTransactionModel(mongoDoc);

        result.Signature.Should().Be(original.Signature);
        result.Payloads[0].Data.Should().Be(original.Payloads[0].Data);
        result.Payloads[0].Hash.Should().Be(original.Payloads[0].Hash);
        result.Payloads[0].IV!.Data.Should().Be(original.Payloads[0].IV!.Data);
        result.Payloads[0].Challenges![0].Data.Should().Be(original.Payloads[0].Challenges![0].Data);
    }

    [Fact]
    public void RoundTrip_LegacyBase64_NormalizedToBase64Url()
    {
        var original = CreateTestTransaction();
        // Use legacy Base64 for the original (old format)
        original.Signature = Convert.ToBase64String(TestSignatureBytes);
        original.Payloads[0].Data = Convert.ToBase64String(TestDataBytes);
        original.Payloads[0].Hash = Convert.ToBase64String(TestHashBytes);

        var mongoDoc = MongoDocumentMapper.ToMongoDocument(original);
        var result = MongoDocumentMapper.ToTransactionModel(mongoDoc);

        // After round-trip, legacy Base64 is normalized to Base64url
        result.Signature.Should().Be(Base64Url.EncodeToString(TestSignatureBytes));
        result.Payloads[0].Data.Should().Be(Base64Url.EncodeToString(TestDataBytes));
        result.Payloads[0].Hash.Should().Be(Base64Url.EncodeToString(TestHashBytes));

        // Same bytes despite different string representation
        Base64Url.DecodeFromChars(result.Signature).Should().Equal(
            Convert.FromBase64String(original.Signature));
    }

    // --- Legacy detection in mapper ---

    [Fact]
    public void ToMongoDocument_LegacyBase64Signature_DecodesCorrectly()
    {
        var transaction = CreateTestTransaction();
        transaction.Signature = Convert.ToBase64String(TestSignatureBytes); // Legacy format with +, /, =

        var result = MongoDocumentMapper.ToMongoDocument(transaction);

        result.Signature.Should().Equal(TestSignatureBytes);
    }

    [Fact]
    public void ToMongoDocument_NullChallengeData_MapsToNullBytes()
    {
        var transaction = CreateTestTransaction();
        transaction.Payloads[0].Challenges![0].Data = null;

        var result = MongoDocumentMapper.ToMongoDocument(transaction);

        result.Payloads[0].Challenges![0].Data.Should().BeNull();
    }

    [Fact]
    public void ToTransactionModel_NullChallengeBytes_MapsToNullString()
    {
        var transaction = CreateTestTransaction();
        var mongoDoc = MongoDocumentMapper.ToMongoDocument(transaction);
        mongoDoc.Payloads[0].Challenges![0].Data = null;

        var result = MongoDocumentMapper.ToTransactionModel(mongoDoc);

        result.Payloads[0].Challenges![0].Data.Should().BeNull();
    }

    // --- Null argument handling ---

    [Fact]
    public void ToMongoDocument_NullTransaction_Throws()
    {
        var act = () => MongoDocumentMapper.ToMongoDocument(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToTransactionModel_NullDocument_Throws()
    {
        var act = () => MongoDocumentMapper.ToTransactionModel(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // --- Helpers ---

    private static TransactionModel CreateTestTransaction()
    {
        return new TransactionModel
        {
            Context = "https://sorcha.dev/contexts/blockchain/v1.jsonld",
            Type = "Transaction",
            Id = "did:sorcha:register:test-register/tx/abcdef",
            RegisterId = "test-register",
            TxId = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890",
            PrevTxId = "0000001234567890abcdef1234567890abcdef1234567890abcdef1234567890",
            DocketNumber = 42,
            Version = 1,
            SenderWallet = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa",
            RecipientsWallets = ["1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2"],
            TimeStamp = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            MetaData = new TransactionMetaData
            {
                RegisterId = "test-register",
                TransactionType = TransactionType.Action,
                BlueprintId = "bp-1",
                InstanceId = "inst-1",
                ActionId = 1
            },
            PayloadCount = 1,
            Payloads =
            [
                new PayloadModel
                {
                    WalletAccess = ["wallet-1"],
                    PayloadSize = (ulong)TestDataBytes.Length,
                    Hash = Base64Url.EncodeToString(TestHashBytes),
                    Data = Base64Url.EncodeToString(TestDataBytes),
                    PayloadFlags = "encrypted",
                    IV = new Challenge
                    {
                        Data = Base64Url.EncodeToString(TestIvBytes),
                        Address = "test-address"
                    },
                    Challenges =
                    [
                        new Challenge
                        {
                            Data = Base64Url.EncodeToString(TestChallengeBytes),
                            Address = "wallet-1"
                        }
                    ],
                    ContentType = "application/json",
                    ContentEncoding = "base64url"
                }
            ],
            Signature = Base64Url.EncodeToString(TestSignatureBytes)
        };
    }
}
