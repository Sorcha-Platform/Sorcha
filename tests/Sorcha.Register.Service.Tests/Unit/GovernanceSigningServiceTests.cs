// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Wallet.Contracts.Constants;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// Feature 189 (T020) — governance control transactions are signed by an ORGANISATION on the
/// register's roster, at slot 100, not by the node's system wallet.
/// </summary>
public class GovernanceSigningServiceTests
{
    private const string RegisterId = "abc123def456abc123def456abc123de";
    private const string OwnerWallet = "ws11qowner00000000000000000000000000000000000000000000000000";
    private const string AdminWallet = "ws11qadmin00000000000000000000000000000000000000000000000000";

    /// <summary>The system register's ceremony-minted Owner subject (US4). Carries no wallet address.</summary>
    private const string GenesisSubject = "did:sorcha:genesis:a3dd941ff5c9cd5a7c0fe16d9b5e08ca";
    private const string ValidatorId = "local-validator";
    private const string SystemWallet = "ws11qsystem0000000000000000000000000000000000000000000000000";

    private readonly Mock<IGovernanceRosterService> _rosterMock = new();
    private readonly Mock<IWalletServiceClient> _walletMock = new();
    private readonly GovernanceSigningService _sut;

    private string? _capturedWallet;
    private string? _capturedDerivationPath;
    private bool? _capturedPreHashed;
    private byte[]? _capturedData;

