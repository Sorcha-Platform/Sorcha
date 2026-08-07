// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Cryptography;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Utilities;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Services;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// Verification of a detached approval, using REAL keys and REAL signatures.
/// </summary>
/// <remarks>
/// Fabricated signatures would prove nothing here — the whole subject is whether a signature means
/// what it claims. Every key below is generated, every signature actually produced, and the DIDs are
/// derived from the keys rather than written by hand.
/// </remarks>
public sealed class DetachedApprovalVerifierTests
{
    private const string RegisterId = "reg-1";
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly ICryptoModule _crypto = new CryptoModule();
    private readonly IWalletUtilities _wallets = new WalletUtilities();

    private DetachedApprovalVerifier Verifier() =>
        new(_crypto, _wallets, NullLogger<DetachedApprovalVerifier>.Instance);

    private sealed record Identity(byte[] PrivateKey, string PublicKeyB64, string Did);

    private async Task<Identity> NewIdentityAsync()
    {
        var keySet = (await _crypto.GenerateKeySetAsync(WalletNetworks.ED25519)).Value!;
        var pub = keySet.PublicKey.Key!;
        var address = _wallets.PublicKeyToWallet(pub, (byte)WalletNetworks.ED25519)!;

        return new Identity(keySet.PrivateKey.Key!, Convert.ToBase64String(pub), $"did:sorcha:w:{address}");
    }

    private async Task<string> SignAsync(byte[] digest, Identity id)
    {
        var result = await _crypto.SignAsync(digest, (byte)WalletNetworks.ED25519, id.PrivateKey);
        return Convert.ToBase64String(result.Value!);
    }

    private static GovernanceOperation Operation(
        GovernanceOperationType type = GovernanceOperationType.CryptoPolicyUpdate) => new()
    {
        OperationType = type,
        ProposerDid = "did:sorcha:w:ws11qproposer",
        ProposedAt = Now.AddHours(-1),
    };

    /// <summary>A fully valid direct approval: the person signs with their own key.</summary>
    private async Task<(GovernanceApprovalSubmission, Identity org, Identity person)> DirectAsync(
        GovernanceOperation operation)
    {
        var org = await NewIdentityAsync();
        var person = await NewIdentityAsync();

        var digest = GovernanceApprovalStatement.ComputeDigest(RegisterId, operation, org.Did, true);

        var submission = new GovernanceApprovalSubmission
        {
            RequestId = "req-1",
            ApproverDid = org.Did,
            IsApproval = true,
            Signature = await SignAsync(digest, org),
            PublicKey = org.PublicKeyB64,
            AuthMethod = ApprovalAuthMethod.HardwareBacked,
            Authorisation = new ApprovalAuthorisation
            {
                Kind = AuthorisationKind.Direct,
                IndividualDid = person.Did,
                Signature = await SignAsync(digest, person),
                PublicKey = person.PublicKeyB64,
                AuthMethod = ApprovalAuthMethod.HardwareBacked,
            },
        };

        return (submission, org, person);
    }

    /// <summary>A fully valid delegated approval: a bot signs under a grant the person signed.</summary>
    private async Task<(GovernanceApprovalSubmission, Identity person)> DelegatedAsync(
        GovernanceOperation operation, GovernanceOperationType[] scope)
    {
        var org = await NewIdentityAsync();
        var person = await NewIdentityAsync();
        var bot = await NewIdentityAsync();

        var delegation = new GovernanceDelegation
        {
            DelegationId = "delegation-1",
            OrganisationDid = org.Did,
            IndividualDid = person.Did,
            ApproverPublicKey = bot.PublicKeyB64,
            Scope = new(scope),
            GrantedAt = Now.AddDays(-1),
            ExpiresAt = Now.AddDays(30),
        };

        var approvalDigest = GovernanceApprovalStatement.ComputeDigest(RegisterId, operation, org.Did, true);
        var delegationDigest = GovernanceDelegationStatement.ComputeDigest(delegation);

        var submission = new GovernanceApprovalSubmission
        {
            RequestId = "req-1",
            ApproverDid = org.Did,
            IsApproval = true,
            Signature = await SignAsync(approvalDigest, org),
            PublicKey = org.PublicKeyB64,
            AuthMethod = ApprovalAuthMethod.Service,
            Authorisation = new ApprovalAuthorisation
            {
                Kind = AuthorisationKind.Delegated,
                IndividualDid = person.Did,
                Signature = await SignAsync(approvalDigest, bot),
                PublicKey = bot.PublicKeyB64,
                AuthMethod = ApprovalAuthMethod.Service,
                Delegation = delegation,
                DelegationSignature = await SignAsync(delegationDigest, person),
                DelegationPublicKey = person.PublicKeyB64,
            },
        };

        return (submission, person);
    }

    [Fact]
    public async Task ValidDirectApproval_IsAccepted()
    {
        var operation = Operation();
        var (submission, _, person) = await DirectAsync(operation);

        var result = await Verifier().VerifyAsync(RegisterId, operation, submission, Now);

        result.Accepted.Should().BeTrue(result.Detail);
        result.AccountableIndividualDid.Should().Be(person.Did);
    }

