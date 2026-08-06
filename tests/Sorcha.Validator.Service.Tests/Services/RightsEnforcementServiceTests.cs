// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
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
    private readonly Mock<ICryptoModule> _cryptoMock = new();
    private readonly RightsEnforcementService _service;

    private static readonly byte[] OwnerPublicKey = new byte[32];
    private static readonly byte[] AdminPublicKey = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 };
    private static readonly byte[] UnknownPublicKey = new byte[] { 99, 98, 97, 96, 95, 94, 93, 92, 91, 90, 89, 88, 87, 86, 85, 84, 83, 82, 81, 80, 79, 78, 77, 76, 75, 74, 73, 72, 71, 70, 69, 68 };

    public RightsEnforcementServiceTests()
    {
        _rosterServiceMock = new Mock<IGovernanceRosterService>();
        var logger = new Mock<ILogger<RightsEnforcementService>>();
        // Feature 189 US2-A: approvals are now cryptographically verified. Default the crypto
        // module to REJECT — a test that wants an approval counted must opt in explicitly, so a
        // forged approval can never pass by accident.
        _cryptoMock
            .Setup(x => x.VerifyAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte>(),
                It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoStatus.InvalidSignature);

        _service = new RightsEnforcementService(
            _rosterServiceMock.Object, _cryptoMock.Object, logger.Object);
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
                new RegisterSignature
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
                new RegisterSignature
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

        // Feature 189 (R-002): this test is named for a GENESIS control transaction but used to
        // build an ordinary governance one — so what it actually asserted was the far broader
        // "any control transaction is allowed when no roster exists". That allowance is the
        // pre-genesis window in which every governance operation was admitted unchecked, and a
        // live DevMode promotion slipped through it and was mistaken for the feature working.
        //
        // The transaction is now genuinely a genesis transaction, which is what the name always
        // claimed. The narrowed behaviour is covered by NoRoster_NonGenesisGovernanceTx_IsRefused.
        var tx = CreateGovernanceTransaction(OwnerPublicKey);
        tx.Metadata["Type"] = "Genesis";

        var result = await _service.ValidateGovernanceRightsAsync(tx);

        result.IsValid.Should().BeTrue("genesis creates the roster, so it cannot require one");
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

    // --- Governance BLUEPRINT publish is NOT a governance operation (#917) ---

    [Fact]
    public async Task ValidateGovernanceRightsAsync_GovernanceBlueprintPublish_NotTreatedAsGovernanceOp()
    {
        // The one-time bootstrap publish of the register-governance-v1 *blueprint* carries
        // BlueprintId == register-governance-v1, but it is a system seed (transactionType
        // "BlueprintPublish"), signed by the system blueprint-publish key which is NOT a roster
        // member. It must NOT be misclassified as a governance roster-operation and rejected
        // VAL_PERM_002 — that drops the governance blueprint from the system register at bootstrap.
        var roster = CreateRoster(
            (OwnerPublicKey, RegisterRole.Owner, "did:sorcha:w:owner1"));
        _rosterServiceMock
            .Setup(r => r.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster);

        var payloadElement = JsonSerializer.Deserialize<JsonElement>("{\"title\":\"Register Governance\"}");
        var tx = new Transaction
        {
            TransactionId = Guid.NewGuid().ToString(),
            RegisterId = "test-register",
            BlueprintId = RightsEnforcementService.GovernanceBlueprintId,
            ActionId = "blueprint-publish",
            Payload = payloadElement,
            PayloadHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
            Signatures =
            [
                new RegisterSignature
                {
                    PublicKey = UnknownPublicKey, // system blueprint-publish key — not a roster member
                    SignatureValue = new byte[64],
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            ],
            Metadata = new Dictionary<string, string> { ["transactionType"] = "BlueprintPublish" }
        };

        var result = await _service.ValidateGovernanceRightsAsync(tx);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().NotContain(e => e.Code == "VAL_PERM_002");
        _rosterServiceMock.Verify(
            r => r.GetCurrentRosterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
                new RegisterSignature
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

    // ==================================================================================
    // Feature 189 — organisation-signed governance.
    //
    // Note on the fixture above: CreateRoster encodes attestation keys with
    // Base64Url.EncodeToString, which is the SAME encoding the old implementation compared
    // against — so the suite agreed with the code and neither noticed that real registers store
    // standard base64. The tests below deliberately build rosters the way genesis actually does.
    // ==================================================================================

    /// <summary>Builds a roster whose keys are encoded the way genesis really stores them.</summary>
    private static AdminRoster CreateRosterWithStandardBase64(
        params (byte[] publicKey, RegisterRole role, string did)[] members)
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
                    // Standard base64 — padded, "+/" alphabet. This is what RegisterAttestation
                    // documents and what a live register carries.
                    PublicKey = Convert.ToBase64String(m.publicKey),
                    Signature = Convert.ToBase64String(new byte[64]),
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow
                }).ToList()
            },
            ControlTransactionCount = 1
        };
    }

    /// <summary>
    /// A key whose base64 encoding contains '+' and requires padding — the shape that makes a
    /// base64-vs-base64url string comparison fail. Modelled on a real n1 roster key
    /// (fFE+9QNpjWLk9+hPDXbfIFctbmex6ONxaOnMVUAkjWA=).
    /// </summary>
    private static byte[] PlusBearingKey()
    {
        var key = Convert.FromBase64String("fFE+9QNpjWLk9+hPDXbfIFctbmex6ONxaOnMVUAkjWA=");
        Convert.ToBase64String(key).Should().Contain("+");
        return key;
    }

    [Fact] // T014 — R-003 regression. Fails against master.
    public async Task GovernanceTx_RosterKeyStoredAsPaddedBase64_StillMatches()
    {
        var orgKey = PlusBearingKey();
        var roster = CreateRosterWithStandardBase64((orgKey, RegisterRole.Owner, "did:sorcha:w:ws11qOwner"));
        _rosterServiceMock.Setup(x => x.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster);

        var tx = CreateGovernanceTransaction(orgKey);
        var result = await _service.ValidateGovernanceRightsAsync(tx);

        // The key IS on the roster. Comparing encoded strings would reject it purely because the
        // roster wrote base64 and the validator wrote base64url.
        result.IsValid.Should().BeTrue(
            "a roster key stored as padded base64 must match the same key supplied as raw bytes");
    }

    [Fact] // T013 — an organisation on the roster is authorised.
    public async Task GovernanceTx_SignedByOrganisationOnRoster_IsAccepted()
    {
        var orgKey = PlusBearingKey();
        _rosterServiceMock.Setup(x => x.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRosterWithStandardBase64((orgKey, RegisterRole.Owner, "did:sorcha:w:ws11qOwner")));

        var result = await _service.ValidateGovernanceRightsAsync(CreateGovernanceTransaction(orgKey));

        result.IsValid.Should().BeTrue();
    }

    [Fact] // T012 — the node's own wallet is never a governance authority.
    public async Task GovernanceTx_SignedByNodeSystemWallet_IsRefused()
    {
        var orgKey = PlusBearingKey();
        var nodeSystemWalletKey = UnknownPublicKey;   // never appears on any roster
        _rosterServiceMock.Setup(x => x.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRosterWithStandardBase64((orgKey, RegisterRole.Owner, "did:sorcha:w:ws11qOwner")));

        var result = await _service.ValidateGovernanceRightsAsync(
            CreateGovernanceTransaction(nodeSystemWalletKey));

        result.IsValid.Should().BeFalse("the node is not an organisation and is never on a roster");
        result.Errors.Should().Contain(e => e.Code == "VAL_PERM_002");
    }

    [Fact] // T016 — every signature is examined, not just the first.
    public async Task GovernanceTx_RosterMemberSignsSecond_IsStillAuthorised()
    {
        var orgKey = PlusBearingKey();
        _rosterServiceMock.Setup(x => x.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRosterWithStandardBase64((orgKey, RegisterRole.Owner, "did:sorcha:w:ws11qOwner")));

        var tx = CreateGovernanceTransaction(UnknownPublicKey);
        tx.Signatures.Add(new RegisterSignature
        {
            PublicKey = orgKey,
            SignatureValue = new byte[64],
            Algorithm = "ED25519",
            SignedAt = DateTimeOffset.UtcNow
        });

        var result = await _service.ValidateGovernanceRightsAsync(tx);

        // Only Signatures[0] used to be considered, so a genuine authority signing in any other
        // position was invisible — which is what made multi-party governance unenforceable.
        result.IsValid.Should().BeTrue("a roster member's signature authorises wherever it appears");
    }

    [Fact] // T016b — a non-roster co-signer confers nothing.
    public async Task GovernanceTx_NonRosterCoSigner_DoesNotAuthorise()
    {
        var orgKey = PlusBearingKey();
        _rosterServiceMock.Setup(x => x.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRosterWithStandardBase64((orgKey, RegisterRole.Owner, "did:sorcha:w:ws11qOwner")));

        var tx = CreateGovernanceTransaction(UnknownPublicKey);
        tx.Signatures.Add(new RegisterSignature
        {
            PublicKey = AdminPublicKey,   // also not on the roster
            SignatureValue = new byte[64],
            Algorithm = "ED25519",
            SignedAt = DateTimeOffset.UtcNow
        });

        var result = await _service.ValidateGovernanceRightsAsync(tx);

        result.IsValid.Should().BeFalse("piling on signatures from non-members must not authorise");
        result.Errors.Should().Contain(e => e.Code == "VAL_PERM_002");
    }

    [Fact] // T015 — R-004. Fails against master, where this shape bypassed enforcement entirely.
    public async Task ProposeShapedTx_IsDetectedAsGovernance_AndRosterEnforced()
    {
        var orgKey = PlusBearingKey();
        _rosterServiceMock.Setup(x => x.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRosterWithStandardBase64((orgKey, RegisterRole.Owner, "did:sorcha:w:ws11qOwner")));

        // The /propose shape: governance discriminator on Metadata["Type"], and a transactionType
        // that is NOT "Control". Under the old detection this matched no arm and was waved through.
        var tx = CreateGovernanceTransaction(UnknownPublicKey, blueprintId: "not-the-governance-blueprint");
        tx.Metadata["Type"] = "Control";
        tx.Metadata["transactionType"] = "GovernanceOperation";

        var result = await _service.ValidateGovernanceRightsAsync(tx);

        result.IsValid.Should().BeFalse(
            "a governance proposal must be roster-enforced, not skipped because of a metadata key mix-up");
        result.Errors.Should().Contain(e => e.Code == "VAL_PERM_002");
    }

    [Fact] // T015b — the #917 bootstrap carve-out must survive the detection change.
    public async Task BlueprintPublishTx_RemainsExemptFromGovernanceEnforcement()
    {
        _rosterServiceMock.Setup(x => x.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRosterWithStandardBase64((PlusBearingKey(), RegisterRole.Owner, "did:sorcha:w:ws11qOwner")));

        var tx = CreateGovernanceTransaction(UnknownPublicKey);
        tx.Metadata["Type"] = "Control";
        tx.Metadata["transactionType"] = "BlueprintPublish";

        var result = await _service.ValidateGovernanceRightsAsync(tx);

        // Publishing the governance blueprint during bootstrap is a system seed signed by the
        // blueprint-publish key, not a roster operation (#917).
        result.IsValid.Should().BeTrue("blueprint publishes are system seeds, not governance operations");
    }

    [Fact] // T018 — R-002. Fails against master, where ANY control tx was admitted with no roster.
    public async Task NoRoster_NonGenesisGovernanceTx_IsRefused()
    {
        _rosterServiceMock.Setup(x => x.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdminRoster?)null);

        var result = await _service.ValidateGovernanceRightsAsync(
            CreateGovernanceTransaction(UnknownPublicKey));

        // The pre-genesis window used to admit everything. A live DevMode promotion "passed"
        // through exactly this gap and was mistaken for the feature working.
        result.IsValid.Should().BeFalse("with no roster there is no authority to authorise against");
        result.Errors.Should().Contain(e => e.Code == "VAL_PERM_007");
    }

    [Fact] // T027 — a sole Owner keeps working: the override means one signature suffices.
    public async Task SingleOwnerRegister_OneSignature_StillAuthorises()
    {
        var orgKey = PlusBearingKey();
        _rosterServiceMock.Setup(x => x.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRosterWithStandardBase64((orgKey, RegisterRole.Owner, "did:sorcha:w:ws11qOwner")));

        var result = await _service.ValidateGovernanceRightsAsync(CreateGovernanceTransaction(orgKey));

        // The required-signer threshold must never regress User Story 1's single-organisation case.
        result.IsValid.Should().BeTrue();
        result.Errors.Should().NotContain(e => e.Code == "VAL_PERM_008");
    }

    [Fact] // T027 — a refusal must tell an administrator what to do about it (SC-008).
    public async Task Refusal_CarriesAnActionableReason()
    {
        var orgKey = PlusBearingKey();
        _rosterServiceMock.Setup(x => x.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRosterWithStandardBase64((orgKey, RegisterRole.Owner, "did:sorcha:w:ws11qOwner")));

        var result = await _service.ValidateGovernanceRightsAsync(
            CreateGovernanceTransaction(UnknownPublicKey));

        result.IsValid.Should().BeFalse();
        var error = result.Errors.Single(e => e.Code == "VAL_PERM_002");
        // "does not match any member" states the actual condition, not an internal code.
        error.Message.Should().Contain("roster");
        error.Message.Should().NotBeNullOrWhiteSpace();
    }

    // ------------------------------------------------------------------
    // Feature 189 US2-A — approvals must be cryptographically verified.
    //
    // Before this, ValidateQuorumAsync counted an approval whose ApproverDid merely appeared in
    // the roster; ApprovalSignature.Signature was never checked anywhere in src/. Quorum was
    // therefore satisfiable by ASSERTING approvals — whoever composed the payload could claim
    // every other member's vote, with no cryptography involved at any point.
    // ------------------------------------------------------------------

    private static GovernanceOperation AddOperationWithApprovals(params (string did, bool approve)[] votes)
        => new()
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:admin1",
            TargetDid = "did:sorcha:w:newadmin",
            TargetRole = RegisterRole.Admin,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            ApprovalSignatures = votes.Select(v => new ApprovalSignature
            {
                ApproverDid = v.did,
                Signature = Convert.ToBase64String(new byte[64]),
                IsApproval = v.approve,
                VotedAt = DateTimeOffset.UtcNow
            }).ToList()
        };

    [Fact]
    public async Task ForgedApprovals_AreNotCounted_SoQuorumIsNotMet()
    {
        var roster = CreateRoster(
            (OwnerPublicKey, RegisterRole.Owner, "did:sorcha:w:owner1"),
            (AdminPublicKey, RegisterRole.Admin, "did:sorcha:w:admin1"));
        _rosterServiceMock.Setup(r => r.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster);
        _rosterServiceMock.Setup(r => r.ValidateProposal(roster, It.IsAny<GovernanceOperation>()))
            .Returns(GovernanceValidationResult.Success());

        // The admin claims the owner's approval. The crypto mock rejects (fixture default).
        var operation = AddOperationWithApprovals(("did:sorcha:w:owner1", true));
        var tx = CreateGovernanceTransaction(AdminPublicKey, operation);

        await _service.ValidateGovernanceRightsAsync(tx);

        // The forged approval must never reach the quorum count.
        _rosterServiceMock.Verify(r => r.ValidateQuorumAsync(
            It.IsAny<string>(), It.IsAny<GovernanceOperation>(),
            It.Is<List<ApprovalSignature>>(l => l.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenuinelySignedApprovals_AreCounted()
    {
        var roster = CreateRoster(
            (OwnerPublicKey, RegisterRole.Owner, "did:sorcha:w:owner1"),
            (AdminPublicKey, RegisterRole.Admin, "did:sorcha:w:admin1"));
        _rosterServiceMock.Setup(r => r.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster);
        _rosterServiceMock.Setup(r => r.ValidateProposal(roster, It.IsAny<GovernanceOperation>()))
            .Returns(GovernanceValidationResult.Success());
        _rosterServiceMock.Setup(r => r.ValidateQuorumAsync(
                It.IsAny<string>(), It.IsAny<GovernanceOperation>(),
                It.IsAny<List<ApprovalSignature>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuorumResult { IsQuorumMet = true, VotesRequired = 1, VotesReceived = 1, VotingPool = 2 });

        // This approval verifies.
        _cryptoMock.Setup(x => x.VerifyAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte>(),
                It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoStatus.Success);

        var operation = AddOperationWithApprovals(("did:sorcha:w:owner1", true));
        var result = await _service.ValidateGovernanceRightsAsync(
            CreateGovernanceTransaction(AdminPublicKey, operation));

        // Verification must not become a blanket refusal — a real approval still counts.
        _rosterServiceMock.Verify(r => r.ValidateQuorumAsync(
            It.IsAny<string>(), It.IsAny<GovernanceOperation>(),
            It.Is<List<ApprovalSignature>>(l => l.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ApprovalFromNonRosterMember_IsDiscardedWithoutVerifying()
    {
        var roster = CreateRoster(
            (OwnerPublicKey, RegisterRole.Owner, "did:sorcha:w:owner1"),
            (AdminPublicKey, RegisterRole.Admin, "did:sorcha:w:admin1"));
        _rosterServiceMock.Setup(r => r.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster);
        _rosterServiceMock.Setup(r => r.ValidateProposal(roster, It.IsAny<GovernanceOperation>()))
            .Returns(GovernanceValidationResult.Success());
        _cryptoMock.Setup(x => x.VerifyAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte>(),
                It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoStatus.Success);

        // An outsider's approval, even perfectly signed, is not a roster vote. It must be dropped
        // before verification — we never verify against a key the approval brought with it.
        var operation = AddOperationWithApprovals(("did:sorcha:w:outsider", true));

        await _service.ValidateGovernanceRightsAsync(
            CreateGovernanceTransaction(AdminPublicKey, operation));

        _rosterServiceMock.Verify(r => r.ValidateQuorumAsync(
            It.IsAny<string>(), It.IsAny<GovernanceOperation>(),
            It.Is<List<ApprovalSignature>>(l => l.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact] // T018b — but genesis must still be able to create the roster.
    public async Task NoRoster_GenesisTx_IsStillAdmitted()
    {
        _rosterServiceMock.Setup(x => x.GetCurrentRosterAsync("test-register", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdminRoster?)null);

        var tx = CreateGovernanceTransaction(UnknownPublicKey);
        tx.Metadata["Type"] = "Genesis";

        var result = await _service.ValidateGovernanceRightsAsync(tx);

        result.IsValid.Should().BeTrue("genesis is what creates the roster, so it cannot require one");
    }
}
