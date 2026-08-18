// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Sorcha.Register.Core.Services;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Wallet.Contracts.Constants;

using Xunit;

namespace Sorcha.Register.Service.Tests.Governance;

/// <summary>
/// Issue #1465 — the system register's ceremony-minted Owner can produce the organisation half of
/// its own approval, and nobody else can.
/// </summary>
/// <remarks>
/// <para>
/// The SSR's roster has one member: the ceremony subject <c>did:sorcha:genesis:…</c>, whose recorded
/// key IS this node's system wallet. US4 gave that subject a way to SIGN a control transaction but
/// not to APPROVE one, and <c>Transfer</c> is deliberately outside the Owner override — so the SSR
/// could raise a proposal it could never approve. The only external route would be
/// <c>POST /wallets/{addr}/sign</c> against the system wallet, which #1397 correctly refuses.
/// </para>
/// <para>
/// R-014's rule is that the server must not hold a MULTI-PARTY register's key. The SSR at genesis is
/// single-member and the key is the node's own, so this does not breach it — but the scope has to be
/// exactly that one subject, which is what these tests pin.
/// </para>
/// </remarks>
public class GenesisOwnerApprovalTests
{
    private const string RegisterId = "0123456789abcdef0123456789abcdef";
    private const string GenesisSubject = "did:sorcha:genesis:a3dd941f";
    private const string OrgSubject = "did:sorcha:w:ws11qexampleorgaddress";
    private const string SystemWallet = "ws11qsystemwalletaddress";

    private static readonly byte[] SigningKey = [.. Enumerable.Repeat((byte)0xAB, 32)];
    private static readonly byte[] Signature = [.. Enumerable.Repeat((byte)0xCD, 64)];

    private static GovernanceOperation Operation() => new()
    {
        OperationType = GovernanceOperationType.Transfer,
        TargetDid = OrgSubject,
        TargetRole = RegisterRole.Owner,
        ProposerDid = GenesisSubject,
        ProposedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static GovernanceSigningService BuildService()
    {
        var roster = new Mock<IGovernanceRosterService>();
        roster.Setup(r => r.GetCurrentRosterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminRoster
            {
                ControlRecord = new RegisterControlRecord
                {
                    RegisterId = RegisterId,
                    Attestations =
                    [
                        new RegisterAttestation
                        {
                            Subject = GenesisSubject,
                            Role = RegisterRole.Owner,
                            // The roster records the slot-102 docket-signing key — the same key the
                            // wallet below returns, which is what makes the match succeed.
                            PublicKey = Convert.ToBase64String(SigningKey),
                            Algorithm = SignatureAlgorithm.ED25519,
                        },
                    ],
                },
            });

        var wallet = new Mock<IWalletServiceClient>();
        wallet.Setup(w => w.CreateOrRetrieveSystemWalletAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemWallet);
        wallet.Setup(w => w.SignTransactionAsync(
                SystemWallet, It.IsAny<byte[]>(), SorchaDerivationPaths.DocketSigning, true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletSignResult
            {
                Signature = Signature,
                PublicKey = SigningKey,
                Algorithm = "ED25519",
                SignedBy = SystemWallet,
            });

        return new GovernanceSigningService(
            roster.Object, wallet.Object,
            new SystemWalletSigningOptions { ValidatorId = "local-validator" },
            NullLogger<GovernanceSigningService>.Instance);
    }

    [Fact]
    public async Task TheCeremonyOwnerCanProduceTheOrganisationHalfOfItsOwnApproval()
    {
        var result = await BuildService().TrySignApprovalAsync(
            RegisterId, Operation(), GenesisSubject, isApproval: true);

        result.Should().NotBeNull(
            "the SSR's sole roster member must be able to approve, or it can raise proposals it can never enact");
        result!.Subject.Should().Be(GenesisSubject);
        result.Signature.Should().Equal(Signature);
    }

    [Fact]
    public async Task ItSignsTheSAMEDigestTheVerifierWillRebuild()
    {
        // The load-bearing assertion. A producer that signs anything other than the canonical
        // statement yields a signature that verifies nowhere — and the failure surfaces as an
        // opaque authorisation refusal a long way from the cause. Capture what was actually sent to
        // the wallet and compare it to the shared builder's output.
        byte[]? signedDigest = null;

        var roster = new Mock<IGovernanceRosterService>();
        roster.Setup(r => r.GetCurrentRosterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminRoster
            {
                ControlRecord = new RegisterControlRecord
                {
                    RegisterId = RegisterId,
                    Attestations =
                    [
                        new RegisterAttestation
                        {
                            Subject = GenesisSubject,
                            Role = RegisterRole.Owner,
                            PublicKey = Convert.ToBase64String(SigningKey),
                            Algorithm = SignatureAlgorithm.ED25519,
                        },
                    ],
                },
            });

        var wallet = new Mock<IWalletServiceClient>();
        wallet.Setup(w => w.CreateOrRetrieveSystemWalletAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemWallet);
        wallet.Setup(w => w.SignTransactionAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Callback<string, byte[], string?, bool, CancellationToken>((_, d, _, _, _) => signedDigest = d)
            .ReturnsAsync(new WalletSignResult
            {
                Signature = Signature, PublicKey = SigningKey, Algorithm = "ED25519",
                SignedBy = SystemWallet,
            });

        var service = new GovernanceSigningService(
            roster.Object, wallet.Object,
            new SystemWalletSigningOptions { ValidatorId = "local-validator" },
            NullLogger<GovernanceSigningService>.Instance);

        var operation = Operation();
        await service.TrySignApprovalAsync(RegisterId, operation, GenesisSubject, isApproval: true);

        var expected = GovernanceApprovalStatement.ComputeDigest(
            RegisterId, operation, GenesisSubject, isApproval: true);

        signedDigest.Should().Equal(expected,
            "the digest signed here must be the one the verifier rebuilds from stored content — two "
            + "canonicalisations of one statement is how a producer and a verifier come to disagree");
    }

    [Fact]
    public async Task ApprovingAndRejectingProduceDifferentDigests()
    {
        // The approve/reject bit is inside the signed statement, so a signature captured for one
        // must not verify as the other.
        var operation = Operation();

        var approve = GovernanceApprovalStatement.ComputeDigest(RegisterId, operation, GenesisSubject, true);
        var reject = GovernanceApprovalStatement.ComputeDigest(RegisterId, operation, GenesisSubject, false);

        approve.Should().NotEqual(reject);
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData(OrgSubject)]
    [InlineData("did:sorcha:org:someorg")]
    [InlineData("did:web:example.com")]
    public async Task TheServerRefusesToSignForAnyOtherSubject(string subject)
    {
        // The scope IS the security property. A general "sign for any roster member" here would
        // recreate GovernanceApprovalService, which R-014 removed precisely so the server cannot
        // approve on an organisation's behalf.
        var result = await BuildService().TrySignApprovalAsync(
            RegisterId, Operation(), subject, isApproval: true);

        result.Should().BeNull($"the server must never produce an approval as '{subject}'");
    }
}
