// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Sorcha.Register.Models;
using Sorcha.Register.Storage.MongoDB;
using Xunit;

namespace Sorcha.Register.Storage.MongoDB.Tests.Serialization;

/// <summary>
/// A receipt must read back from the document MongoDB actually stores — which carries an `_id` the
/// model does not declare. Found live on n1: once receipts were really written (#1704), every
/// verification-bundle export failed with "Element '_id' does not match any field or property".
/// </summary>
public class TransactionReceiptBsonTests
{
    private static void RegisterRepositoryClassMaps() =>
        typeof(MongoRegisterRepository)
            .GetMethod("RegisterBsonClassMaps", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null);

    private static TransactionReceipt Receipt() => new()
    {
        ReceiptId = "r1",
        TransactionId = "tx1",
        RegisterId = "0123456789abcdef0123456789abcdef",
        DocketNumber = 6,
        MerkleRoot = "root",
        InclusionProof = new MerkleInclusionProof
        {
            TransactionHash = "h", MerkleRoot = "root", DocketNumber = 6, ProofPath = [], LeafIndex = 0, TreeSize = 1,
        },
        Signatures = [],
        SealedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void AStoredReceipt_WithTheIdMongoAdds_ReadsBack()
    {
        RegisterRepositoryClassMaps();

        var stored = Receipt().ToBsonDocument();
        stored.InsertAt(0, new BsonElement("_id", ObjectId.GenerateNewId()));

        var read = BsonSerializer.Deserialize<TransactionReceipt>(stored);

        read.TransactionId.Should().Be("tx1");
        read.DocketNumber.Should().Be(6);
    }
}
