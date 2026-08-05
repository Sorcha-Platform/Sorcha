// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Provenance;
using Xunit;

namespace Sorcha.Register.Service.Tests.Provenance;

/// <summary>
/// The resolver that answers "which validator set applied when THIS docket was sealed?".
/// </summary>
/// <remarks>
/// <para>
/// The engine cannot get this wrong, because it is never handed the current roster (plan D5). The
/// resolver is where it can be got wrong, so this is where the roster-as-of scenario is tested
/// against a real walk over control transactions rather than against hand-supplied evidence.
/// </para>
/// <para>
/// <b>The off-by-one is the whole risk.</b> A roster change sealed in docket 12 cannot govern
/// docket 12 — that docket was proposed and signed under the set in force at the time, and the
/// change only takes effect once it is sealed. So the roster applying at docket N is the one
/// established by the latest control transaction sealed <i>strictly before</i> N, with the genesis
/// control record applying to docket 0 itself. Getting this inclusive instead of exclusive would
/// report the sealing validators of every governance docket as unauthorised — a false accusation
/// landing precisely on the dockets that record the network growing.
/// </para>
/// </remarks>
public class RosterAsOfResolverTests
{
    private const string RegisterId = "b388e51816e34d4ea7ce275ca7e8219c";

    private const string KeyA = "a2V5LWE=";
    private const string KeyB = "a2V5LWI=";

    /// <summary>Genesis roster, sealed in docket 0: validator-a and validator-b.</summary>
    private static TransactionModel GenesisControlTx() => ControlTx(
        txId: "ctrl-genesis",
        docketNumber: 0,
        version: 1,
        (ValidatorId: "validator-a", Key: KeyA, Status: ValidatorKeyStatus.Active),
        (ValidatorId: "validator-b", Key: KeyB, Status: ValidatorKeyStatus.Active));

    /// <summary>The removal of validator-b, sealed in docket 12.</summary>
    private static TransactionModel RemovalControlTx() => ControlTx(
        txId: "ctrl-remove-b",
        docketNumber: 12,
        version: 2,
        (ValidatorId: "validator-a", Key: KeyA, Status: ValidatorKeyStatus.Active));

    private static TransactionModel ControlTx(
        string txId,
        ulong docketNumber,
        int version,
        params (string ValidatorId, string Key, ValidatorKeyStatus Status)[] validators)
    {
        var record = new RegisterControlRecord
        {
            RegisterId = RegisterId,
            Name = "test",
            CreatedAt = DateTimeOffset.UnixEpoch,
            Attestations =
            [
                new RegisterAttestation { Role = RegisterRole.Owner, Subject = "owner-wallet" },
            ],
            Validators = new ValidatorRoster
            {
                Version = version,
                RequiredSignatures = 1,
                Validators = validators.Select(v => new ValidatorRosterEntry
                {
                    ValidatorId = v.ValidatorId,
                    PublicKey = v.Key,
                    Status = v.Status,
                    DerivationContext = "sorcha:docket-signing",
                    AuthorizedAt = DateTimeOffset.UnixEpoch,
                }).ToList(),
            },
        };

        var payload = new ControlTransactionPayload { Version = 1, Roster = record };
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);

