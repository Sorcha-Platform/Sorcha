// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Services;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// Feature 189 US2 — producing a governance approval that the Validator will actually accept.
/// </summary>
/// <remarks>
/// US2-A made the Validator verify approval signatures, which closed a hole where quorum could be
/// satisfied by asserting approvals. It also meant nothing could produce a <i>valid</i> one, so
/// every quorum-requiring operation became unsatisfiable until this service existed. These tests
/// pin the join: the digest signed here must be the digest verified there.
/// </remarks>
public class GovernanceApprovalServiceTests
{
    private const string RegisterId = "abc123def456abc123def456abc123de";
    private const string OrgA = "did:sorcha:w:ws11qorgA";
    private const string OrgB = "did:sorcha:w:ws11qorgB";

    private readonly Mock<IGovernanceSigningService> _signingMock = new();
    private readonly GovernanceApprovalService _sut;

    private byte[]? _capturedDigest;
    private string? _capturedPreferredSubject;

    public GovernanceApprovalServiceTests()
    {
        _signingMock
            .Setup(x => x.SignDigestAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, byte[] digest, string? subject, CancellationToken _) =>
            {
                _capturedDigest = digest;
                _capturedPreferredSubject = subject;
            })
            .ReturnsAsync(new GovernanceSignResult
            {
                Signature = [9, 9, 9, 9],
                PublicKey = [8, 8, 8, 8],
                Algorithm = "ED25519",
                WalletAddress = "ws11qorgA",
                Subject = OrgA
            });

        _sut = new GovernanceApprovalService(
            _signingMock.Object, Mock.Of<ILogger<GovernanceApprovalService>>());
    }

    private static GovernanceOperation Operation() => new()
    {
        OperationType = GovernanceOperationType.Add,
        ProposerDid = OrgB,
        TargetDid = "did:sorcha:w:ws11qnew",
        TargetRole = RegisterRole.Admin,
        ProposedAt = DateTimeOffset.Parse("2026-08-07T10:00:00Z"),
        ExpiresAt = DateTimeOffset.Parse("2026-08-14T10:00:00Z")
    };

    [Fact]
    public async Task SignsExactlyTheDigestTheValidatorVerifies()
    {
        await _sut.CreateApprovalAsync(RegisterId, Operation(), OrgA, isApproval: true);

        // The producer and the verifier must derive identical bytes. If they ever diverge, every
        // approval is rejected and the message says only "signature invalid".
        var expected = GovernanceApprovalStatement.ComputeDigest(
            RegisterId, Operation(), OrgA, isApproval: true);

        _capturedDigest.Should().Equal(expected);
    }

    [Fact]
    public async Task SignsAsTheApprovingOrganisation_NotTheOwner()
    {
        await _sut.CreateApprovalAsync(RegisterId, Operation(), OrgA, isApproval: true);

        // Without pinning the subject, the Owner would sign every approval — so a three-member
        // consortium would produce three identical Owner signatures and quorum would be satisfied
        // by one party voting three times.
        _capturedPreferredSubject.Should().Be(OrgA);
    }

    [Fact]
    public async Task ApprovalAndRejection_ProduceDifferentDigests()
    {
        await _sut.CreateApprovalAsync(RegisterId, Operation(), OrgA, isApproval: true);
        var approveDigest = _capturedDigest!.ToArray();

        await _sut.CreateApprovalAsync(RegisterId, Operation(), OrgA, isApproval: false);
        var rejectDigest = _capturedDigest!.ToArray();

        // If isApproval were outside the signed statement, a rejection could be recounted as an
        // approval by flipping a boolean the signature does not cover.
        rejectDigest.Should().NotEqual(approveDigest);
    }

    [Fact]
    public async Task RecordsTheAuthenticationMethod_ButNoProof()
    {
        var approval = await _sut.CreateApprovalAsync(
            RegisterId, Operation(), OrgA, isApproval: true, authMethod: "passkey");

        // The ledger records HOW the human authenticated so an auditor can tell a phishing-resistant
        // approval from a password one — and nothing more, because this record is immutable,
        // replicated to every node, and readable forever.
        approval.AuthMethod.Should().Be("passkey");
        approval.ApproverDid.Should().Be(OrgA);
        approval.IsApproval.Should().BeTrue();
        approval.Signature.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ApprovalsForDifferentTargetRoles_AreNotInterchangeable()
    {
        var asAuditor = Operation();
        asAuditor.TargetRole = RegisterRole.Auditor;
        await _sut.CreateApprovalAsync(RegisterId, asAuditor, OrgA, isApproval: true);
        var auditorDigest = _capturedDigest!.ToArray();

        var asOwner = Operation();
        asOwner.TargetRole = RegisterRole.Owner;
        await _sut.CreateApprovalAsync(RegisterId, asOwner, OrgA, isApproval: true);
        var ownerDigest = _capturedDigest!.ToArray();

        // Approving "add them as Auditor" must never authorise "add them as Owner" — the difference
        // between a reader and a party who can transfer the register.
        ownerDigest.Should().NotEqual(auditorDigest);
    }

    [Fact]
    public async Task ApprovalsOnDifferentRegisters_AreNotInterchangeable()
    {
        await _sut.CreateApprovalAsync(RegisterId, Operation(), OrgA, isApproval: true);
        var first = _capturedDigest!.ToArray();

        await _sut.CreateApprovalAsync("ffffffffffffffffffffffffffffffff", Operation(), OrgA, isApproval: true);
        var second = _capturedDigest!.ToArray();

        second.Should().NotEqual(first);
    }
}
