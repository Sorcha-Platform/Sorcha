// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// What an approver is asked to sign, and that they can derive the digest the server will check
/// it against (T077 / FR-028).
/// </summary>
/// <remarks>
/// <para>
/// <b>The signing request deliberately carries no digest.</b> A server-supplied one could fail to
/// match the operation the client displayed, reinstating at the transport layer exactly the
/// substitution that statement v2 closes inside the digest. So the client derives it from the
/// operation it rendered — which only works if the operation survives the wire intact.
/// </para>
/// <para>
/// <b>That is the seam this file exists for, and it fails silently.</b> The server serialises with
/// one serialiser and the client deserialises with another; a digest-bound property that does not
/// survive the round trip leaves the client computing a digest over a subtly different operation.
/// Nothing throws. The signature verifies against nothing, the approval is refused as invalid, and
/// the message blames the approver rather than the field that was dropped in transit.
/// </para>
/// </remarks>
public sealed class GovernanceSigningRequestContractTests
{
    private const string RegisterId = "cbb1fa4c1bc942b7a1f86eabcfb96ea6";
    private const string ApproverDid = "did:sorcha:w:ws11qapprover";

    /// <summary>
    /// The options the endpoint ACTUALLY serialises with.
    /// </summary>
    /// <remarks>
    /// The Register Service calls neither <c>ConfigureHttpJsonOptions</c> nor <c>AddJsonOptions</c>,
    /// so its minimal APIs use the web defaults. Asserting against <c>SorchaJson.Options</c> — which
    /// this service does not use — is what let an earlier contract test pass while the endpoint
    /// emitted something no client could read.
    /// </remarks>
    private static readonly JsonSerializerOptions HostOptions = JsonSerializerOptions.Web;

    /// <summary>
    /// Stands in for the client. Refit's default content serialiser is web-shaped, and the CLI
    /// registers no <c>RefitSettings</c> of its own, so this is the pairing that runs in production.
    /// </summary>
    private static readonly JsonSerializerOptions ClientOptions = JsonSerializerOptions.Web;

    /// <summary>
    /// Excluded from the digest by <see cref="GovernanceApprovalStatement"/>: both are state
    /// <i>about</i> the proposal rather than part of what is being authorised. Read from the
    /// production type rather than restated, so this cannot drift from what is actually bound.
    /// </summary>
    private static readonly string[] NotBound =
    [
        nameof(GovernanceOperation.ApprovalSignatures),
        nameof(GovernanceOperation.Status),
    ];

