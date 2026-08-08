// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// The signed value must bind the WHOLE operation (FR-026 / R-013).
/// </summary>
/// <remarks>
/// <para>
/// v1 bound a hand-picked field list. <see cref="GovernanceOperation"/> carries more than that list,
/// so <c>ValidatorEntry</c>, <c>RosterSnapshotId</c>, <c>QuorumFormulaAtRaise</c> and
/// <c>ExpiresAt</c> were all unbound. The sharp case is <c>AddValidator</c>: an approval bound
/// "add a validator" and <b>not which one</b> — the validator's public key and endpoint sat outside
/// the digest entirely.
/// </para>
/// <para>
/// That was close to inert while the server both built and signed the operation, because there was
/// no separate party to mislead. Detached signing (R-014) is exactly what makes it exploitable: an
/// external party reviews something and signs it, so any unbound field is a way to display one thing
/// and enact another — leaving a cryptographically valid signature on the ledger and no record
/// anywhere of the substitution.
/// </para>
/// <para>
/// <b>This test is reflection-driven on purpose.</b> A hand-listed test rots exactly as the
/// hand-listed field list did: a property added to <see cref="GovernanceOperation"/> next year would
/// be silently uncovered, with no compiler error and no failing test. Enumerating the type means new
/// properties are covered the moment they exist. Same reasoning as the derivation-context reflection
/// tests in <c>Sorcha.Wallet.Contracts.Tests</c>.
/// </para>
/// </remarks>
public sealed class GovernanceApprovalStatementBindingTests
{
    /// <summary>
    /// Members deliberately outside the digest. Both are derived or mutable state ABOUT the proposal
    /// rather than part of what is being authorised: signatures accumulate as approvals arrive (so
    /// binding them would make the first signature invalidate the second), and status is the
    /// proposal's lifecycle, not its content.
    /// </summary>
    private static readonly HashSet<string> Excluded = new(StringComparer.Ordinal)
    {
        nameof(GovernanceOperation.ApprovalSignatures),
        nameof(GovernanceOperation.Status),
    };

    private const string RegisterId = "reg-1";
    private const string ApproverDid = "did:sorcha:w:ws11qapprover";

    private static GovernanceOperation Baseline() => new()
    {
        OperationType = GovernanceOperationType.AddValidator,
        ProposerDid = "did:sorcha:w:ws11qproposer",
        TargetDid = "did:sorcha:w:ws11qtarget",
        TargetRole = RegisterRole.Admin,
        ProposedAt = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero),
        ExpiresAt = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero),
        Justification = "baseline",
        RosterSnapshotId = "snapshot-A",
        QuorumFormulaAtRaise = QuorumFormula.StrictMajority,
        ValidatorEntry = new ValidatorRosterEntry
        {
            ValidatorId = "validator-A",
            PublicKey = "QUFBQQ==",
            DerivationContext = "sorcha:docket-signing",
        },
    };

    private static byte[] Digest(GovernanceOperation op)
        => GovernanceApprovalStatement.ComputeDigest(RegisterId, op, ApproverDid, isApproval: true);

    /// <summary>
    /// Every property that is part of what is being authorised must change the digest when it
    /// changes. Anything that does not is a field an approver's signature does not cover.
    /// </summary>
    [Theory]
    [MemberData(nameof(BindableProperties))]
    public void EveryBindableProperty_ChangesTheDigest(string propertyName)
    {
        var property = typeof(GovernanceOperation).GetProperty(propertyName)!;

        var baseline = Baseline();
        var mutated = Baseline();
        property.SetValue(mutated, MutatedValue(property, property.GetValue(baseline)));

        Digest(mutated).Should().NotEqual(
            Digest(baseline),
            "changing {0} changes what is being authorised, so a signature that does not cover it "
            + "lets the operation be altered after review", propertyName);
    }

    public static TheoryData<string> BindableProperties()
    {
        var data = new TheoryData<string>();
        foreach (var p in typeof(GovernanceOperation)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.CanWrite)
                     .Where(p => !Excluded.Contains(p.Name))
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            data.Add(p.Name);
        }

        return data;
    }

    /// <summary>Produces a value definitely different from <paramref name="current"/>.</summary>
    private static object? MutatedValue(PropertyInfo property, object? current)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (type == typeof(string)) return (current as string) == "mutated" ? "mutated-2" : "mutated";
        if (type == typeof(DateTimeOffset))
            return (current is DateTimeOffset d ? d : DateTimeOffset.UnixEpoch).AddDays(1);
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type).Cast<object>().ToList();
            return values.First(v => !Equals(v, current));
        }

        if (type == typeof(ValidatorRosterEntry))
        {
            // A genuinely DIFFERENT validator — different id AND different signing key. This is the
            // case that matters most: approving "add a validator" must not authorise adding an
            // arbitrary one. (A bare `new ValidatorRosterEntry()` is NOT a mutation here: the
            // baseline would have to be default too, and identical values must hash identically.)
            return new ValidatorRosterEntry
            {
                ValidatorId = "validator-B",
                PublicKey = "QkJCQg==",
                DerivationContext = "sorcha:docket-signing",
            };
        }

        throw new NotSupportedException(
            $"No mutation strategy for {property.Name} ({type.Name}). Add one — do not exclude the "
            + "property to make this pass, or the signature stops covering it.");
    }

    [Fact]
    public void TheExclusionsAreOnlyDerivedOrMutableState()
    {
        // Guards the escape hatch: excluding a property is how this test gets defeated, so the
        // exclusion list itself is pinned. Growing it is a deliberate act, not a quiet one.
        Excluded.Should().BeEquivalentTo(new[] { "ApprovalSignatures", "Status" });
    }

    [Fact]
    public void StatementIsV2_SoV1SignaturesCannotVerify()
    {
        // T073 / R-011 clean break. The domain tag is the first field, so a v1 statement and a v2
        // statement over the same proposal never produce the same bytes — a signature collected
        // under v1 rules (which did not bind ValidatorEntry) can never be replayed as a v2 approval.
        var statement = GovernanceApprovalStatement.BuildStatement(
            RegisterId, Baseline(), ApproverDid, isApproval: true);

        statement.Should().StartWith("sorcha:governance-approval:v2");
        statement.Should().NotContain("governance-approval:v1");
    }

    [Fact]
    public void OperationIsBoundWhole_NotByAFieldList()
    {
        // The property the design turns on: the digest covers the operation's serialisation, so a
        // property added to GovernanceOperation later is bound the moment it exists.
        var statement = GovernanceApprovalStatement.BuildStatement(
            RegisterId, Baseline(), ApproverDid, isApproval: true);

        statement.Should().Contain("validator-A", "the specific validator must be inside the digest");
        statement.Should().Contain("snapshot-A", "the roster snapshot must be inside the digest");
    }
}
