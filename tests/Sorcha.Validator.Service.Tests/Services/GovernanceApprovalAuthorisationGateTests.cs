// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Utilities;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Validator.Core.Validators;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// A node receiving a <b>replicated</b> approval re-verifies its accountability block for itself
/// (Feature 189, T079/T080).
/// </summary>
/// <remarks>
/// <para>
/// Until this landed, only the Register Service that took the approval in over HTTP verified the
/// <c>authorisation</c>. Every other node folded the sealed transaction and counted the vote on the
/// organisation's signature alone — so accountability was verified <i>once</i>, by whichever node
/// happened to receive the submission, rather than by each node that acts on it. An approval whose
/// authorisation was forged, expired, out of scope or simply absent still counted everywhere else.
/// </para>
/// <para>
/// <b>Real keys, real signatures.</b> The subject here is whether a signature means what it claims,
/// so a fabricated one would prove nothing. Every key is generated, every signature actually
/// produced, and every DID derived from its key rather than written by hand.
/// </para>
/// <para>
/// The assertion is on the votes handed to <c>ValidateQuorumAsync</c>, because that list <i>is</i>
/// the output of the counting under test. Asserting on the final verdict would pass for the wrong
/// reason — the quorum verdict is mocked, so a refusal there could come from arithmetic rather than
/// from the approval being excluded.
/// </para>
/// </remarks>
public sealed class GovernanceApprovalAuthorisationGateTests
{
    private const string RegisterId = "test-register";
    private const string ProposalId = "proposal-tx";

    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly ICryptoModule _crypto = new CryptoModule();
    private readonly IWalletUtilities _wallets = new WalletUtilities();

    private readonly Mock<IGovernanceRosterService> _roster = new();
    private readonly Mock<IReadOnlyRegisterRepository> _repository = new();

    /// <summary>The votes the service actually handed to the quorum arithmetic.</summary>
    private List<ApprovalSignature> _votesCounted = [];

    private sealed record Identity(byte[] PrivateKey, byte[] PublicKey, string PublicKeyB64, string Did);

    private async Task<Identity> NewIdentityAsync()
    {
        var keySet = (await _crypto.GenerateKeySetAsync(WalletNetworks.ED25519)).Value!;
        var pub = keySet.PublicKey.Key!;
        var address = _wallets.PublicKeyToWallet(pub, (byte)WalletNetworks.ED25519)!;

        return new Identity(
            keySet.PrivateKey.Key!, pub, Convert.ToBase64String(pub), $"did:sorcha:w:{address}");
    }

    private async Task<string> SignAsync(byte[] digest, Identity id)
    {
        var result = await _crypto.SignAsync(digest, (byte)WalletNetworks.ED25519, id.PrivateKey);
        return Convert.ToBase64String(result.Value!);
    }

    private static RegisterAttestation Attestation(Identity id, RegisterRole role) => new()
    {
        Subject = id.Did,
        Role = role,
        PublicKey = id.PublicKeyB64,
        Signature = Convert.ToBase64String(new byte[64]),
        Algorithm = SignatureAlgorithm.ED25519,
        GrantedAt = DateTimeOffset.UnixEpoch,
    };

    private static AdminRoster Roster(params RegisterAttestation[] attestations) => new()
    {
        RegisterId = RegisterId,
        ControlRecord = new RegisterControlRecord
        {
            RegisterId = RegisterId,
            Name = "Test",
            CreatedAt = DateTimeOffset.UnixEpoch,
            Attestations = [.. attestations],
        },
        ControlTransactionCount = 1,
        LastControlTxId = "genesis-tx",
    };

    private static GovernanceOperation Operation(string proposerDid) => new()
    {
        OperationType = GovernanceOperationType.Add,
        ProposerDid = proposerDid,
        TargetDid = "did:sorcha:w:newadmin",
        TargetRole = RegisterRole.Admin,
        Status = ProposalStatus.Pending,
        ProposedAt = Now.AddHours(-1),
        ExpiresAt = Now.AddYears(50),
        RosterSnapshotId = "genesis-tx",
    };

