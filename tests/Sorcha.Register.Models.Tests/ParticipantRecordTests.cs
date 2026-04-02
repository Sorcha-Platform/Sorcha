// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;

namespace Sorcha.Register.Models.Tests;

public class ParticipantRecordTests
{
    #region ParticipantRecord Construction

    [Fact]
    public void ParticipantRecord_WithRequiredFields_CreatesSuccessfully()
    {
        var record = CreateValidRecord();

        record.ParticipantId.Should().NotBeNullOrWhiteSpace();
        record.OrganizationName.Should().Be("Acme Corp");
        record.ParticipantName.Should().Be("Alice");
        record.Status.Should().Be(ParticipantRecordStatus.Active);
        record.Version.Should().Be(1);
        record.Addresses.Should().HaveCount(1);
    }

    [Fact]
    public void ParticipantRecord_Metadata_DefaultsToNull()
    {
        var record = CreateValidRecord();

        record.Metadata.Should().BeNull();
    }

    [Fact]
    public void ParticipantRecord_WithMetadata_PreservesJsonElement()
    {
        var metaJson = """{"description":"Test participant","tier":"gold"}""";
        var metadata = JsonSerializer.Deserialize<JsonElement>(metaJson);

        var record = CreateValidRecord(metadata: metadata);

        record.Metadata.Should().NotBeNull();
        record.Metadata!.Value.GetProperty("description").GetString().Should().Be("Test participant");
        record.Metadata!.Value.GetProperty("tier").GetString().Should().Be("gold");
    }

    #endregion

    #region ParticipantAddress Construction

    [Fact]
    public void ParticipantAddress_Primary_DefaultsToFalse()
    {
        var address = new ParticipantAddress
        {
            WalletAddress = "addr1",
            PublicKey = Convert.ToBase64String(new byte[32]),
            Algorithm = "ED25519"
        };

        address.Primary.Should().BeFalse();
    }

    [Fact]
    public void ParticipantAddress_WithPrimaryTrue_PreservesFlag()
    {
        var address = new ParticipantAddress
        {
            WalletAddress = "addr1",
            PublicKey = Convert.ToBase64String(new byte[32]),
            Algorithm = "ED25519",
            Primary = true
        };

        address.Primary.Should().BeTrue();
    }

    #endregion

    #region JSON Serialization Round-Trip

    [Fact]
    public void ParticipantRecord_Serialization_RoundTrips()
    {
        var record = CreateValidRecord();

        var json = JsonSerializer.Serialize(record);
        var deserialized = JsonSerializer.Deserialize<ParticipantRecord>(json);

        deserialized.Should().NotBeNull();
        deserialized!.ParticipantId.Should().Be(record.ParticipantId);
        deserialized.OrganizationName.Should().Be(record.OrganizationName);
        deserialized.ParticipantName.Should().Be(record.ParticipantName);
        deserialized.Status.Should().Be(record.Status);
        deserialized.Version.Should().Be(record.Version);
        deserialized.Addresses.Should().HaveCount(record.Addresses.Count);
    }

    [Fact]
    public void ParticipantRecord_Serialization_UsesJsonPropertyNames()
    {
        var record = CreateValidRecord();

        var json = JsonSerializer.Serialize(record);

        json.Should().Contain("\"participantId\"");
        json.Should().Contain("\"organizationName\"");
        json.Should().Contain("\"participantName\"");
        json.Should().Contain("\"status\"");
        json.Should().Contain("\"version\"");
        json.Should().Contain("\"addresses\"");
    }

    [Fact]
    public void ParticipantRecord_Serialization_StatusAsString()
    {
        var record = CreateValidRecord(status: ParticipantRecordStatus.Deprecated);

        var json = JsonSerializer.Serialize(record);

        json.Should().Contain("\"Deprecated\"");
    }

    [Theory]
    [InlineData(ParticipantRecordStatus.Active, "Active")]
    [InlineData(ParticipantRecordStatus.Deprecated, "Deprecated")]
    [InlineData(ParticipantRecordStatus.Revoked, "Revoked")]
    public void ParticipantRecord_Serialization_AllStatusValues_RoundTrip(
        ParticipantRecordStatus status, string expectedString)
    {
        var record = CreateValidRecord(status: status);

        var json = JsonSerializer.Serialize(record);
        json.Should().Contain($"\"{expectedString}\"");

        var deserialized = JsonSerializer.Deserialize<ParticipantRecord>(json);
        deserialized!.Status.Should().Be(status);
    }

