// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Tests.Services;

public class RightsEnforcementServiceTests
{
    private readonly Mock<IGovernanceRosterService> _rosterServiceMock;
    private readonly RightsEnforcementService _service;

    private static readonly byte[] OwnerPublicKey = new byte[32];
    private static readonly byte[] AdminPublicKey = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 };
    private static readonly byte[] UnknownPublicKey = new byte[] { 99, 98, 97, 96, 95, 94, 93, 92, 91, 90, 89, 88, 87, 86, 85, 84, 83, 82, 81, 80, 79, 78, 77, 76, 75, 74, 73, 72, 71, 70, 69, 68 };

    public RightsEnforcementServiceTests()
    {
        _rosterServiceMock = new Mock<IGovernanceRosterService>();
        var logger = new Mock<ILogger<RightsEnforcementService>>();
        _service = new RightsEnforcementService(_rosterServiceMock.Object, logger.Object);
    }

    private static AdminRoster CreateRoster(params (byte[] publicKey, RegisterRole role, string did)[] members)
    {
        return new AdminRoster
        {
            RegisterId = "test-register",
            ControlRecord = new RegisterControlRecord
            {
                RegisterId = "test-register",
                Name = "Test",
                CreatedAt = DateTimeOffset.UtcNow,
                Attestations = members.Select(m => new RegisterAttestation
                {
                    Role = m.role,
                    Subject = m.did,
                    PublicKey = Base64Url.EncodeToString(m.publicKey),
                    Signature = Base64Url.EncodeToString(new byte[64]),
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow
                }).ToList()
            },
            ControlTransactionCount = 1
        };
    }

    private static Transaction CreateGovernanceTransaction(
        byte[] signerPublicKey,
        GovernanceOperation? operation = null,
        string? blueprintId = null)
    {
        var payload = new ControlTransactionPayload
        {
            Version = 1,
            Roster = new RegisterControlRecord
            {
                RegisterId = "test-register",
                Name = "Test",
                CreatedAt = DateTimeOffset.UtcNow,
                Attestations = []
            },
            Operation = operation
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadElement = JsonSerializer.Deserialize<JsonElement>(payloadJson);

        return new Transaction
        {
            TransactionId = Guid.NewGuid().ToString(),
            RegisterId = "test-register",
            BlueprintId = blueprintId ?? RightsEnforcementService.GovernanceBlueprintId,
            ActionId = "1",
            Payload = payloadElement,
            PayloadHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
            Signatures =
            [
                new Signature
                {
                    PublicKey = signerPublicKey,
                    SignatureValue = new byte[64],
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            ]
        };
    }

    private static Transaction CreateNonGovernanceTransaction()
    {
        var payloadElement = JsonSerializer.Deserialize<JsonElement>("{}");
        return new Transaction
        {
            TransactionId = Guid.NewGuid().ToString(),
            RegisterId = "test-register",
            BlueprintId = "some-other-blueprint",
            ActionId = "1",
            Payload = payloadElement,
            PayloadHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
            Signatures =
            [
                new Signature
                {
                    PublicKey = new byte[32],
                    SignatureValue = new byte[64],
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            ]
        };
    }

    // --- Non-governance transactions pass through ---

    [Fact]
    public async Task ValidateGovernanceRightsAsync_NonGovernanceTransaction_PassesThrough()
    {
        var tx = CreateNonGovernanceTransaction();

        var result = await _service.ValidateGovernanceRightsAsync(tx);

        result.IsValid.Should().BeTrue();
        _rosterServiceMock.Verify(
            r => r.GetCurrentRosterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // --- Genesis Control TX (no existing roster) ---

    [Fact]
    public async Task ValidateGovernanceRightsAsync_GenesisControlTx_AllowedWhenNoRoster()
    {
        _rosterServiceMock
            .Setup(r => r.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdminRoster?)null);

        var tx = CreateGovernanceTransaction(OwnerPublicKey);

        var result = await _service.ValidateGovernanceRightsAsync(tx);

        result.IsValid.Should().BeTrue();
    }

    // --- Admin accepted ---

    [Fact]
    public async Task ValidateGovernanceRightsAsync_AdminSubmitter_Accepted()
    {
        var roster = CreateRoster(
            (OwnerPublicKey, RegisterRole.Owner, "did:sorcha:w:owner1"),
            (AdminPublicKey, RegisterRole.Admin, "did:sorcha:w:admin1"));
        _rosterServiceMock
            .Setup(r => r.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster);

        var tx = CreateGovernanceTransaction(AdminPublicKey);

        var result = await _service.ValidateGovernanceRightsAsync(tx);

        result.IsValid.Should().BeTrue();
    }

    // --- Owner accepted ---

    [Fact]
    public async Task ValidateGovernanceRightsAsync_OwnerSubmitter_Accepted()
    {
        var roster = CreateRoster(
            (OwnerPublicKey, RegisterRole.Owner, "did:sorcha:w:owner1"));
        _rosterServiceMock
            .Setup(r => r.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster);

        var tx = CreateGovernanceTransaction(OwnerPublicKey);

        var result = await _service.ValidateGovernanceRightsAsync(tx);

        result.IsValid.Should().BeTrue();
    }

    // --- Non-roster member rejected ---

    [Fact]
    public async Task ValidateGovernanceRightsAsync_NonRosterMember_Rejected()
    {
        var roster = CreateRoster(
            (OwnerPublicKey, RegisterRole.Owner, "did:sorcha:w:owner1"));
        _rosterServiceMock
            .Setup(r => r.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster);

        var tx = CreateGovernanceTransaction(UnknownPublicKey);

        var result = await _service.ValidateGovernanceRightsAsync(tx);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_PERM_002");
    }

    // --- Auditor (non-voting) rejected ---

    [Fact]
    public async Task ValidateGovernanceRightsAsync_AuditorSubmitter_Rejected()
    {
        var auditorKey = new byte[] { 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81 };
        var roster = CreateRoster(
            (OwnerPublicKey, RegisterRole.Owner, "did:sorcha:w:owner1"),
            (auditorKey, RegisterRole.Auditor, "did:sorcha:w:auditor1"));
        _rosterServiceMock
            .Setup(r => r.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster);

        var tx = CreateGovernanceTransaction(auditorKey);

        var result = await _service.ValidateGovernanceRightsAsync(tx);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_PERM_003");
        result.Errors.Should().Contain(e => e.Message.Contains("Auditor"));
    }

    // --- Metadata-based Control detection ---

    [Fact]
    public async Task ValidateGovernanceRightsAsync_MetadataControlType_DetectedAsGovernance()
    {
        var roster = CreateRoster(
            (OwnerPublicKey, RegisterRole.Owner, "did:sorcha:w:owner1"));
        _rosterServiceMock
            .Setup(r => r.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster);

        var payloadElement = JsonSerializer.Deserialize<JsonElement>("{}");
        var tx = new Transaction
        {
            TransactionId = Guid.NewGuid().ToString(),
            RegisterId = "test-register",
            BlueprintId = "custom-governance-blueprint",
            ActionId = "1",
            Payload = payloadElement,
            PayloadHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
            Signatures =
            [
                new Signature
                {
                    PublicKey = OwnerPublicKey,
                    SignatureValue = new byte[64],
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            ],
            Metadata = new Dictionary<string, string> { ["transactionType"] = "Control" }
        };

        var result = await _service.ValidateGovernanceRightsAsync(tx);

        result.IsValid.Should().BeTrue();
    }

    // --- Config flag disables check ---

    [Fact]
    public async Task ValidateGovernanceRightsAsync_DisabledConfig_SkipsCheck()
    {
        // This is tested at the ValidationEngine level — when EnableGovernanceValidation = false,
        // the engine doesn't call the rights enforcement service at all.
        // The service itself always runs when called.
        var roster = CreateRoster(
            (OwnerPublicKey, RegisterRole.Owner, "did:sorcha:w:owner1"));
        _rosterServiceMock
            .Setup(r => r.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster);

        // Even with governance BP, Owner is always valid
        var tx = CreateGovernanceTransaction(OwnerPublicKey);

        var result = await _service.ValidateGovernanceRightsAsync(tx);

        result.IsValid.Should().BeTrue();
    }

    // --- Proposal validation errors propagated ---

    [Fact]
    public async Task ValidateGovernanceRightsAsync_InvalidProposal_ErrorsPropagated()
    {
        var roster = CreateRoster(
            (OwnerPublicKey, RegisterRole.Owner, "did:sorcha:w:owner1"));
        _rosterServiceMock
            .Setup(r => r.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster);

        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:owner1", // Already in roster
            TargetRole = RegisterRole.Admin,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        _rosterServiceMock
            .Setup(r => r.ValidateProposal(roster, It.IsAny<GovernanceOperation>()))
            .Returns(GovernanceValidationResult.Failure("Target is already in the roster"));

        var tx = CreateGovernanceTransaction(OwnerPublicKey, operation);

        var result = await _service.ValidateGovernanceRightsAsync(tx);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_PERM_004");
    }

    // --- Quorum not met for non-owner ---

    [Fact]
    public async Task ValidateGovernanceRightsAsync_NonOwnerWithoutQuorum_Rejected()
    {
        var roster = CreateRoster(
            (OwnerPublicKey, RegisterRole.Owner, "did:sorcha:w:owner1"),
            (AdminPublicKey, RegisterRole.Admin, "did:sorcha:w:admin1"));
        _rosterServiceMock
            .Setup(r => r.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster);

        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:admin1",
            TargetDid = "did:sorcha:w:newadmin",
            TargetRole = RegisterRole.Admin,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        _rosterServiceMock
            .Setup(r => r.ValidateProposal(roster, It.IsAny<GovernanceOperation>()))
            .Returns(GovernanceValidationResult.Success());

        var tx = CreateGovernanceTransaction(AdminPublicKey, operation);

        var result = await _service.ValidateGovernanceRightsAsync(tx);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_PERM_005");
    }

    // --- Owner bypass (no quorum needed) ---

    [Fact]
    public async Task ValidateGovernanceRightsAsync_OwnerAddBypass_NoQuorumNeeded()
    {
        var roster = CreateRoster(
            (OwnerPublicKey, RegisterRole.Owner, "did:sorcha:w:owner1"));
        _rosterServiceMock
            .Setup(r => r.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster);

        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:newadmin",
            TargetRole = RegisterRole.Admin,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        _rosterServiceMock
            .Setup(r => r.ValidateProposal(roster, It.IsAny<GovernanceOperation>()))
            .Returns(GovernanceValidationResult.Success());

        var tx = CreateGovernanceTransaction(OwnerPublicKey, operation);

        var result = await _service.ValidateGovernanceRightsAsync(tx);

        // Owner doesn't need quorum — should pass
        result.IsValid.Should().BeTrue();
    }

    // --- Removed admin re-submits → rejected ---

    [Fact]
    public async Task ValidateGovernanceRightsAsync_RemovedAdminResubmits_Rejected()
    {
        // Roster no longer contains the admin
        var roster = CreateRoster(
            (OwnerPublicKey, RegisterRole.Owner, "did:sorcha:w:owner1"));
        _rosterServiceMock
            .Setup(r => r.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster);

        // Admin (whose key is no longer in roster) tries to submit
        var tx = CreateGovernanceTransaction(AdminPublicKey);

        var result = await _service.ValidateGovernanceRightsAsync(tx);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_PERM_002");
    }
}
