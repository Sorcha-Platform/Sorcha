// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Engine.Credentials.Sources;
using Sorcha.Blueprint.Models.Credentials;

using Factory = Sorcha.Blueprint.Engine.Tests.Credentials.EngineSdJwtTestFactory;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

/// <summary>
/// Feature 135 / T021 — default-policy synthesis (FR-026) and offline pinned re-evaluation (SC-005).
/// </summary>
public class TrustPolicyDefaultsTests
{
    private static TrustEvaluator RegisterEvaluator() =>
        new(new TrustResolverRegistry([new RegisterTrustSourceResolver(new Factory.AlwaysResolvesDirectory())]));

    private static IssuerContext SignedIssuer(string id = "did:sorcha:org:abc") =>
        new() { IssuerId = id, SignatureVerified = true, Format = CredentialFormat.SdJwtVc };

    // ---- default-policy synthesis (FR-026) -----------------------------------

    [Fact]
    public void FromLegacyIssuers_WithIssuers_BuildsDidAllowlistSource()
    {
        var policy = TrustPolicyExtensions.FromLegacyIssuers(["did:sorcha:org:gov", "did:sorcha:org:gov"]);

        policy.Sources.Should().ContainSingle();
        policy.Sources[0].Kind.Should().Be(TrustSourceKind.DidAllowlist);
        policy.Sources[0].AllowedIssuers.Should().BeEquivalentTo(["did:sorcha:org:gov"]);
        policy.Combinator.Should().Be(TrustCombinator.AnyOf);
        policy.MinAssuranceLevel.Should().Be(AssuranceLevel.Low);
    }

    [Fact]
    public void FromLegacyIssuers_Empty_BuildsRegisterSourceAtLow()
    {
        var policy = TrustPolicyExtensions.FromLegacyIssuers(null);

        policy.Sources.Should().ContainSingle();
        policy.Sources[0].Kind.Should().Be(TrustSourceKind.Register);
        policy.MinAssuranceLevel.Should().Be(AssuranceLevel.Low);
    }

    [Fact]
    public async Task NullPolicy_DefaultsToRegisterAtLow_AndAccepts()
    {
        var decision = await RegisterEvaluator().EvaluateAsync(SignedIssuer(), policy: null);

        decision.IsTrusted.Should().BeTrue();
        decision.Evidence.VouchingSource.Should().Be(TrustSourceKind.Register);
        decision.EstablishedAssurance.Should().Be(AssuranceLevel.Low);
    }

    // ---- offline pinned re-evaluation (SC-005) -------------------------------

    [Fact]
    public async Task PinnedPolicy_ReEvaluatedOffline_YieldsSameDecisionAndDigest()
    {
        var policy = new TrustPolicy
        {
            Sources = [new TrustSourceRef { Kind = TrustSourceKind.Register, ConfersAssurance = AssuranceLevel.Substantial }],
            Combinator = TrustCombinator.AnyOf,
            MinAssuranceLevel = AssuranceLevel.Low
        };
        var issuer = SignedIssuer("did:sorcha:org:pinned");

        // First (online-ish) evaluation pins the policy digest into the evidence.
        var first = await RegisterEvaluator().EvaluateAsync(issuer, policy);

        // A second, independent evaluator (offline, in-memory directory) using the SAME pinned
        // policy reaches the same decision + evidence — the basis for offline re-verification.
        var second = await RegisterEvaluator().EvaluateAsync(issuer, policy);

        first.IsTrusted.Should().BeTrue();
        second.IsTrusted.Should().Be(first.IsTrusted);
        second.EstablishedAssurance.Should().Be(first.EstablishedAssurance);
        second.Evidence.PolicyDigest.Should().Be(first.Evidence.PolicyDigest);
        first.Evidence.PolicyDigest.Should().Be(TrustEvaluator.ComputePolicyDigest(policy));
    }
}
