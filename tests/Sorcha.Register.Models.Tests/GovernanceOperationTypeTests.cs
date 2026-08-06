// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// Guards the wire contract of <see cref="GovernanceOperationType"/>.
/// </summary>
/// <remarks>
/// Written after very nearly shipping a collision: <c>CryptoPolicyUpdate</c> was first assigned
/// <c>6</c>, which <c>RevokeIssuanceKey</c> already held. C# permits duplicate enum values
/// <b>silently</b> — the solution built clean — and the two operations would then have compared
/// equal, matched the same switch arm, and round-tripped as each other. On a ledger, that means a
/// sealed transaction saying "revoke this organisation's issuance key" could be read back as
/// "change the register's crypto policy". Nothing downstream would notice.
/// </remarks>
public class GovernanceOperationTypeTests
{
    [Fact]
    public void EveryOperationTypeHasADistinctValue()
    {
        var values = Enum.GetValues<GovernanceOperationType>()
            .Cast<int>()
            .ToList();
        var names = Enum.GetNames<GovernanceOperationType>();

        // Enum.GetValues collapses duplicates, so compare against the NAME count to detect them.
        values.Distinct().Should().HaveCount(names.Length,
            "two governance operations sharing a numeric value compare equal and are "
            + "indistinguishable once sealed on the ledger");
    }

    [Theory]
    [InlineData(GovernanceOperationType.Add, 0)]
    [InlineData(GovernanceOperationType.Remove, 1)]
    [InlineData(GovernanceOperationType.Transfer, 2)]
    [InlineData(GovernanceOperationType.AddValidator, 3)]
    [InlineData(GovernanceOperationType.RemoveValidator, 4)]
    [InlineData(GovernanceOperationType.RotateValidatorKey, 5)]
    [InlineData(GovernanceOperationType.RevokeIssuanceKey, 6)]
    [InlineData(GovernanceOperationType.RotateIssuanceKey, 7)]
    [InlineData(GovernanceOperationType.CryptoPolicyUpdate, 8)]
    public void OperationTypeValuesArePinned(GovernanceOperationType type, int expected)
    {
        // These values are an on-ledger contract: sealed transactions carry them, and replicas on
        // other installations read them back. Renumbering reinterprets history.
        ((int)type).Should().Be(expected);
    }

    [Fact]
    public void CryptoPolicyUpdateIsDistinctFromTheIssuanceKeyOperations()
    {
        // The specific collision that was nearly shipped.
        GovernanceOperationType.CryptoPolicyUpdate.Should().NotBe(GovernanceOperationType.RevokeIssuanceKey);
        GovernanceOperationType.CryptoPolicyUpdate.Should().NotBe(GovernanceOperationType.RotateIssuanceKey);
    }
}