    [Fact]
    public async Task ValidDelegatedApproval_IsAccepted_AndResolvesToThePerson()
    {
        var operation = Operation();
        var (submission, person) = await DelegatedAsync(
            operation, new[] { GovernanceOperationType.CryptoPolicyUpdate });

        var result = await Verifier().VerifyAsync(RegisterId, operation, submission, Now);

        result.Accepted.Should().BeTrue(result.Detail);
        result.AccountableIndividualDid.Should().Be(person.Did,
            "a bot's approval is still attributable to whoever empowered it");
    }

    [Fact]
    public async Task NamingSomeoneElseAsAccountable_IsRefused()
    {
        // THE check that was missing. Signing with your own key while naming a colleague as
        // accountable would otherwise produce a perfectly valid signature and a false audit record.
        var operation = Operation();
        var (submission, _, _) = await DirectAsync(operation);
        var someoneElse = await NewIdentityAsync();

        submission.Authorisation!.IndividualDid = someoneElse.Did;

        var result = await Verifier().VerifyAsync(RegisterId, operation, submission, Now);

        result.Accepted.Should().BeFalse();
        result.Detail.Should().Contain("does not belong to the individual");
    }

    [Fact]
    public async Task DelegationSignedByAnyoneOtherThanTheGrantor_IsRefused()
    {
        var operation = Operation();
        var (submission, person) = await DelegatedAsync(
            operation, new[] { GovernanceOperationType.CryptoPolicyUpdate });

        // An impostor re-signs the grant with their own key while it still names the real person.
        var impostor = await NewIdentityAsync();
        var digest = GovernanceDelegationStatement.ComputeDigest(submission.Authorisation!.Delegation!);
        submission.Authorisation.DelegationSignature = await SignAsync(digest, impostor);
        submission.Authorisation.DelegationPublicKey = impostor.PublicKeyB64;

        var result = await Verifier().VerifyAsync(RegisterId, operation, submission, Now);

        result.Accepted.Should().BeFalse();
        result.Detail.Should().Contain("delegation was not signed by a key belonging");
    }

    [Fact]
    public async Task TamperingWithTheOperationAfterSigning_IsRefused()
    {
        // The reason statement v2 binds the whole operation. Approve "add validator A", enact
        // "add validator B".
        var operation = Operation(GovernanceOperationType.AddValidator);
        operation.ValidatorEntry = new ValidatorRosterEntry { ValidatorId = "A", PublicKey = "QQ==" };

        var (submission, _, _) = await DirectAsync(operation);

        operation.ValidatorEntry = new ValidatorRosterEntry { ValidatorId = "B", PublicKey = "Qg==" };

        var result = await Verifier().VerifyAsync(RegisterId, operation, submission, Now);

        result.Accepted.Should().BeFalse("the signature covers the validator entry, not just the intent");
    }

    [Fact]
    public async Task DelegationOutsideItsScope_IsRefused()
    {
        var operation = Operation(GovernanceOperationType.Transfer);
        var (submission, _) = await DelegatedAsync(
            operation, new[] { GovernanceOperationType.CryptoPolicyUpdate });

        var result = await Verifier().VerifyAsync(RegisterId, operation, submission, Now);

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Be(AuthorisationRefusalReason.OutOfScope);
    }

    [Fact]
    public async Task RevokedDelegation_IsRefused()
    {
        var operation = Operation();
        var (submission, _) = await DelegatedAsync(
            operation, new[] { GovernanceOperationType.CryptoPolicyUpdate });

        var result = await Verifier().VerifyAsync(
            RegisterId, operation, submission, Now, isRevoked: id => id == "delegation-1");

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Be(AuthorisationRefusalReason.Revoked);
    }

    [Fact]
    public async Task ApprovalWithNoAuthorisation_IsRefused()
    {
        var operation = Operation();
        var org = await NewIdentityAsync();

        var result = await Verifier().VerifyAsync(RegisterId, operation, new GovernanceApprovalSubmission
        {
            ApproverDid = org.Did,
            PublicKey = org.PublicKeyB64,
        }, Now);

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Be(AuthorisationRefusalReason.Missing);
    }

    [Fact]
    public async Task UnsupportedDidMethod_IsRefused_NotWavedThrough()
    {
        // Failing open on a DID we cannot parse would make every other check here decorative.
        var operation = Operation();
        var (submission, _, _) = await DirectAsync(operation);
        submission.Authorisation!.IndividualDid = "did:example:123";

        var result = await Verifier().VerifyAsync(RegisterId, operation, submission, Now);

        result.Accepted.Should().BeFalse();
    }

    [Fact]
    public async Task FlippingApproveToReject_IsRefused()
    {
        // isApproval is bound, so a rejection cannot be recounted as an approval.
        var operation = Operation();
        var (submission, _, _) = await DirectAsync(operation);

        submission.IsApproval = false;

        var result = await Verifier().VerifyAsync(RegisterId, operation, submission, Now);

        result.Accepted.Should().BeFalse();
    }
}
