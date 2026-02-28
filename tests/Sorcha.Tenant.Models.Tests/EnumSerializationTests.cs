// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using FluentAssertions;

using Sorcha.Tenant.Models;

using Xunit;

namespace Sorcha.Tenant.Models.Tests;

public class EnumSerializationTests
{
    // --- ChallengeStatus ---

    [Theory]
    [InlineData(ChallengeStatus.Pending, 0)]
    [InlineData(ChallengeStatus.Completed, 1)]
    [InlineData(ChallengeStatus.Expired, 2)]
    [InlineData(ChallengeStatus.Failed, 3)]
    public void ChallengeStatus_HasCorrectNumericValues(ChallengeStatus status, int expectedValue)
    {
        ((int)status).Should().Be(expectedValue);
    }

    [Theory]
    [InlineData(ChallengeStatus.Pending, "\"Pending\"")]
    [InlineData(ChallengeStatus.Completed, "\"Completed\"")]
    [InlineData(ChallengeStatus.Expired, "\"Expired\"")]
    [InlineData(ChallengeStatus.Failed, "\"Failed\"")]
    public void ChallengeStatus_SerializesToString(ChallengeStatus status, string expectedJson)
    {
        var json = JsonSerializer.Serialize(status);
        json.Should().Be(expectedJson);
    }

    [Theory]
    [InlineData("\"Pending\"", ChallengeStatus.Pending)]
    [InlineData("\"Completed\"", ChallengeStatus.Completed)]
    [InlineData("\"Expired\"", ChallengeStatus.Expired)]
    [InlineData("\"Failed\"", ChallengeStatus.Failed)]
    public void ChallengeStatus_DeserializesFromString(string json, ChallengeStatus expected)
    {
        var result = JsonSerializer.Deserialize<ChallengeStatus>(json);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(ChallengeStatus.Pending)]
    [InlineData(ChallengeStatus.Completed)]
    [InlineData(ChallengeStatus.Expired)]
    [InlineData(ChallengeStatus.Failed)]
    public void ChallengeStatus_RoundTrip_PreservesValue(ChallengeStatus original)
    {
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<ChallengeStatus>(json);
        deserialized.Should().Be(original);
    }

    // --- ParticipantIdentityStatus ---

    [Theory]
    [InlineData(ParticipantIdentityStatus.Active, 0)]
    [InlineData(ParticipantIdentityStatus.Inactive, 1)]
    [InlineData(ParticipantIdentityStatus.Suspended, 2)]
    public void ParticipantIdentityStatus_HasCorrectNumericValues(ParticipantIdentityStatus status, int expectedValue)
    {
        ((int)status).Should().Be(expectedValue);
    }

    [Theory]
    [InlineData(ParticipantIdentityStatus.Active, "\"Active\"")]
    [InlineData(ParticipantIdentityStatus.Inactive, "\"Inactive\"")]
    [InlineData(ParticipantIdentityStatus.Suspended, "\"Suspended\"")]
    public void ParticipantIdentityStatus_SerializesToString(ParticipantIdentityStatus status, string expectedJson)
    {
        var json = JsonSerializer.Serialize(status);
        json.Should().Be(expectedJson);
    }

    [Theory]
    [InlineData("\"Active\"", ParticipantIdentityStatus.Active)]
    [InlineData("\"Inactive\"", ParticipantIdentityStatus.Inactive)]
    [InlineData("\"Suspended\"", ParticipantIdentityStatus.Suspended)]
    public void ParticipantIdentityStatus_DeserializesFromString(string json, ParticipantIdentityStatus expected)
    {
        var result = JsonSerializer.Deserialize<ParticipantIdentityStatus>(json);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(ParticipantIdentityStatus.Active)]
    [InlineData(ParticipantIdentityStatus.Inactive)]
    [InlineData(ParticipantIdentityStatus.Suspended)]
    public void ParticipantIdentityStatus_RoundTrip_PreservesValue(ParticipantIdentityStatus original)
    {
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<ParticipantIdentityStatus>(json);
        deserialized.Should().Be(original);
    }

    // --- WalletLinkStatus ---

    [Theory]
    [InlineData(WalletLinkStatus.Active, 0)]
    [InlineData(WalletLinkStatus.Revoked, 1)]
    public void WalletLinkStatus_HasCorrectNumericValues(WalletLinkStatus status, int expectedValue)
    {
        ((int)status).Should().Be(expectedValue);
    }

    [Theory]
    [InlineData(WalletLinkStatus.Active, "\"Active\"")]
    [InlineData(WalletLinkStatus.Revoked, "\"Revoked\"")]
    public void WalletLinkStatus_SerializesToString(WalletLinkStatus status, string expectedJson)
    {
        var json = JsonSerializer.Serialize(status);
        json.Should().Be(expectedJson);
    }

    [Theory]
    [InlineData("\"Active\"", WalletLinkStatus.Active)]
    [InlineData("\"Revoked\"", WalletLinkStatus.Revoked)]
    public void WalletLinkStatus_DeserializesFromString(string json, WalletLinkStatus expected)
    {
        var result = JsonSerializer.Deserialize<WalletLinkStatus>(json);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(WalletLinkStatus.Active)]
    [InlineData(WalletLinkStatus.Revoked)]
    public void WalletLinkStatus_RoundTrip_PreservesValue(WalletLinkStatus original)
    {
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<WalletLinkStatus>(json);
        deserialized.Should().Be(original);
    }
}