    private static TransactionModel ProposalTransaction(GovernanceOperation operation) => new()
    {
        TxId = ProposalId,
        RegisterId = RegisterId,
        Payloads =
        [
            new PayloadModel
            {
                Data = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
                    new ControlTransactionPayload
                    {
                        Version = 1,
                        Roster = null,
                        Operation = operation,
                    })),
            }
        ],
    };

    /// <summary>Seals an approval payload as the transaction a remote node would replicate.</summary>
    private static TransactionModel ApprovalTransaction(
        GovernanceApprovalActionPayload payload, ulong docketNumber = 1) => new()
    {
        TxId = $"approval-{payload.ApproverDid}",
        RegisterId = RegisterId,
        DocketNumber = docketNumber,
        Payloads =
        [
            new PayloadModel
            {
                // The options the payload is written with everywhere else. Reading it back with
                // ad-hoc ones throws on the kebab-case enums, and every reader treats a throw as
                // "not an approval" — which is how the tally silently counted nothing on n1.
                Data = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
                    payload, GovernanceApprovalActionPayload.CanonicalJsonOptions)),
            }
        ],
    };

    private static Transaction Enactment(GovernanceOperation proposed, Identity carrier)
    {
        var enacted = JsonSerializer.Deserialize<GovernanceOperation>(
            JsonSerializer.Serialize(proposed))!;
        enacted.Status = ProposalStatus.Recorded;

        var payload = new ControlTransactionPayload
        {
            Version = 1,
            Roster = Roster().ControlRecord,
            Operation = enacted,
            EnactsProposalId = ProposalId,
        };

        return new Transaction
        {
            TransactionId = "enactment-tx",
            RegisterId = RegisterId,
            BlueprintId = RightsEnforcementService.GovernanceBlueprintId,
            ActionId = "1",
            Payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload)),
            PayloadHash = "hash",
            CreatedAt = Now,
            Signatures =
            [
                new RegisterSignature
                {
                    PublicKey = carrier.PublicKey,
                    SignatureValue = new byte[64],
                    Algorithm = "ED25519",
                    SignedAt = Now,
                }
            ],
        };
    }

    private RightsEnforcementService Service() => new(
        _roster.Object,
        _crypto,
        Mock.Of<ILogger<RightsEnforcementService>>(),
        metrics: null,
        repository: _repository.Object,
        approvalVerifier: new DetachedApprovalVerifier(_crypto, _wallets));

    /// <summary>
    /// Wires the register up around one proposal and the approvals sealed against it.
    /// </summary>
    private void Arrange(
        AdminRoster roster,
        GovernanceOperation operation,
        params GovernanceApprovalActionPayload[] approvals)
    {
        _roster.Setup(r => r.GetCurrentRosterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster);
        _roster.Setup(r => r.ValidateProposal(It.IsAny<AdminRoster>(), It.IsAny<GovernanceOperation>()))
            .Returns(GovernanceValidationResult.Success());
        _roster.Setup(r => r.ValidateQuorumAsync(
                It.IsAny<string>(), It.IsAny<GovernanceOperation>(),
                It.IsAny<List<ApprovalSignature>>(), It.IsAny<CancellationToken>()))
            .Callback<string, GovernanceOperation, List<ApprovalSignature>, CancellationToken>(
                (_, _, votes, _) => _votesCounted = votes)
            .ReturnsAsync(new QuorumResult
            {
                IsQuorumMet = false, VotesRequired = 2, VotesReceived = 0, VotingPool = 2
            });

        _repository.Setup(r => r.GetTransactionAsync(RegisterId, ProposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProposalTransaction(operation));
        _repository.Setup(r => r.GetTransactionsByPrevTxIdAsync(
                RegisterId, ProposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([.. approvals.Select((a, i) => ApprovalTransaction(a, (ulong)(i + 1)))]);
    }

    /// <summary>Builds an approval whose organisation signature is genuine.</summary>
    private async Task<GovernanceApprovalActionPayload> ApprovalAsync(
        Identity org, GovernanceOperation operation, ApprovalAuthorisation? authorisation)
    {
        var digest = GovernanceApprovalStatement.ComputeDigest(RegisterId, operation, org.Did, true);

        return new GovernanceApprovalActionPayload
        {
            ProposalId = ProposalId,
            ApproverDid = org.Did,
            IsApproval = true,
            Signature = await SignAsync(digest, org),
            PublicKey = org.PublicKeyB64,
            AuthMethod = ApprovalAuthMethod.HardwareBacked,
            Authorisation = authorisation,
        };
    }

    /// <summary>A genuine direct authorisation: the named individual signs with their own key.</summary>
    private async Task<ApprovalAuthorisation> DirectAuthorisationAsync(
        Identity person, Identity org, GovernanceOperation operation)
    {
        var digest = GovernanceApprovalStatement.ComputeDigest(RegisterId, operation, org.Did, true);

        return new ApprovalAuthorisation
        {
            Kind = AuthorisationKind.Direct,
            IndividualDid = person.Did,
            Signature = await SignAsync(digest, person),
            PublicKey = person.PublicKeyB64,
            AuthMethod = ApprovalAuthMethod.HardwareBacked,
        };
    }

    /// <summary>
    /// The control. Everything below asserts an approval is refused, so without this they would all
    /// pass against a validator that simply counted nothing.
    /// </summary>
    [Fact]
    public async Task AnApproval_WithAGenuineAuthorisation_Counts()
    {
        var owner = await NewIdentityAsync();
        var admin = await NewIdentityAsync();
        var person = await NewIdentityAsync();
        var operation = Operation(admin.Did);

        var approval = await ApprovalAsync(
            owner, operation, await DirectAuthorisationAsync(person, owner, operation));

        Arrange(Roster(Attestation(owner, RegisterRole.Owner), Attestation(admin, RegisterRole.Admin)),
            operation, approval);

        await Service().ValidateGovernanceRightsAsync(Enactment(operation, owner));

        _votesCounted.Should().ContainSingle()
            .Which.ApproverDid.Should().Be(owner.Did);
    }

    /// <summary>
    /// FR-029: no approval reaches a tally without resolving to a named individual — on <i>every</i>
    /// node, not only the one that took the submission in.
    /// </summary>
    [Fact]
    public async Task AnApproval_WithNoAuthorisation_DoesNotCount()
    {
        var owner = await NewIdentityAsync();
        var admin = await NewIdentityAsync();
        var operation = Operation(admin.Did);

        var approval = await ApprovalAsync(owner, operation, authorisation: null);

        Arrange(Roster(Attestation(owner, RegisterRole.Owner), Attestation(admin, RegisterRole.Admin)),
            operation, approval);

        await Service().ValidateGovernanceRightsAsync(Enactment(operation, owner));

        _votesCounted.Should().BeEmpty(
            "an organisation signature carries authority, but accountability is not optional");
    }

    /// <summary>
    /// FR-032: an authorisation that does not verify refuses the approval outright. It is never
    /// accepted with the accountability quietly discarded, which would leave a record that looks
    /// complete and is not.
    /// </summary>
    [Fact]
    public async Task AnApproval_WhoseAuthorisationSignatureIsForged_DoesNotCount()
    {
        var owner = await NewIdentityAsync();
        var admin = await NewIdentityAsync();
        var person = await NewIdentityAsync();
        var impostor = await NewIdentityAsync();
        var operation = Operation(admin.Did);

        var authorisation = await DirectAuthorisationAsync(person, owner, operation);

        // Names the person, but the signature is the impostor's — and its own key is offered with it,
        // which is exactly what the key-to-DID binding exists to catch.
        authorisation.Signature = await SignAsync(
            GovernanceApprovalStatement.ComputeDigest(RegisterId, operation, owner.Did, true), impostor);
        authorisation.PublicKey = impostor.PublicKeyB64;

        Arrange(Roster(Attestation(owner, RegisterRole.Owner), Attestation(admin, RegisterRole.Admin)),
            operation, await ApprovalAsync(owner, operation, authorisation));

        await Service().ValidateGovernanceRightsAsync(Enactment(operation, owner));

        _votesCounted.Should().BeEmpty(
            "signing with your own key while naming somebody else makes accountability a self-declaration");
    }

    /// <summary>
    /// T080: the authorisation is attestation metadata, never a roster claim. An individual with a
    /// perfectly good signature is not a member of the register's governance.
    /// </summary>
    [Fact]
    public async Task AnAuthorisationAlone_DoesNotSatisfyTheRoster()
    {
        var owner = await NewIdentityAsync();
        var admin = await NewIdentityAsync();
        var person = await NewIdentityAsync();
        var outsider = await NewIdentityAsync();
        var operation = Operation(admin.Did);

        // The approval is offered by an organisation that is NOT on the roster, carrying a genuine
        // authorisation. Both signatures verify; neither confers governance authority.
        var approval = await ApprovalAsync(
            outsider, operation, await DirectAuthorisationAsync(person, outsider, operation));

        Arrange(Roster(Attestation(owner, RegisterRole.Owner), Attestation(admin, RegisterRole.Admin)),
            operation, approval);

        await Service().ValidateGovernanceRightsAsync(Enactment(operation, owner));

        _votesCounted.Should().BeEmpty(
            "a valid authorisation attests who decided; the roster decides whose decision counts");
    }
}
