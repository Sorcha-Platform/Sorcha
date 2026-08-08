// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// Withdrawing a delegation (R-023).
/// </summary>
/// <remarks>
/// Revocation is deliberately unilateral rather than quorum-gated: routing it through the proposal
/// path would leave a compromised autonomous approver live while votes were collected, which inverts
/// the point of having revocation at all.
/// </remarks>
public sealed class GovernanceDelegationRevocationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static GovernanceDelegationRevocation Revocation() => new()
    {
        DelegationId = "delegation-1",
        OrganisationDid = "did:sorcha:w:ws11qorg",
        IndividualDid = "did:sorcha:w:ws11qperson",
        RevokedAt = Now,
        Reason = "bot decommissioned",
    };

    private static GovernanceDelegation Grant() => new()
    {
        DelegationId = "delegation-1",
        OrganisationDid = "did:sorcha:w:ws11qorg",
        IndividualDid = "did:sorcha:w:ws11qperson",
        ApproverPublicKey = "Qk9US0VZ",
        Scope = [GovernanceOperationType.CryptoPolicyUpdate],
        GrantedAt = Now.AddDays(-1),
        ExpiresAt = Now.AddDays(30),
    };

    [Fact]
    public void GrantAndRevocation_CannotBeReplayedAsOneAnother()
    {
        // Distinct domain tags. Without them a signature collected to CREATE authority could be
        // replayed to destroy it, or the reverse — and both are signed by the same person's key.
        GovernanceDelegationRevocationStatement.BuildStatement(Revocation())
            .Should().StartWith("sorcha:governance-delegation-revocation:v1");

        GovernanceDelegationStatement.BuildStatement(Grant())
            .Should().StartWith("sorcha:governance-delegation:v1");

        GovernanceDelegationRevocationStatement.ComputeDigest(Revocation())
            .Should().NotEqual(GovernanceDelegationStatement.ComputeDigest(Grant()));
    }

    [Fact]
    public void RevokingADifferentDelegation_ChangesTheDigest()
    {
        var other = Revocation();
        other.DelegationId = "delegation-2";

        GovernanceDelegationRevocationStatement.ComputeDigest(other)
            .Should().NotEqual(GovernanceDelegationRevocationStatement.ComputeDigest(Revocation()),
                "a signature to withdraw one grant must not withdraw another");
    }

    [Theory]
    [MemberData(nameof(AllProperties))]
    public void EveryProperty_IsBound(string propertyName)
    {
        // Same reflection-driven approach as the approval statement, for the same reason: a property
        // added later must be covered automatically rather than depending on someone remembering.
        var property = typeof(GovernanceDelegationRevocation).GetProperty(propertyName)!;

        var mutated = Revocation();
        var current = property.GetValue(mutated);
        property.SetValue(mutated, Mutate(property, current));

        GovernanceDelegationRevocationStatement.ComputeDigest(mutated)
            .Should().NotEqual(GovernanceDelegationRevocationStatement.ComputeDigest(Revocation()),
                "{0} is part of what is being signed", propertyName);
    }

    public static TheoryData<string> AllProperties()
    {
        var data = new TheoryData<string>();
        foreach (var p in typeof(GovernanceDelegationRevocation)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.CanWrite)
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            data.Add(p.Name);
        }

        return data;
    }

    private static object? Mutate(PropertyInfo property, object? current)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (type == typeof(string)) return (current as string) == "mutated" ? "mutated-2" : "mutated";
        if (type == typeof(DateTimeOffset))
            return (current is DateTimeOffset d ? d : DateTimeOffset.UnixEpoch).AddDays(1);

        throw new NotSupportedException(
            $"No mutation strategy for {property.Name} ({type.Name}). Add one rather than excluding "
            + "the property, or the signature stops covering it.");
    }

    [Fact]
    public void StatementIsDeterministic()
    {
        // Two nodes must agree on the bytes, or a revocation verifies on one and not another.
        GovernanceDelegationRevocationStatement.BuildStatement(Revocation())
            .Should().Be(GovernanceDelegationRevocationStatement.BuildStatement(Revocation()));
    }
}