    public GovernanceSigningServiceTests()
    {
        _walletMock
            .Setup(x => x.SignTransactionAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback((string addr, byte[] data, string? path, bool pre, CancellationToken _) =>
            {
                _capturedWallet = addr;
                _capturedData = data;
                _capturedDerivationPath = path;
                _capturedPreHashed = pre;
            })
            .ReturnsAsync(new WalletSignResult
            {
                Signature = [1, 2, 3, 4],
                PublicKey = [5, 6, 7, 8],
                Algorithm = "ED25519",
                SignedBy = OwnerWallet
            });

        _walletMock
            .Setup(x => x.CreateOrRetrieveSystemWalletAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemWallet);

        _sut = new GovernanceSigningService(
            _rosterMock.Object,
            _walletMock.Object,
            new SystemWalletSigningOptions { ValidatorId = ValidatorId },
            Mock.Of<ILogger<GovernanceSigningService>>());
    }

    private void SetRoster(params (string subject, RegisterRole role)[] members)
    {
        _rosterMock.Setup(x => x.GetCurrentRosterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminRoster
            {
                RegisterId = RegisterId,
                ControlRecord = new RegisterControlRecord
                {
                    RegisterId = RegisterId,
                    Name = "Test",
                    CreatedAt = DateTimeOffset.UtcNow,
                    Attestations = members.Select(m => new RegisterAttestation
                    {
                        Role = m.role,
                        Subject = m.subject,
                        PublicKey = Convert.ToBase64String(new byte[32]),
                        Signature = Convert.ToBase64String(new byte[64]),
                        Algorithm = SignatureAlgorithm.ED25519,
                        GrantedAt = DateTimeOffset.UtcNow
                    }).ToList()
                },
                ControlTransactionCount = 1
            });
    }

    // ---- US4: the system register's ceremony-minted Owner ---------------------------------------

    /// <summary>
    /// The options must be taken as the CONCRETE type, because that is the only registration that
    /// actually carries a validator id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a live defect, caught on n1, written down so it cannot come back.</b>
    /// <c>AddSystemWalletSigning</c> registers a bare instance —
    /// <c>services.AddSingleton(options)</c> — not <c>Configure&lt;T&gt;</c>. Asking for
    /// <c>IOptions&lt;SystemWalletSigningOptions&gt;</c> therefore resolves a <b>default-constructed</b>
    /// one whose <c>ValidatorId</c> is null. <c>required</c> is a compile-time promise about object
    /// initialisers; it does not make the options system produce a configured instance.
    /// </para>
    /// <para>
    /// The consequence was invisible rather than loud: a null validator id makes the Wallet Service
    /// hand back the <c>default-validator</c> wallet, which exists, is healthy, and is entirely the
    /// wrong key. Only the roster-key guard turned it into a diagnosable failure instead of a
    /// signature the Validator would refuse for reasons naming neither cause.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheServiceTakesTheConcreteOptions_NotIOptions()
    {
        var parameter = typeof(GovernanceSigningService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Single(p => p.ParameterType.Name.Contains("SystemWalletSigningOptions"));

        parameter.ParameterType.Should().Be<SystemWalletSigningOptions>(
            "AddSystemWalletSigning registers a bare singleton, so IOptions<> resolves a "
            + "default-constructed instance with a null ValidatorId — and a null validator id "
            + "silently selects the wrong system wallet");
    }

    /// <summary>
    /// Sets a roster whose attestation public key is the one the wallet mock actually returns, so a
    /// signature produced through it genuinely matches what the roster records.
    /// </summary>
    /// <remarks>
    /// The shared <see cref="SetRoster"/> uses a zeroed placeholder key, which is fine for the tests
    /// that only assert which derivation path was used — but the genesis path asserts the resulting
    /// key IS the roster's, so it needs a fixture where that can actually be true.
    /// </remarks>
    private void SetGenesisRoster(byte[]? attestationKey = null)
    {
        _rosterMock.Setup(x => x.GetCurrentRosterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminRoster
            {
                RegisterId = RegisterId,
                ControlRecord = new RegisterControlRecord
                {
                    RegisterId = RegisterId,
                    Name = "System Register",
                    CreatedAt = DateTimeOffset.UtcNow,
                    Attestations =
                    [
                        new RegisterAttestation
                        {
                            Role = RegisterRole.Owner,
                            Subject = GenesisSubject,
                            // What the ceremony records: the slot-102 docket-signing key.
                            PublicKey = Convert.ToBase64String(attestationKey ?? [5, 6, 7, 8]),
                            Signature = string.Empty,
                            Algorithm = SignatureAlgorithm.ED25519,
                            GrantedAt = DateTimeOffset.UtcNow
                        }
                    ]
                },
                ControlTransactionCount = 1
            });
    }

    /// <summary>
    /// The system register is governable: its ceremony-minted Owner resolves to the node's system
    /// wallet and signs at <c>sorcha:docket-signing</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Before this, a propose against the system register threw — "carries no wallet address … so
    /// there is no key to sign with" — because the subject is <c>did:sorcha:genesis:{fingerprint}</c>
    /// and the resolver only understood <c>did:sorcha:w:{address}</c>.
    /// </para>
    /// <para>
    /// The key was never missing. The ceremony derives everything from one BIP39 mnemonic and records
    /// the <b>slot-102</b> key on the Owner attestation; `import-validator-key` recovers that mnemonic
    /// into the system wallet, which is the same wallet the validator seals dockets with. So the fix
    /// is a resolution path, not a re-genesis — which is what keeps the trust anchor untouched.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task SignAsync_ForTheGenesisSubject_UsesTheSystemWalletAtDocketSigning()
    {
        SetGenesisRoster();

        await _sut.SignAsync(RegisterId, "tx-1", "hash-1");

        _capturedWallet.Should().Be(SystemWallet,
            "the ceremony subject carries no address, so the wallet comes from the configured validator id");
        _capturedDerivationPath.Should().Be(SorchaDerivationPaths.DocketSigning,
            "slot 102 is the key the ceremony recorded on the Owner attestation — slot 100 would "
            + "produce a signature the Validator refuses");
        _capturedPreHashed.Should().BeTrue();
    }

    /// <summary>
    /// The genesis branch is confined to the genesis subject: an ordinary organisation still signs at
    /// slot 100 from its own wallet.
    /// </summary>
    /// <remarks>
    /// Without this, the fix would be indistinguishable from "governance may sign at any derivation
    /// path", which would let any subject sign with a key its attestation does not record.
    /// </remarks>
    [Fact]
    public async Task SignAsync_ForAnOrdinarySubject_IsUnchangedByTheGenesisBranch()
    {
        SetRoster(($"did:sorcha:w:{OwnerWallet}", RegisterRole.Owner));

        await _sut.SignAsync(RegisterId, "tx-1", "hash-1");

        _capturedWallet.Should().Be(OwnerWallet, "the address comes from the subject, not from config");
        _capturedDerivationPath.Should().Be(SorchaDerivationPaths.RegisterAttestation);
    }

    /// <summary>
    /// A signature whose key is not the one the roster records is refused here, not left for the
    /// Validator to reject opaquely.
    /// </summary>
    /// <remarks>
    /// The genesis path resolves its wallet from <c>SystemWalletSigning:ValidatorId</c> rather than
    /// from the subject, so a misconfigured id silently selects a different — perfectly valid —
    /// wallet. The signature then fails authorisation far from the cause, and the message names the
    /// roster rather than the configuration. This turns that into a diagnosable failure at the point
    /// the wrong key is produced.
    /// </remarks>
    [Fact]
    public async Task SignAsync_ForTheGenesisSubject_RefusesAKeyTheRosterDoesNotRecord()
    {
        // The wallet mock returns [5,6,7,8]; the roster records something else — the shape a
        // mismatched ValidatorId produces.
        SetGenesisRoster(attestationKey: [9, 9, 9, 9]);

        var act = async () => await _sut.SignAsync(RegisterId, "tx-1", "hash-1");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*SystemWalletSigning:ValidatorId*",
                "the message must name the configuration that is actually wrong");
    }

    /// <summary>
    /// T058 — once ownership transfers away, the ceremony subject cannot sign again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The genesis branch is reached only for an attestation the roster actually carries, because
    /// <c>SelectSigner</c> runs first. That ordering is the whole safety of it: a subject removed by
    /// a transfer simply stops being selectable, and the branch never fires.
    /// </para>
    /// <para>
    /// Worth pinning rather than assuming. Had the implementation matched on the subject before
    /// consulting the roster — the obvious shortcut, since the subject is a constant — a former owner
    /// would go on signing with the node's system wallet forever, and the transfer that US4 exists to
    /// perform would change nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task SignAsync_AfterTransfer_TheFormerGenesisOwnerCanNoLongerSign()
    {
        // The roster as it stands after the transfer: a real organisation, no ceremony subject.
        SetRoster(($"did:sorcha:w:{OwnerWallet}", RegisterRole.Owner));

        var act = () => _sut.SignDigestAsync(RegisterId, [1, 2, 3], preferredSubject: GenesisSubject);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*governance role*",
                "the former owner is not on the roster, so there is no authority to sign as");

        _walletMock.Verify(x => x.CreateOrRetrieveSystemWalletAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "a subject that is off the roster must never reach the system wallet");
    }