    [Fact]
    public void ParticipantAddress_Serialization_RoundTrips()
    {
        var address = new ParticipantAddress
        {
            WalletAddress = "1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2",
            PublicKey = Convert.ToBase64String(new byte[32]),
            Algorithm = "ED25519",
            Primary = true
        };

        var json = JsonSerializer.Serialize(address);
        var deserialized = JsonSerializer.Deserialize<ParticipantAddress>(json);

        deserialized.Should().NotBeNull();
        deserialized!.WalletAddress.Should().Be(address.WalletAddress);
        deserialized.PublicKey.Should().Be(address.PublicKey);
        deserialized.Algorithm.Should().Be(address.Algorithm);
        deserialized.Primary.Should().BeTrue();
    }

    [Fact]
    public void ParticipantAddress_Serialization_UsesJsonPropertyNames()
    {
        var address = new ParticipantAddress
        {
            WalletAddress = "addr1",
            PublicKey = "key1",
            Algorithm = "ED25519"
        };

        var json = JsonSerializer.Serialize(address);

        json.Should().Contain("\"walletAddress\"");
        json.Should().Contain("\"publicKey\"");
        json.Should().Contain("\"algorithm\"");
        json.Should().Contain("\"primary\"");
    }

    [Fact]
    public void ParticipantRecord_WithMultipleAddresses_RoundTrips()
    {
        var record = new ParticipantRecord
        {
            ParticipantId = Guid.NewGuid().ToString(),
            OrganizationName = "Acme Corp",
            ParticipantName = "Alice",
            Status = ParticipantRecordStatus.Active,
            Version = 1,
            Addresses =
            [
                new ParticipantAddress
                {
                    WalletAddress = "addr1",
                    PublicKey = Convert.ToBase64String(new byte[32]),
                    Algorithm = "ED25519",
                    Primary = true
                },
                new ParticipantAddress
                {
                    WalletAddress = "addr2",
                    PublicKey = Convert.ToBase64String(new byte[33]),
                    Algorithm = "P-256"
                },
                new ParticipantAddress
                {
                    WalletAddress = "addr3",
                    PublicKey = Convert.ToBase64String(new byte[512]),
                    Algorithm = "RSA-4096"
                }
            ]
        };

        var json = JsonSerializer.Serialize(record);
        var deserialized = JsonSerializer.Deserialize<ParticipantRecord>(json);

        deserialized!.Addresses.Should().HaveCount(3);
        deserialized.Addresses[0].Primary.Should().BeTrue();
        deserialized.Addresses[1].Primary.Should().BeFalse();
        deserialized.Addresses[2].Algorithm.Should().Be("RSA-4096");
    }

    [Fact]
    public void ParticipantRecord_WithNullMetadata_SerializesAsNull()
    {
        var record = CreateValidRecord();

        var json = JsonSerializer.Serialize(record);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("metadata", out var metaProp).Should().BeTrue();
        metaProp.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void ParticipantRecord_WithMetadata_SerializesMetadata()
    {
        var metaJson = """{"key":"value"}""";
        var metadata = JsonSerializer.Deserialize<JsonElement>(metaJson);
        var record = CreateValidRecord(metadata: metadata);

        var json = JsonSerializer.Serialize(record);
        var deserialized = JsonSerializer.Deserialize<ParticipantRecord>(json);

        deserialized!.Metadata.Should().NotBeNull();
        deserialized.Metadata!.Value.GetProperty("key").GetString().Should().Be("value");
    }

    #endregion

    #region ParticipantRecordStatus Enum

    [Fact]
    public void ParticipantRecordStatus_HasExpectedValues()
    {
        Enum.GetValues<ParticipantRecordStatus>().Should().HaveCount(3);
        ((int)ParticipantRecordStatus.Active).Should().Be(0);
        ((int)ParticipantRecordStatus.Deprecated).Should().Be(1);
        ((int)ParticipantRecordStatus.Revoked).Should().Be(2);
    }

    #endregion

    #region TransactionType Enum - Participant Value

    [Fact]
    public void TransactionType_Participant_HasValue3()
    {
        ((int)TransactionType.Participant).Should().Be(3);
    }

    [Fact]
    public void TransactionType_HasFiveValues()
    {
        Enum.GetValues<TransactionType>().Should().HaveCount(5);
    }

    #endregion

    #region Helper Methods

    private static ParticipantRecord CreateValidRecord(
        ParticipantRecordStatus status = ParticipantRecordStatus.Active,
        JsonElement? metadata = null)
    {
        return new ParticipantRecord
        {
            ParticipantId = Guid.NewGuid().ToString(),
            OrganizationName = "Acme Corp",
            ParticipantName = "Alice",
            Status = status,
            Version = 1,
            Addresses =
            [
                new ParticipantAddress
                {
                    WalletAddress = "1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2",
                    PublicKey = Convert.ToBase64String(new byte[32]),
                    Algorithm = "ED25519",
                    Primary = true
                }
            ],
            Metadata = metadata
        };
    }

    #endregion
}
