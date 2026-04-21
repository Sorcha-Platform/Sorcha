// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Storage.MongoDB.Serialization;
using Xunit;

namespace Sorcha.Register.Storage.MongoDB.Tests.Serialization;

public class RegisterSyncStateBsonSerializerTests
{
    private static RegisterSyncState? RoundTrip(BsonValue persisted)
    {
        var doc = new BsonDocument("syncState", persisted);
        using var reader = new BsonDocumentReader(doc);
        reader.ReadStartDocument();
        reader.ReadName();

        var serializer = new RegisterSyncStateBsonSerializer();
        var ctx = BsonDeserializationContext.CreateRoot(reader);
        var value = serializer.Deserialize(ctx, new BsonDeserializationArgs());

        reader.ReadEndDocument();
        return value;
    }

    [Theory]
    [InlineData("Indeterminate", RegisterSyncState.Indeterminate)]
    [InlineData("Syncing",       RegisterSyncState.Syncing)]
    [InlineData("CaughtUp",      RegisterSyncState.CaughtUp)]
    [InlineData("Error",         RegisterSyncState.Error)]
    public void Deserialize_NewEnumNames_ReturnsEnum(string name, RegisterSyncState expected)
    {
        RoundTrip(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("Subscribing", RegisterSyncState.Syncing)]
    [InlineData("Syncing",     RegisterSyncState.Syncing)]
    [InlineData("Synced",      RegisterSyncState.CaughtUp)]
    [InlineData("Error",       RegisterSyncState.Error)]
    public void Deserialize_LegacyStrings_MapsToEnum(string legacy, RegisterSyncState expected)
    {
        RoundTrip(legacy).Should().Be(expected);
    }

    [Fact]
    public void Deserialize_Null_ReturnsNull()
    {
        RoundTrip(BsonNull.Value).Should().BeNull();
    }

    [Fact]
    public void Deserialize_UnknownString_ReturnsNullWithoutThrowing()
    {
        // The real serializer logs a warning and returns null. Absence of exception is the
        // contract: unknown strings from old or foreign writers must not crash the read path.
        RoundTrip("SomethingFromTheFuture").Should().BeNull();
    }

    [Fact]
    public void Serialize_Enum_WritesEnumNameString()
    {
        var serializer = new RegisterSyncStateBsonSerializer();
        var doc = new BsonDocument();
        using (var writer = new BsonDocumentWriter(doc))
        {
            writer.WriteStartDocument();
            writer.WriteName("syncState");
            var ctx = BsonSerializationContext.CreateRoot(writer);
            serializer.Serialize(ctx, new BsonSerializationArgs(), RegisterSyncState.CaughtUp);
            writer.WriteEndDocument();
        }

        doc["syncState"].IsString.Should().BeTrue();
        doc["syncState"].AsString.Should().Be("CaughtUp");
    }

    [Fact]
    public void Serialize_Null_WritesBsonNull()
    {
        var serializer = new RegisterSyncStateBsonSerializer();
        var doc = new BsonDocument();
        using (var writer = new BsonDocumentWriter(doc))
        {
            writer.WriteStartDocument();
            writer.WriteName("syncState");
            var ctx = BsonSerializationContext.CreateRoot(writer);
            serializer.Serialize(ctx, new BsonSerializationArgs(), null);
            writer.WriteEndDocument();
        }

        doc["syncState"].IsBsonNull.Should().BeTrue();
    }
}