    [Fact]
    public async Task SignAsync_SignsAtSlot100_NotThePrimaryKey()
    {
        SetRoster(($"did:sorcha:w:{OwnerWallet}", RegisterRole.Owner));

        await _sut.SignAsync(RegisterId, "tx-1", "hash-1");

        // The roster records the slot-100 key. Signing with the wallet's primary key (which is what
        // a null derivation path selects) reproduces the original "submitter not found in roster".
        _capturedDerivationPath.Should().Be(SorchaDerivationPaths.RegisterAttestation);
        _capturedPreHashed.Should().BeTrue();
    }

    [Fact]
    public async Task SignAsync_ResolvesWalletAddressFromRosterSubject()
    {
        SetRoster(($"did:sorcha:w:{OwnerWallet}", RegisterRole.Owner));

        var result = await _sut.SignAsync(RegisterId, "tx-1", "hash-1");

        _capturedWallet.Should().Be(OwnerWallet);
        result.WalletAddress.Should().Be(OwnerWallet);
        result.Subject.Should().Be($"did:sorcha:w:{OwnerWallet}");
    }

    [Fact]
    public async Task SignAsync_SignsTheBytesTheValidatorRecomputes()
    {
        SetRoster(($"did:sorcha:w:{OwnerWallet}", RegisterRole.Owner));

        await _sut.SignAsync(RegisterId, "tx-abc", "hash-def");

        // VerifySignaturesAsync recomputes SHA-256(UTF-8("{TxId}:{PayloadHash}")). Diverging here
        // yields a signature that verifies nowhere, and a rejection naming neither cause.
        var expected = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("tx-abc:hash-def"));
        _capturedData.Should().Equal(expected);
    }

    [Fact]
    public async Task SignAsync_PrefersOwnerOverAdmin()
    {
        SetRoster(
            ($"did:sorcha:w:{AdminWallet}", RegisterRole.Admin),
            ($"did:sorcha:w:{OwnerWallet}", RegisterRole.Owner));

        var result = await _sut.SignAsync(RegisterId, "tx-1", "hash-1");

        result.WalletAddress.Should().Be(OwnerWallet);
    }

    [Fact]
    public async Task SignAsync_PreferredSubject_SignsAsThatOrganisation()
    {
        SetRoster(
            ($"did:sorcha:w:{OwnerWallet}", RegisterRole.Owner),
            ($"did:sorcha:w:{AdminWallet}", RegisterRole.Admin));

        // Consortium approvals: each organisation signs its own, so the caller names the signer.
        var result = await _sut.SignAsync(RegisterId, "tx-1", "hash-1",
            preferredSubject: $"did:sorcha:w:{AdminWallet}");

        result.WalletAddress.Should().Be(AdminWallet);
    }

    [Fact]
    public async Task SignAsync_NoRoster_ThrowsWithAnActionableReason()
    {
        _rosterMock.Setup(x => x.GetCurrentRosterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdminRoster?)null);

        var act = () => _sut.SignAsync(RegisterId, "tx-1", "hash-1");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*no governance roster*");
    }

    [Fact]
    public async Task SignAsync_NoGovernanceRoleOnRoster_Throws()
    {
        SetRoster(($"did:sorcha:w:{OwnerWallet}", RegisterRole.Auditor));

        var act = () => _sut.SignAsync(RegisterId, "tx-1", "hash-1");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*governance role*");
        _walletMock.Verify(x => x.SignTransactionAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// A subject in a shape the resolver does not understand still fails clearly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This test used to pin the opposite claim, for <c>did:sorcha:genesis:</c> specifically</b> —
    /// that the system register's ceremony-minted subject had no key to sign with, which was true and
    /// was why US4 existed. US4 resolved it (see
    /// <see cref="SignAsync_ForTheGenesisSubject_UsesTheSystemWalletAtDocketSigning"/>), so keeping
    /// that assertion would have pinned a limitation the platform no longer has.
    /// </para>
    /// <para>
    /// The half worth keeping is that the resolver is a closed set: exactly two subject shapes are
    /// understood, and anything else fails loudly rather than falling through to some default wallet.
    /// That is what stops the US4 branch from generalising into "governance may sign as anything".
    /// </para>
    /// </remarks>
    [Fact]
    public async Task SignAsync_SubjectInAnUnrecognisedShape_ThrowsClearly()
    {
        SetRoster(("did:example:not-a-shape-this-resolver-knows", RegisterRole.Owner));

        var act = () => _sut.SignAsync(RegisterId, "tx-1", "hash-1");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*carries no wallet address*");

        _walletMock.Verify(x => x.SignTransactionAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never,
            "an unrecognised subject must not quietly sign with somebody else's wallet");
    }
}
