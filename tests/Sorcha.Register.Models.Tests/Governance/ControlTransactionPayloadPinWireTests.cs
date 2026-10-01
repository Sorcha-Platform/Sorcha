// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Models.Tests.Governance;

/// <summary>
/// The governance-definition pin (Feature 197, T010) is carried on <see cref="ControlTransactionPayload"/>
/// but must not move the canonical bytes of any payload that does not carry one.
/// </summary>
/// <remarks>
/// The expected strings below were captured from the serialisation BEFORE the property was added,
/// so a green run proves the addition is invisible to the genesis, enactment and SSR-genesis
/// producers whose bytes are hashed and signed. The property is omitted when null — not written as
/// <c>null</c> — which is the whole reason these tests exist: <c>CanonicalJsonOptions</c> writes
/// nulls explicitly, so a plain nullable property would append <c>"governanceDefinitionTxId":null</c>
/// to every payload ever produced.
/// </remarks>
public sealed class ControlTransactionPayloadPinWireTests
{
    private const string Pin = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static RegisterControlRecord Roster() => new()
    {
        RegisterId = "00112233445566778899aabbccddeeff",
        Name = "Pin wire register",
        Description = null,
        CreatedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
    };

    private static GovernanceOperation Operation() => new()
    {
        OperationType = GovernanceOperationType.AddValidator,
        ProposerDid = "did:sorcha:w:ws11qproposer",
        TargetDid = "did:sorcha:w:ws11qtarget",
        TargetRole = RegisterRole.Admin,
        ProposedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
        ExpiresAt = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero),
        Status = ProposalStatus.Approved,
    };

    private static string Serialise(ControlTransactionPayload p) =>
        JsonSerializer.Serialize(p, ControlTransactionPayload.CanonicalJsonOptions);

    private const string GenesisExpected = """
        {"version":1,"roster":{"registerId":"00112233445566778899aabbccddeeff","name":"Pin wire register","description":null,"createdAt":"2026-10-01T12:00:00+00:00","attestations":[],"metadata":null},"operation":null,"enactsProposalId":null}
        """;
    private const string EnactmentExpected = """
        {"version":1,"roster":{"registerId":"00112233445566778899aabbccddeeff","name":"Pin wire register","description":null,"createdAt":"2026-10-01T12:00:00+00:00","attestations":[],"metadata":null},"operation":{"operationType":"AddValidator","proposerDid":"did:sorcha:w:ws11qproposer","targetDid":"did:sorcha:w:ws11qtarget","targetRole":"Admin","approvalSignatures":[],"proposedAt":"2026-10-01T12:00:00+00:00","expiresAt":"2026-10-08T12:00:00+00:00","status":"Approved","justification":null},"enactsProposalId":"aa11bb22cc33dd44ee55ff6600112233aa11bb22cc33dd44ee55ff6600112233"}
        """;

    [Fact]
    public void Serialise_GenesisShapedPayload_IsByteIdenticalToPreChangeBytes()
    {
        var json = Serialise(new ControlTransactionPayload { Version = 1, Roster = Roster() });

        json.Should().Be(GenesisExpected);
        json.Should().NotContain("governanceDefinitionTxId");
    }

    [Fact]
    public void Serialise_EnactmentShapedPayload_IsByteIdenticalToPreChangeBytes()
    {
        var json = Serialise(new ControlTransactionPayload
        {
            Version = 1,
            Roster = Roster(),
            Operation = Operation(),
            EnactsProposalId = "aa11bb22cc33dd44ee55ff6600112233aa11bb22cc33dd44ee55ff6600112233",
        });

        json.Should().Be(EnactmentExpected);
        json.Should().NotContain("governanceDefinitionTxId");
    }

    [Fact]
    public void Serialise_ProposalWithPin_RoundTripsAndWritesPinLast()
    {
        var proposal = new ControlTransactionPayload
        {
            Version = 1,
            Operation = Operation(),
            GovernanceDefinitionTxId = Pin,
        };

        var json = Serialise(proposal);
        var back = JsonSerializer.Deserialize<ControlTransactionPayload>(
            json, ControlTransactionPayload.CanonicalJsonOptions);

        json.Should().EndWith($",\"governanceDefinitionTxId\":\"{Pin}\"}}");
        back!.GovernanceDefinitionTxId.Should().Be(Pin);
        back.Operation.Should().NotBeNull();
        back.EnactsProposalId.Should().BeNull();
    }
}