    /// <summary>
    /// Every digest-bound property populated, and none left at its default — otherwise the
    /// round-trip below would be comparing two operations that agree only by being empty.
    /// </summary>
    private static GovernanceOperation StoredOperation() => new()
    {
        // AddValidator deliberately: it is the operation whose payload rides in ValidatorEntry, the
        // nested object most likely to be lost in transit and the one v1 failed to bind at all.
        OperationType = GovernanceOperationType.AddValidator,
        ProposerDid = "did:sorcha:w:ws11qproposer",
        TargetDid = "did:sorcha:w:ws11qtarget",
        TargetRole = RegisterRole.Admin,
        ProposedAt = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero),
        ExpiresAt = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero),
        Justification = "T077 — the whole operation must reach the approver",
        RosterSnapshotId = "genesis-tx",
        QuorumFormulaAtRaise = QuorumFormula.Unanimous,
        ValidatorEntry = new ValidatorRosterEntry
        {
            ValidatorId = "validator-A",
            PublicKey = "QUFBQQ==",
            DerivationContext = "sorcha:docket-signing",
        },
    };

    /// <summary>The request exactly as the endpoint composes it.</summary>
    private static GovernanceSigningRequest ServerResponse(GovernanceOperation operation) => new()
    {
        RequestId = "proposal-tx",
        RegisterId = RegisterId,
        Operation = operation,
        StatementVersion = GovernanceApprovalStatement.StatementVersion,
        ApproverDid = ApproverDid,
        ExpiresAt = operation.ExpiresAt,
    };

    /// <summary>Server serialises, client deserialises — the pairing that runs in production.</summary>
    private static GovernanceSigningRequest OverTheWire(GovernanceSigningRequest response)
        => JsonSerializer.Deserialize<GovernanceSigningRequest>(
            JsonSerializer.Serialize(response, HostOptions), ClientOptions)!;

    private static byte[] Digest(string registerId, GovernanceOperation op, string approverDid, bool isApproval)
        => GovernanceApprovalStatement.ComputeDigest(registerId, op, approverDid, isApproval);

    private static IEnumerable<string> PropertyNames(JsonElement element, string prefix = "")
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return prefix + property.Name;
                    foreach (var nested in PropertyNames(property.Value, prefix + property.Name + "."))
                    {
                        yield return nested;
                    }
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in PropertyNames(item, prefix))
                    {
                        yield return nested;
                    }
                }
                break;
        }
    }

    // ---- claim 1: the full operation, and no digest ------------------------------------------

    /// <summary>
    /// FR-028. A digest on the wire is a value the approver cannot check against what they read.
    /// </summary>
    [Fact]
    public void TheSigningRequest_CarriesNoDigest_AtAnyDepth()
    {
        var json = JsonSerializer.Serialize(ServerResponse(StoredOperation()), HostOptions);

        using var document = JsonDocument.Parse(json);
        var offending = PropertyNames(document.RootElement)
            .Where(name => name.Contains("digest", StringComparison.OrdinalIgnoreCase))
            .ToList();

        offending.Should().BeEmpty(
            "a server-supplied digest could disagree with the operation the approver actually read, "
            + "which is the substitution statement v2 closes inside the digest");
    }

    /// <summary>
    /// Every property the signature binds must arrive. A field lost in transit is one the approver
    /// never saw and cannot have meant to authorise.
    /// </summary>
    [Fact]
    public void TheSigningRequest_CarriesEveryPropertyTheSignatureBinds()
    {
        var stored = StoredOperation();
        var received = OverTheWire(ServerResponse(stored)).Operation;

        var bound = typeof(GovernanceOperation)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !NotBound.Contains(p.Name))
            .ToList();

        bound.Should().NotBeEmpty("the reflection must actually find properties");

        foreach (var property in bound)
        {
            var sent = property.GetValue(stored);
            var arrived = property.GetValue(received);

            // The fixture must exercise the property, or its round trip proves nothing.
            sent.Should().NotBeNull($"{property.Name} must be populated for this test to mean anything");
            if (sent is string s) s.Should().NotBeEmpty($"{property.Name} must be populated");

            arrived.Should().NotBeNull($"{property.Name} did not survive the wire");
            JsonSerializer.Serialize(arrived).Should().Be(JsonSerializer.Serialize(sent),
                $"{property.Name} arrived different from how it was sent");
        }
    }

    // ---- claim 2: the client can derive what the server will check ----------------------------

    /// <summary>
    /// T077's load-bearing claim: the digest a client derives from the request it received equals
    /// the digest the server rebuilds from the stored operation and verifies the signature against.
    /// </summary>
    /// <remarks>
    /// The server never takes the digest from the client — <c>GovernanceAuthorisationValidator</c>
    /// and <c>GovernanceApprovalTally</c> both rebuild it from the <b>stored</b> operation. So the
    /// two agree only if the operation survived the wire byte-for-byte in every bound field. If it
    /// did not, the signature simply fails to verify and the refusal names the approver rather than
    /// the transport.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AClientDerivedDigest_MatchesWhatTheServerVerifiesAgainst(bool isApproval)
    {
        var stored = StoredOperation();

        // What the Validator and the approve endpoint rebuild and check against.
        var serverSide = Digest(RegisterId, stored, ApproverDid, isApproval);

        // What the approver's client computes, from the request as it actually arrives.
        var received = OverTheWire(ServerResponse(stored));
        var clientSide = Digest(received.RegisterId, received.Operation, received.ApproverDid, isApproval);

        clientSide.Should().Equal(serverSide,
            "the approver signs what they were shown, and the server checks what it stored — if the "
            + "wire loses a bound field those are different statements and every approval is refused");
    }

    /// <summary>
    /// The request must carry the register and approver the digest is scoped to, not leave the
    /// client to supply them from elsewhere.
    /// </summary>
    [Fact]
    public void TheSigningRequest_CarriesTheDigestsOwnScope()
    {
        var received = OverTheWire(ServerResponse(StoredOperation()));

        received.RegisterId.Should().Be(RegisterId, "the digest is per-register (T033's sibling claim)");
        received.ApproverDid.Should().Be(ApproverDid, "the digest names the approving organisation");
        received.StatementVersion.Should().Be(GovernanceApprovalStatement.StatementVersion,
            "the client must build the same statement version the server verifies");
    }

    /// <summary>
    /// The comparison above must be sensitive to the operation, or it is comparing two constants.
    /// </summary>
    /// <remarks>
    /// Without this, the equality test would pass just as well if the digest ignored the operation
    /// entirely — which is precisely the v1 defect, where <c>ValidatorEntry</c> sat outside the
    /// digest and an approval bound "add a validator" without binding <b>which one</b>.
    /// </remarks>
    [Fact]
    public void ADigestDerivedOverTheWire_StillBindsTheOperation()
    {
        var received = OverTheWire(ServerResponse(StoredOperation()));
        var asReceived = Digest(RegisterId, received.Operation, ApproverDid, isApproval: true);

        var tampered = OverTheWire(ServerResponse(StoredOperation()));
        tampered.Operation.ValidatorEntry!.PublicKey = "QkJCQg==";   // a different validator key

        Digest(RegisterId, tampered.Operation, ApproverDid, isApproval: true)
            .Should().NotEqual(asReceived,
                "approving one validator's key must not authorise another's — the round trip must "
                + "preserve the binding, not just the bytes");
    }

    /// <summary>
    /// Approve and reject are different statements, over the wire as much as in the leaf.
    /// </summary>
    [Fact]
    public void ADigestDerivedOverTheWire_StillDistinguishesApproveFromReject()
    {
        var received = OverTheWire(ServerResponse(StoredOperation()));

        Digest(RegisterId, received.Operation, ApproverDid, isApproval: true)
            .Should().NotEqual(Digest(RegisterId, received.Operation, ApproverDid, isApproval: false),
                "a rejection must never be reusable as an approval");
    }
}