        return new TransactionModel
        {
            RegisterId = RegisterId,
            TxId = txId,
            DocketNumber = docketNumber,
            Payloads = [new PayloadModel { Data = Convert.ToBase64String(json) }],
        };
    }

    private static RosterAsOfResolver ResolverOver(params TransactionModel[] controlTransactions)
    {
        var repo = new Mock<IReadOnlyRegisterRepository>();
        repo.Setup(r => r.GetTransactionsByTypeAsync(
                RegisterId,
                TransactionType.Control,
                TransactionSort.DocketNumberAscending,
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(controlTransactions.OrderBy(t => t.DocketNumber).ToList());

        return new RosterAsOfResolver(repo.Object, NullLogger<RosterAsOfResolver>.Instance);
    }

    [Fact]
    public async Task DocketBeforeTheRemoval_ResolvesTheRosterThatStillContainedTheRemovedValidator()
    {
        var resolver = ResolverOver(GenesisControlTx(), RemovalControlTx());

        var roster = await resolver.ResolveAsync(RegisterId, docketNumber: 10, CancellationToken.None);

        roster.Should().NotBeNull();
        roster!.RosterVersion.Should().Be(1);
        roster.Entries.Select(e => e.ValidatorId).Should().BeEquivalentTo(["validator-a", "validator-b"]);
        roster.ResolvedFrom.Should().Contain("ctrl-genesis");
    }

    [Fact]
    public async Task DocketAfterTheRemoval_ResolvesTheRosterWithoutIt()
    {
        var resolver = ResolverOver(GenesisControlTx(), RemovalControlTx());

        var roster = await resolver.ResolveAsync(RegisterId, docketNumber: 14, CancellationToken.None);

        roster.Should().NotBeNull();
        roster!.RosterVersion.Should().Be(2);
        roster.Entries.Select(e => e.ValidatorId).Should().BeEquivalentTo(["validator-a"]);
        roster.ResolvedFrom.Should().Contain("ctrl-remove-b");
    }

    /// <summary>
    /// The docket that SEALS a roster change is still governed by the previous set. Resolving this
    /// inclusively would report the validators who sealed every governance docket as unauthorised.
    /// </summary>
    [Fact]
    public async Task TheDocketThatSealsTheChange_IsStillGovernedByThePreviousRoster()
    {
        var resolver = ResolverOver(GenesisControlTx(), RemovalControlTx());

        var roster = await resolver.ResolveAsync(RegisterId, docketNumber: 12, CancellationToken.None);

        roster.Should().NotBeNull();
        roster!.RosterVersion.Should().Be(1,
            "docket 12 was proposed and sealed under the set in force before the change it carries");
        roster.Entries.Select(e => e.ValidatorId).Should().Contain("validator-b");
    }

    [Fact]
    public async Task TheDocketAfterTheSealingDocket_IsGovernedByTheNewRoster()
    {
        var resolver = ResolverOver(GenesisControlTx(), RemovalControlTx());

        var roster = await resolver.ResolveAsync(RegisterId, docketNumber: 13, CancellationToken.None);

        roster!.RosterVersion.Should().Be(2);
    }

    /// <summary>
    /// The genesis docket declares the validator set that seals it, so it is the one case where the
    /// control record sealed IN the docket governs that docket.
    /// </summary>
    [Fact]
    public async Task GenesisDocket_ResolvesTheGenesisRoster()
    {
        var resolver = ResolverOver(GenesisControlTx(), RemovalControlTx());

        var roster = await resolver.ResolveAsync(RegisterId, docketNumber: 0, CancellationToken.None);

        roster.Should().NotBeNull();
        roster!.RosterVersion.Should().Be(1);
    }

    [Fact]
    public async Task NoControlTransactions_ResolvesToNull_SoTheCheckReportsUnverified()
    {
        var resolver = ResolverOver();

        var roster = await resolver.ResolveAsync(RegisterId, docketNumber: 5, CancellationToken.None);

        roster.Should().BeNull("an unresolvable roster must reach the engine as null, which it renders Unverified");
    }

    /// <summary>
    /// A control transaction carrying governance attestations but no validator section says nothing
    /// about who may sign dockets. Treating it as the applicable roster would empty the set and turn
    /// every signature into a failure.
    /// </summary>
    [Fact]
    public async Task ControlTransactionWithNoValidatorSection_IsSkipped()
    {
        var noValidators = ControlTx(txId: "ctrl-attestations-only", docketNumber: 6, version: 9);
        noValidators.Payloads =
        [
            new PayloadModel
            {
                Data = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new ControlTransactionPayload
                {
                    Version = 1,
                    Roster = new RegisterControlRecord
                    {
                        RegisterId = RegisterId,
                        Name = "test",
                        Attestations =
                        [
                            new RegisterAttestation { Role = RegisterRole.Owner, Subject = "owner-wallet" },
                        ],
                        Validators = null,
                    },
                })),
            },
        ];

        var resolver = ResolverOver(GenesisControlTx(), noValidators);

        var roster = await resolver.ResolveAsync(RegisterId, docketNumber: 10, CancellationToken.None);

        roster!.RosterVersion.Should().Be(1, "the attestation-only control transaction carries no validator set");
    }

    /// <summary>
    /// A rotated key is what a historical docket was actually signed with — the register's own
    /// <c>ValidatorKeyStatus.Rotated</c> documentation says it "can still verify historical dockets".
    /// Dropping it here would produce a false failure on every register that has rotated a key.
    /// </summary>
    [Fact]
    public async Task RotatedKeys_StillHeldAuthority_ButRevokedAndEjectedDidNot()
    {
        var mixed = ControlTx(
            txId: "ctrl-mixed",
            docketNumber: 3,
            version: 4,
            (ValidatorId: "active", Key: KeyA, Status: ValidatorKeyStatus.Active),
            (ValidatorId: "rotated", Key: KeyB, Status: ValidatorKeyStatus.Rotated),
            (ValidatorId: "revoked", Key: "cmV2b2tlZA==", Status: ValidatorKeyStatus.Revoked));

        var resolver = ResolverOver(mixed);

        var roster = await resolver.ResolveAsync(RegisterId, docketNumber: 20, CancellationToken.None);

        roster!.Entries.Single(e => e.ValidatorId == "active").HeldAuthority.Should().BeTrue();
        roster.Entries.Single(e => e.ValidatorId == "rotated").HeldAuthority.Should().BeTrue();
        roster.Entries.Single(e => e.ValidatorId == "revoked").HeldAuthority.Should().BeFalse();
    }

    /// <summary>
    /// A control transaction not yet sealed into a docket has no position in history, so it cannot
    /// be said to apply from any point. Including it would let an unsealed proposal silently govern
    /// the whole register.
    /// </summary>
    [Fact]
    public async Task UnsealedControlTransaction_IsIgnored()
    {
        var unsealed = RemovalControlTx();
        unsealed.DocketNumber = null;

        var resolver = ResolverOver(GenesisControlTx(), unsealed);

        var roster = await resolver.ResolveAsync(RegisterId, docketNumber: 20, CancellationToken.None);

        roster!.RosterVersion.Should().Be(1, "an unsealed control transaction has no position in history");
    }

    /// <summary>
    /// The resolver must never be the thing that breaks the view. A repository fault becomes null —
    /// which the engine renders Unverified with a reason — rather than an exception (FR-020).
    /// </summary>
    [Fact]
    public async Task RepositoryFault_ResolvesToNull_RatherThanThrowing()
    {
        var repo = new Mock<IReadOnlyRegisterRepository>();
        repo.Setup(r => r.GetTransactionsByTypeAsync(
                It.IsAny<string>(), It.IsAny<TransactionType>(), It.IsAny<TransactionSort>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unavailable"));

        var resolver = new RosterAsOfResolver(repo.Object, NullLogger<RosterAsOfResolver>.Instance);

        var roster = await resolver.ResolveAsync(RegisterId, docketNumber: 1, CancellationToken.None);

        roster.Should().BeNull();
    }
}
