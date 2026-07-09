// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

/// <summary>
/// US3 (feature 177) — the scoped, opt-in reduced-assurance (Warn) fallback for a signature-valid
/// issuer that no trust source vouches for. Default OFF = fail-closed preserved.
/// </summary>
public class TrustEvaluatorWarnTests
{
    private sealed class DeclineOrVouchResolver : ITrustSourceResolver
    {
        public required TrustSourceKind Kind { get; init; }
        public bool Vouches { get; init; }

        public Task<TrustSourceVouch> VouchAsync(IssuerContext issuer, TrustSourceRef source, CancellationToken ct)
            => Task.FromResult(Vouches
                ? new TrustSourceVouch { Vouched = true, Assurance = AssuranceLevel.Low, ApplyEvidence = e => e.IssuerId = issuer.IssuerId }
                : TrustSourceVouch.Decline(TrustFailureReason.UntrustedIssuer));
    }

    private static IssuerContext SignedIssuer(string id = "did:key:zQ3s-eth") =>
        new() { IssuerId = id, SignatureVerified = true, Format = CredentialFormat.SdJwtVc };

    private static TrustEvaluator Evaluator(bool vouches)
        => new(new TrustResolverRegistry([new DeclineOrVouchResolver { Kind = TrustSourceKind.DidAllowlist, Vouches = vouches }]), statusChecker: null);

    private static TrustPolicy AllowlistPolicy(bool warn) =>
        new()
        {
            Combinator = TrustCombinator.AnyOf,
            MinAssuranceLevel = AssuranceLevel.Low,
            Sources = [new TrustSourceRef { Kind = TrustSourceKind.DidAllowlist }],
            WarnOnUnlistedVerifiedIssuer = warn
        };

    [Fact]
    public async Task UnlistedIssuer_FlagOff_Rejects_FailClosed()
    {
        var decision = await Evaluator(vouches: false).EvaluateAsync(SignedIssuer(), AllowlistPolicy(warn: false));

        decision.IsTrusted.Should().BeFalse();
        decision.ReducedAssurance.Should().BeFalse();
        decision.SignatureValid.Should().BeTrue();
    }

    [Fact]
    public async Task UnlistedIssuer_FlagOn_AcceptsAtReducedAssuranceWarn()
    {
        var decision = await Evaluator(vouches: false).EvaluateAsync(SignedIssuer(), AllowlistPolicy(warn: true));

        decision.IsTrusted.Should().BeTrue();
        decision.ReducedAssurance.Should().BeTrue();
        decision.EstablishedAssurance.Should().Be(AssuranceLevel.None);
        decision.SignatureValid.Should().BeTrue();
    }

    [Fact]
    public async Task VouchedIssuer_FlagOn_IsFullTrustNotWarn()
    {
        var decision = await Evaluator(vouches: true).EvaluateAsync(SignedIssuer(), AllowlistPolicy(warn: true));

        decision.IsTrusted.Should().BeTrue();
        decision.ReducedAssurance.Should().BeFalse();
        decision.EstablishedAssurance.Should().NotBe(AssuranceLevel.None);
    }

    [Fact]
    public async Task UnverifiedSignature_FlagOn_StillRejects()
    {
        // The Warn path never fires on an unverified signature (Step 0 fail-closed precondition).
        var issuer = SignedIssuer();
        issuer.SignatureVerified = false;

        var decision = await Evaluator(vouches: false).EvaluateAsync(issuer, AllowlistPolicy(warn: true));

        decision.IsTrusted.Should().BeFalse();
        decision.ReducedAssurance.Should().BeFalse();
        decision.FailureReason.Should().Be(TrustFailureReason.SignatureInvalid);
    }

    [Fact]
    public void PolicyDigest_ChangesWhenWarnFlagFlips()
    {
        TrustEvaluator.ComputePolicyDigest(AllowlistPolicy(warn: false))
            .Should().NotBe(TrustEvaluator.ComputePolicyDigest(AllowlistPolicy(warn: true)));
    }

    // ── Feature 178 (US2) — the same governance applies to address-form issuer DIDs. The trust
    // evaluator sees only the issuer id + SignatureVerified, so a did:pkh / did:ethr issuer whose
    // ES256K signature was verified by recovery is governed identically to any other verified issuer. ──

    [Theory]
    [InlineData("did:pkh:eip155:1:0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B")]
    [InlineData("did:ethr:0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B")]
    public async Task AddressFormIssuer_Unlisted_FlagOff_Rejects_FailClosed(string issuerDid)
    {
        var decision = await Evaluator(vouches: false).EvaluateAsync(SignedIssuer(issuerDid), AllowlistPolicy(warn: false));

        decision.IsTrusted.Should().BeFalse();
        decision.ReducedAssurance.Should().BeFalse();
        decision.SignatureValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("did:pkh:eip155:1:0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B")]
    [InlineData("did:ethr:0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B")]
    public async Task AddressFormIssuer_Unlisted_FlagOn_AcceptsAtReducedAssuranceWarn(string issuerDid)
    {
        var decision = await Evaluator(vouches: false).EvaluateAsync(SignedIssuer(issuerDid), AllowlistPolicy(warn: true));

        decision.IsTrusted.Should().BeTrue();
        decision.ReducedAssurance.Should().BeTrue();
        decision.EstablishedAssurance.Should().Be(AssuranceLevel.None);
    }

    [Fact]
    public async Task AddressFormIssuer_Allowlisted_IsFullTrustNotWarn()
    {
        var decision = await Evaluator(vouches: true)
            .EvaluateAsync(SignedIssuer("did:pkh:eip155:1:0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B"), AllowlistPolicy(warn: true));

        decision.IsTrusted.Should().BeTrue();
        decision.ReducedAssurance.Should().BeFalse();
        decision.EstablishedAssurance.Should().NotBe(AssuranceLevel.None);
    }
}
