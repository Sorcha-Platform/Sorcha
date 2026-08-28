// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Models.Genesis;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Wallet.Contracts.Constants;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Feature 196 / issue #1591 — the six administrative validation exemptions must be granted from
/// PROVED signer authority, never from a claim in an unsigned field.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every refusal here is paired with a counterfactual in the same run.</b> A test that asserts
/// "the forged claim is refused" proves nothing on its own — a validator that refused everything
/// would pass it. So each route is accompanied by (a) the same key refused WITHOUT the claim, and
/// (b) the legitimate signer ACCEPTED with it. Only the three together show the decision tracks
/// authority rather than the label.
/// </para>
/// <para>
/// <b>Real hashing throughout.</b> #1587's tests stubbed the hash provider to a fixed array, so
/// every digest compared equal by construction and the defect under test was invisible behind a
/// green suite. Fingerprints here are computed by the same code production uses.
/// </para>
/// </remarks>
public class ExemptionAuthorityTests
{
    private const string TestRegister = "test-register";

    private static readonly byte[] GenesisKey = Enumerable.Repeat((byte)7, 32).ToArray();
    private static readonly byte[] RosterKey = Enumerable.Repeat((byte)11, 32).ToArray();
    private static readonly byte[] PublisherKey = Enumerable.Repeat((byte)13, 32).ToArray();
    private static readonly byte[] AttackerKey = Enumerable.Repeat((byte)99, 32).ToArray();

    private readonly Mock<IGovernanceRosterService> _roster = new();

    private ExemptionAuthorityResolver Anchored() =>
        ExemptionAuthorityTestKit.Resolver(ExemptionAuthorityTestKit.AnchorFor(GenesisKey), _roster.Object);

    // ─────────────────────────── transaction builders ───────────────────────────

