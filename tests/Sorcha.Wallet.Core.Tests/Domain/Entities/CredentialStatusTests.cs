// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using FluentAssertions;

using Sorcha.Wallet.Core.Domain.Entities;

using Xunit;

namespace Sorcha.Wallet.Core.Tests.Domain.Entities;

/// <summary>
/// Feature 106 — covers the <see cref="CredentialStatus"/> enum additions
/// (<c>PendingAcceptance</c>, <c>Declined</c>) and JSON round-trip serialisation.
/// The full state-machine transition table is exercised by
/// <c>CredentialLifecycleTests</c> in <c>Sorcha.Wallet.Service.Tests</c>; this
/// suite keeps the portable layer honest against enum drift.
/// </summary>
public class CredentialStatusTests
{
    [Theory]
    [InlineData(CredentialStatus.Active, "Active")]
    [InlineData(CredentialStatus.Expired, "Expired")]
    [InlineData(CredentialStatus.Revoked, "Revoked")]
    [InlineData(CredentialStatus.Suspended, "Suspended")]
    [InlineData(CredentialStatus.PendingAcceptance, "PendingAcceptance")]
    [InlineData(CredentialStatus.Declined, "Declined")]
    [InlineData(CredentialStatus.Consumed, "Consumed")]
    public void Serialize_EmitsEnumName(CredentialStatus status, string expected)
    {
        var json = JsonSerializer.Serialize(status);
        json.Should().Be($"\"{expected}\"");
    }

    [Theory]
    [InlineData("\"PendingAcceptance\"", CredentialStatus.PendingAcceptance)]
    [InlineData("\"Declined\"", CredentialStatus.Declined)]
    [InlineData("\"Active\"", CredentialStatus.Active)]
    public void Deserialize_AcceptsEnumName(string json, CredentialStatus expected)
    {
        var parsed = JsonSerializer.Deserialize<CredentialStatus>(json);
        parsed.Should().Be(expected);
    }

    [Fact]
    public void CredentialEntity_StatusRoundTripsThroughJson()
    {
        var entity = new CredentialEntity
        {
            Id = "urn:uuid:test",
            Type = "VerifiedCitizenCredential",
            IssuerDid = "did:sorcha:issuer:gov",
            SubjectDid = "did:sorcha:subject:alice",
            ClaimsJson = "{}",
            RawToken = "eyJ.test.token",
            WalletAddress = "wallet-1",
            Status = CredentialStatus.PendingAcceptance,
        };

        var json = JsonSerializer.Serialize(entity);
        json.Should().Contain("\"Status\":\"PendingAcceptance\"");

        var roundTripped = JsonSerializer.Deserialize<CredentialEntity>(json);
        roundTripped.Should().NotBeNull();
        roundTripped!.Status.Should().Be(CredentialStatus.PendingAcceptance);
    }

    [Fact]
    public void PendingAcceptanceAndDeclined_HaveDistinctNumericValues()
    {
        // Persisted column is string via HasConversion<string>(), but the underlying
        // numeric ordering should never collide with existing values — that would be
        // a silent wire-compat break on any consumer still reading integers.
        ((int)CredentialStatus.PendingAcceptance).Should().Be(4);
        ((int)CredentialStatus.Declined).Should().Be(5);
        ((int)CredentialStatus.Consumed).Should().Be(6);
    }
}