    private static Transaction Tx(
        byte[] signerKey,
        string? blueprintId = "some-workflow",
        string? txId = null,
        string registerId = TestRegister,
        params (string k, string v)[] metadata)
    {
        var meta = new Dictionary<string, string>();
        foreach (var (k, v) in metadata) meta[k] = v;

        return new Transaction
        {
            TransactionId = txId ?? Guid.NewGuid().ToString("N"),
            RegisterId = registerId,
            BlueprintId = blueprintId,
            ActionId = "1",
            Payload = JsonSerializer.Deserialize<JsonElement>("{}"),
            PayloadHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
            Signatures =
            [
                new RegisterSignature
                {
                    PublicKey = signerKey,
                    SignatureValue = new byte[64],
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            ],
            Metadata = meta
        };
    }

    /// <summary>A transaction shaped exactly like the network's real genesis.</summary>
    private static Transaction RealShapeGenesis(byte[] signerKey) =>
        Tx(signerKey,
           blueprintId: GenesisConstants.BlueprintId,
           txId: GenesisSignatureVerifier.ComputeGenesisTxId(),
           registerId: SystemRegisterConstants.SystemRegisterId);

    private void RosterWith(params RegisterAttestation[] attestations) =>
        _roster.Setup(r => r.GetCurrentRosterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new AdminRoster
               {
                   RegisterId = TestRegister,
                   ControlRecord = new RegisterControlRecord
                   {
                       RegisterId = TestRegister,
                       Name = "Test",
                       CreatedAt = DateTimeOffset.UtcNow,
                       Attestations = attestations.ToList()
                   }
               });

    private void RosterWithValidators(ValidatorRoster validators) =>
        _roster.Setup(r => r.GetCurrentRosterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new AdminRoster
               {
                   RegisterId = TestRegister,
                   ControlRecord = new RegisterControlRecord
                   {
                       RegisterId = TestRegister,
                       Name = "Test",
                       CreatedAt = DateTimeOffset.UtcNow,
                       Attestations = [],
                       Validators = validators
                   }
               });

    private static RegisterAttestation Member(byte[] key) => new()
    {
        Role = RegisterRole.Owner,
        Subject = "did:sorcha:w:member",
        PublicKey = Base64Url.EncodeToString(key),
        Signature = Base64Url.EncodeToString(new byte[64]),
        Algorithm = SignatureAlgorithm.ED25519,
        GrantedAt = DateTimeOffset.UtcNow
    };

    private static ValidatorRoster Validators(byte[] key, string context) => new()
    {
        Validators =
        [
            new ValidatorRosterEntry
            {
                ValidatorId = "node-1",
                PublicKey = Convert.ToBase64String(key),
                Algorithm = SignatureAlgorithm.ED25519,
                DerivationContext = context,
                Status = ValidatorKeyStatus.Active
            }
        ],
        RequiredSignatures = 1,
        Version = 1
    };

    // ════════════════════════ THE COUNTERFACTUAL ════════════════════════

    /// <summary>
    /// The control for every refusal below: an unauthorised key that claims NOTHING is granted
    /// nothing. Without this, a resolver hard-wired to refuse would pass the whole file.
    /// </summary>
    [Fact]
    public async Task NoClaim_IsNotGranted_AndIsNotTreatedAsARefusedClaim()
    {
        var decision = await Anchored().ResolveAsync(Tx(AttackerKey));

        decision.Granted.Should().BeFalse();
        decision.RefusalReason.Should().Be(ExemptionRefusalReason.NoClaim);
        decision.IsRefusedClaim.Should().BeFalse(
            "ordinary traffic must not be counted as an attempted bypass, or the signal is worthless");
    }

    // ════════════════════════ US1 — GENESIS ════════════════════════

    [Fact]
    public async Task Genesis_ClaimedViaTypeLabelByAnAttacker_IsRefused()
    {
        var tx = RealShapeGenesis(AttackerKey);
        tx.Metadata["Type"] = "Genesis";

        var decision = await Anchored().ResolveAsync(tx);

        decision.Granted.Should().BeFalse();
        decision.RefusalReason.Should().Be(ExemptionRefusalReason.NotEntitled);
    }

    [Fact]
    public async Task Genesis_ClaimedViaBlueprintIdentifierByAnAttacker_IsRefused()
    {
        // The second, independent route. It touches no metadata at all, which is why closing only
        // the metadata route would have closed nothing.
        var tx = RealShapeGenesis(AttackerKey);

        var decision = await Anchored().ResolveAsync(tx);

        ExemptionAuthorityResolver.ReadClaim(tx).Route
            .Should().Be(ExemptionClaimRoute.BlueprintIdentifier);
        decision.Granted.Should().BeFalse();
        decision.RefusalReason.Should().Be(ExemptionRefusalReason.NotEntitled);
    }

    /// <summary>
    /// The case the transaction-id check alone does not catch, and the reason the anchor comparison
    /// is the load-bearing part of the genesis rule.
    /// </summary>
    /// <remarks>
    /// The genesis transaction id is <c>SHA-256("genesis-{SystemRegisterId}")</c> — a compile-time
    /// constant. An attacker can therefore set it exactly, supply their own payload with a matching
    /// payload hash, and sign it with their own key: the signature verifies, because the transaction
    /// really is theirs. Only the anchor fingerprint separates that from the real genesis.
    /// </remarks>
    [Fact]
    public async Task Genesis_CorrectTransactionIdButAttackerKey_IsRefused()
    {
        var tx = RealShapeGenesis(AttackerKey);
        tx.TransactionId.Should().Be(GenesisSignatureVerifier.ComputeGenesisTxId(),
            "the id is a constant, so an attacker can always match it");

        var decision = await Anchored().ResolveAsync(tx);

        decision.Granted.Should().BeFalse();
        decision.Detail.Should().Contain("genesis key");
    }

    [Fact]
    public async Task Genesis_SignedByTheAnchoredKey_IsGranted()
    {
        var decision = await Anchored().ResolveAsync(RealShapeGenesis(GenesisKey));

        decision.Granted.Should().BeTrue();
        decision.Kind.Should().Be(ExemptionKind.Genesis);
        decision.IsGenesis.Should().BeTrue();
    }

    [Fact]
    public async Task Genesis_OnANodeHoldingNoAnchor_IsWithheldNotGranted()
    {
        // FR-007: a node that cannot tell has not checked. Fail closed, in every environment.
        var resolver = ExemptionAuthorityTestKit.Resolver(
            ExemptionAuthorityTestKit.NoAnchor(), _roster.Object);

        var decision = await resolver.ResolveAsync(RealShapeGenesis(GenesisKey));

        decision.Granted.Should().BeFalse();
        decision.RefusalReason.Should().Be(ExemptionRefusalReason.AuthorityUnresolvable,
            "'I could not check' must be distinguishable from 'you are not entitled' — they need "
            + "different operator responses");
    }

    [Fact]
    public async Task Genesis_OnAnOrdinaryRegister_IsRefused()
    {
        var tx = Tx(GenesisKey,
            blueprintId: GenesisConstants.BlueprintId,
            txId: GenesisSignatureVerifier.ComputeGenesisTxId(),
            registerId: TestRegister);

        var decision = await Anchored().ResolveAsync(tx);

        decision.Granted.Should().BeFalse("genesis exists only on the system register");
    }

    // ════════════════════════ US3 — CONTROL ════════════════════════

    [Fact]
    public async Task Control_SignedByARosterMember_IsGranted()
    {
        RosterWith(Member(RosterKey));

        var decision = await Anchored().ResolveAsync(Tx(RosterKey, metadata: ("Type", "Control")));

        decision.Granted.Should().BeTrue();
        decision.Kind.Should().Be(ExemptionKind.Control);
    }

    [Fact]
    public async Task Control_SignedByANonMember_IsRefused()
    {
        RosterWith(Member(RosterKey));

        var decision = await Anchored().ResolveAsync(Tx(AttackerKey, metadata: ("Type", "Control")));

        decision.Granted.Should().BeFalse();
        decision.RefusalReason.Should().Be(ExemptionRefusalReason.NotEntitled);
    }

    /// <summary>
    /// US3's point: the exemption now DERIVES from the roster check rather than running alongside
    /// it. With no roster there is no authority, so the waiver is withheld — it cannot be granted on
    /// the label alone the way it could when the two were independent.
    /// </summary>
    [Fact]
    public async Task Control_WithNoRoster_IsWithheldRatherThanGrantedOnTheLabel()
    {
        _roster.Setup(r => r.GetCurrentRosterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((AdminRoster?)null);

        var decision = await Anchored().ResolveAsync(Tx(RosterKey, metadata: ("Type", "Control")));

        decision.Granted.Should().BeFalse();
    }

    [Fact]
    public async Task Control_WhenTheRosterCannotBeRead_FailsClosed()
    {
        _roster.Setup(r => r.GetCurrentRosterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("storage unavailable"));

        var decision = await Anchored().ResolveAsync(Tx(RosterKey, metadata: ("Type", "Control")));

        decision.Granted.Should().BeFalse();
        decision.RefusalReason.Should().Be(ExemptionRefusalReason.AuthorityUnresolvable);
    }

    // ════════════════════════ US2 — BLUEPRINT PUBLICATION ════════════════════════

    [Fact]
    public async Task Publication_SignedByAnAuthorisedPublisher_IsGranted()
    {
        RosterWithValidators(Validators(PublisherKey, SorchaDerivationPaths.BlueprintPublish));

        var decision = await Anchored().ResolveAsync(
            Tx(PublisherKey, metadata: ("Type", "BlueprintPublish")));

        decision.Granted.Should().BeTrue();
        decision.Kind.Should().Be(ExemptionKind.BlueprintPublish);
    }

    [Fact]
    public async Task Publication_SignedByAnAttacker_IsRefused()
    {
        RosterWithValidators(Validators(PublisherKey, SorchaDerivationPaths.BlueprintPublish));

        var decision = await Anchored().ResolveAsync(
            Tx(AttackerKey, metadata: ("Type", "BlueprintPublish")));

        decision.Granted.Should().BeFalse();
        decision.RefusalReason.Should().Be(ExemptionRefusalReason.NotEntitled);
    }

    /// <summary>
    /// The provisioning requirement, pinned as behaviour: a roster carrying only docket-signing keys
    /// authorises nobody to publish.
    /// </summary>
    /// <remarks>
    /// This was the shape of every register before Feature 196 — <c>RegisterCreationOrchestrator</c>
    /// and the genesis ceremony each emitted a single <c>sorcha:docket-signing</c> entry, so
    /// publication would have been refused everywhere. Both now provision a
    /// <c>sorcha:blueprint-publish</c> entry as well. This test pins the fail-closed behaviour that
    /// remains correct for any register lacking one.
    /// </remarks>
    [Theory]
    [InlineData("sorcha:docket-signing")]
    [InlineData("sorcha:register-control")]
    public async Task Publication_WhenTheRosterCarriesNoPublishingContext_IsRefused(string otherContext)
    {
        // Both are real contexts held by the SAME node wallet, and neither authorises publication.
        // Matching is by public key, and each derivation slot yields a DIFFERENT key — so "the
        // bootstrap wallet is on the roster" is not the same as "this wallet may publish".
        RosterWithValidators(Validators(PublisherKey, otherContext));

        var decision = await Anchored().ResolveAsync(
            Tx(PublisherKey, metadata: ("Type", "BlueprintPublish")));

        decision.Granted.Should().BeFalse();
        decision.Detail.Should().Contain(SorchaDerivationPaths.BlueprintPublish);
    }

    [Fact]
    public async Task Publication_ByARevokedPublisher_IsRefused()
    {
        var roster = Validators(PublisherKey, SorchaDerivationPaths.BlueprintPublish);
        roster.Validators[0].Status = ValidatorKeyStatus.Revoked;
        RosterWithValidators(roster);

        var decision = await Anchored().ResolveAsync(
            Tx(PublisherKey, metadata: ("Type", "BlueprintPublish")));

        decision.Granted.Should().BeFalse();
    }

    // ════════════════════════ EFFECTIVE KIND (#917 / research R6) ════════════════════════

    /// <summary>
    /// A publication of the governance blueprint is labelled <c>Type=Control</c> and distinguished
    /// only by a second key, <c>transactionType</c>. Authority must be judged against what it
    /// effectively is, or a publication is measured against governance-roster authority it never had.
    /// </summary>
    [Fact]
    public void ReadClaim_PublicationLabelledAsControl_IsClassifiedAsAPublication()
    {
        var tx = Tx(PublisherKey,
            metadata: [("Type", "Control"), ("transactionType", "BlueprintPublish")]);

        ExemptionAuthorityResolver.ReadClaim(tx).Kind.Should().Be(ExemptionKind.BlueprintPublish);
    }

    [Fact]
    public void ReadClaim_AnUnknownLabel_ClaimsNothing()
    {
        ExemptionAuthorityResolver.ReadClaim(Tx(AttackerKey, metadata: ("Type", "Whatever")))
            .IsClaimed.Should().BeFalse();
    }
}
