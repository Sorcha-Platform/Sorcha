// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

public class TrustEvaluatorTests
{
    // ---- helpers -------------------------------------------------------------

    private sealed class FakeResolver : ITrustSourceResolver
    {
        public required TrustSourceKind Kind { get; init; }
        public bool Vouches { get; init; }
        public AssuranceLevel Assurance { get; init; } = AssuranceLevel.Low;
        public TrustFailureReason DeclineReason { get; init; } = TrustFailureReason.UntrustedIssuer;

        public Task<TrustSourceVouch> VouchAsync(IssuerContext issuer, TrustSourceRef source, CancellationToken ct)
            => Task.FromResult(Vouches
                ? new TrustSourceVouch { Vouched = true, Assurance = Assurance, ApplyEvidence = e => e.IssuerId = issuer.IssuerId }
                : TrustSourceVouch.Decline(DeclineReason));
    }

    private sealed class FakeStatusChecker(CredentialStatusValue bit) : IStatusListChecker
    {
        public Task<CredentialStatusValue> CheckAsync(StatusReference statusRef, CancellationToken ct) => Task.FromResult(bit);
    }

    private static IssuerContext SignedIssuer(string id = "did:sorcha:org:test") =>
        new() { IssuerId = id, SignatureVerified = true, Format = CredentialFormat.SdJwtVc };

    private static TrustPolicy Policy(TrustCombinator combinator, AssuranceLevel min, params TrustSourceKind[] kinds) =>
        new()
        {
            Combinator = combinator,
            MinAssuranceLevel = min,
            Sources = kinds.Select(k => new TrustSourceRef { Kind = k }).ToArray()
        };

    private static TrustEvaluator Evaluator(params ITrustSourceResolver[] resolvers)
        => new(new TrustResolverRegistry(resolvers), statusChecker: null);

    private static TrustEvaluator EvaluatorWithStatus(IStatusListChecker status, params ITrustSourceResolver[] resolvers)
        => new(new TrustResolverRegistry(resolvers), status);

    // ---- signature precondition (FR-008 / SC-002) ----------------------------

    [Fact]
    public async Task EvaluateAsync_UnverifiedSignature_RejectsSignatureInvalid()
    {
        var evaluator = Evaluator( new FakeResolver { Kind = TrustSourceKind.Register, Vouches = true });
        var issuer = SignedIssuer();
        issuer.SignatureVerified = false;

        var decision = await evaluator.EvaluateAsync(issuer, Policy(TrustCombinator.AnyOf, AssuranceLevel.Low, TrustSourceKind.Register));

        decision.IsTrusted.Should().BeFalse();
        decision.SignatureValid.Should().BeFalse();
        decision.FailureReason.Should().Be(TrustFailureReason.SignatureInvalid);
    }

    // ---- combinators (SC-006) ------------------------------------------------

    [Fact]
    public async Task EvaluateAsync_AnyOf_OneSourceVouches_Accepts()
    {
        var evaluator = Evaluator(
            new FakeResolver { Kind = TrustSourceKind.Register, Vouches = false },
            new FakeResolver { Kind = TrustSourceKind.X509Tenant, Vouches = true, Assurance = AssuranceLevel.Substantial });

        var decision = await evaluator.EvaluateAsync(SignedIssuer(),
            Policy(TrustCombinator.AnyOf, AssuranceLevel.Low, TrustSourceKind.Register, TrustSourceKind.X509Tenant));

        decision.IsTrusted.Should().BeTrue();
        decision.SignatureValid.Should().BeTrue();
        decision.DecidingSources.Should().Contain(TrustSourceKind.X509Tenant);
        decision.Evidence.VouchingSource.Should().Be(TrustSourceKind.X509Tenant);
    }

    [Fact]
    public async Task EvaluateAsync_AnyOf_NoSourceVouches_RejectsWithFirstDeclineReason()
    {
        var evaluator = Evaluator(
            new FakeResolver { Kind = TrustSourceKind.Register, Vouches = false, DeclineReason = TrustFailureReason.ChainInvalid });

        var decision = await evaluator.EvaluateAsync(SignedIssuer(),
            Policy(TrustCombinator.AnyOf, AssuranceLevel.Low, TrustSourceKind.Register));

        decision.IsTrusted.Should().BeFalse();
        decision.FailureReason.Should().Be(TrustFailureReason.ChainInvalid);
    }

    [Fact]
    public async Task EvaluateAsync_AllOf_OneDeclines_Rejects()
    {
        var evaluator = Evaluator(
            new FakeResolver { Kind = TrustSourceKind.Register, Vouches = true },
            new FakeResolver { Kind = TrustSourceKind.X509Tenant, Vouches = false });

        var decision = await evaluator.EvaluateAsync(SignedIssuer(),
            Policy(TrustCombinator.AllOf, AssuranceLevel.Low, TrustSourceKind.Register, TrustSourceKind.X509Tenant));

        decision.IsTrusted.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_AllOf_AllVouch_Accepts()
    {
        var evaluator = Evaluator(
            new FakeResolver { Kind = TrustSourceKind.Register, Vouches = true, Assurance = AssuranceLevel.Low },
            new FakeResolver { Kind = TrustSourceKind.X509Tenant, Vouches = true, Assurance = AssuranceLevel.High });

        var decision = await evaluator.EvaluateAsync(SignedIssuer(),
            Policy(TrustCombinator.AllOf, AssuranceLevel.Low, TrustSourceKind.Register, TrustSourceKind.X509Tenant));

        decision.IsTrusted.Should().BeTrue();
        decision.EstablishedAssurance.Should().Be(AssuranceLevel.High); // strongest vouching source
    }

    // ---- fail-closed (FR-013 / SC-007) ---------------------------------------

    [Fact]
    public async Task EvaluateAsync_AllOf_SourceUnavailable_FailsClosed()
    {
        // Only register is registered; the policy also requires x509-tenant (no resolver) → SourceUnavailable.
        var evaluator = Evaluator( new FakeResolver { Kind = TrustSourceKind.Register, Vouches = true });

        var decision = await evaluator.EvaluateAsync(SignedIssuer(),
            Policy(TrustCombinator.AllOf, AssuranceLevel.Low, TrustSourceKind.Register, TrustSourceKind.X509Tenant));

        decision.IsTrusted.Should().BeFalse();
        decision.FailureReason.Should().Be(TrustFailureReason.SourceUnavailable);
    }

    [Fact]
    public async Task EvaluateAsync_AnyOf_UnavailableSourceButAnotherVouches_Accepts()
    {
        var evaluator = Evaluator( new FakeResolver { Kind = TrustSourceKind.Register, Vouches = true });

        var decision = await evaluator.EvaluateAsync(SignedIssuer(),
            Policy(TrustCombinator.AnyOf, AssuranceLevel.Low, TrustSourceKind.Register, TrustSourceKind.TrustList));

        decision.IsTrusted.Should().BeTrue();
    }

    // ---- assurance (FR-012 / clarification A4) -------------------------------

    [Fact]
    public async Task EvaluateAsync_EstablishedBelowMinimum_RejectsInsufficientAssurance()
    {
        var evaluator = Evaluator(
            new FakeResolver { Kind = TrustSourceKind.Register, Vouches = true, Assurance = AssuranceLevel.Low });

        var decision = await evaluator.EvaluateAsync(SignedIssuer(),
            Policy(TrustCombinator.AnyOf, AssuranceLevel.Substantial, TrustSourceKind.Register));

        decision.IsTrusted.Should().BeFalse();
        decision.FailureReason.Should().Be(TrustFailureReason.InsufficientAssurance);
        decision.EstablishedAssurance.Should().Be(AssuranceLevel.Low);
    }

    [Fact]
    public async Task EvaluateAsync_ClaimOverride_RaisesOnlyWhenSourceIsSubstantialOrHigher()
    {
        // Low-conferring source + High claim → claim ignored (stays Low).
        var lowEvaluator = Evaluator(
            new FakeResolver { Kind = TrustSourceKind.Register, Vouches = true, Assurance = AssuranceLevel.Low });
        var lowIssuer = SignedIssuer();
        lowIssuer.ClaimedAssurance = AssuranceLevel.High;

        var lowDecision = await lowEvaluator.EvaluateAsync(lowIssuer,
            Policy(TrustCombinator.AnyOf, AssuranceLevel.Low, TrustSourceKind.Register));
        lowDecision.EstablishedAssurance.Should().Be(AssuranceLevel.Low);

        // Substantial-conferring source + High claim → claim honoured upward to High.
        var subEvaluator = Evaluator(
            new FakeResolver { Kind = TrustSourceKind.X509Tenant, Vouches = true, Assurance = AssuranceLevel.Substantial });
        var subIssuer = SignedIssuer();
        subIssuer.ClaimedAssurance = AssuranceLevel.High;

        var subDecision = await subEvaluator.EvaluateAsync(subIssuer,
            Policy(TrustCombinator.AnyOf, AssuranceLevel.Low, TrustSourceKind.X509Tenant));
        subDecision.EstablishedAssurance.Should().Be(AssuranceLevel.High);
    }

    // ---- status / revocation -------------------------------------------------

    [Fact]
    public async Task EvaluateAsync_StatusRevoked_RejectsRevoked()
    {
        var evaluator = EvaluatorWithStatus(new FakeStatusChecker(CredentialStatusValue.Invalid),
            new FakeResolver { Kind = TrustSourceKind.Register, Vouches = true });
        var issuer = SignedIssuer();
        issuer.Status = new StatusReference { Uri = "https://status", Index = 3 };

        var decision = await evaluator.EvaluateAsync(issuer, Policy(TrustCombinator.AnyOf, AssuranceLevel.Low, TrustSourceKind.Register));

        decision.IsTrusted.Should().BeFalse();
        decision.FailureReason.Should().Be(TrustFailureReason.Revoked);
    }

    [Fact]
    public async Task EvaluateAsync_StatusUnknown_FailClosed_RejectsRevocationUnavailable()
    {
        var evaluator = EvaluatorWithStatus(new FakeStatusChecker(CredentialStatusValue.Unresolved),
            new FakeResolver { Kind = TrustSourceKind.Register, Vouches = true });
        var issuer = SignedIssuer();
        issuer.Status = new StatusReference { Uri = "https://status", Index = 3 };
        issuer.RevocationPolicy = RevocationCheckPolicy.FailClosed;

        var decision = await evaluator.EvaluateAsync(issuer, Policy(TrustCombinator.AnyOf, AssuranceLevel.Low, TrustSourceKind.Register));

        decision.IsTrusted.Should().BeFalse();
        decision.FailureReason.Should().Be(TrustFailureReason.RevocationUnavailable);
    }

    [Fact]
    public async Task EvaluateAsync_StatusUnknown_FailOpen_Accepts()
    {
        var evaluator = EvaluatorWithStatus(new FakeStatusChecker(CredentialStatusValue.Unresolved),
            new FakeResolver { Kind = TrustSourceKind.Register, Vouches = true });
        var issuer = SignedIssuer();
        issuer.Status = new StatusReference { Uri = "https://status", Index = 3 };
        issuer.RevocationPolicy = RevocationCheckPolicy.FailOpen;

        var decision = await evaluator.EvaluateAsync(issuer, Policy(TrustCombinator.AnyOf, AssuranceLevel.Low, TrustSourceKind.Register));

        decision.IsTrusted.Should().BeTrue();
    }

    // ---- evidence & pinnability (FR-014 / FR-015 / SC-005) -------------------

    [Fact]
    public async Task EvaluateAsync_Accept_PopulatesEvidenceWithPolicyDigest()
    {
        var evaluator = Evaluator(
            new FakeResolver { Kind = TrustSourceKind.Register, Vouches = true, Assurance = AssuranceLevel.Substantial });
        var policy = Policy(TrustCombinator.AnyOf, AssuranceLevel.Low, TrustSourceKind.Register);

        var decision = await evaluator.EvaluateAsync(SignedIssuer("did:sorcha:org:abc"), policy);

        decision.IsTrusted.Should().BeTrue();
        decision.Evidence.IssuerId.Should().Be("did:sorcha:org:abc");
        decision.Evidence.AssuranceLevel.Should().Be(AssuranceLevel.Substantial);
        decision.Evidence.PolicyDigest.Should().Be(TrustEvaluator.ComputePolicyDigest(policy));
        decision.Evidence.PolicyDigest.Should().HaveLength(64); // SHA-256 hex
    }

    [Fact]
    public void ComputePolicyDigest_IsStableAcrossSourceOrdering()
    {
        var a = new TrustPolicy
        {
            Sources = [new TrustSourceRef { Kind = TrustSourceKind.Register }, new TrustSourceRef { Kind = TrustSourceKind.X509Tenant }]
        };
        var b = new TrustPolicy
        {
            Sources = [new TrustSourceRef { Kind = TrustSourceKind.X509Tenant }, new TrustSourceRef { Kind = TrustSourceKind.Register }]
        };

        TrustEvaluator.ComputePolicyDigest(a).Should().Be(TrustEvaluator.ComputePolicyDigest(b));
    }

    // ---- default policy (FR-026) ---------------------------------------------

    [Fact]
    public async Task EvaluateAsync_NullPolicy_AppliesDefaultRegisterSource()
    {
        // A register resolver that vouches at Low; null policy must fall back to register@Low.
        var evaluator = Evaluator(
            new FakeResolver { Kind = TrustSourceKind.Register, Vouches = true, Assurance = AssuranceLevel.Low });

        var decision = await evaluator.EvaluateAsync(SignedIssuer(), policy: null);

        decision.IsTrusted.Should().BeTrue();
        decision.Evidence.VouchingSource.Should().Be(TrustSourceKind.Register);
    }

    [Fact]
    public async Task EvaluateAsync_NullPolicy_NoRegisterResolver_FailsClosed()
    {
        var evaluator = Evaluator(); // no resolvers registered

        var decision = await evaluator.EvaluateAsync(SignedIssuer(), policy: null);

        decision.IsTrusted.Should().BeFalse();
        decision.FailureReason.Should().Be(TrustFailureReason.SourceUnavailable);
    }
}

public class TrustResolverRegistryTests
{
    private sealed class StubResolver(TrustSourceKind kind) : ITrustSourceResolver
    {
        public TrustSourceKind Kind => kind;
        public Task<TrustSourceVouch> VouchAsync(IssuerContext i, TrustSourceRef s, CancellationToken ct)
            => Task.FromResult(new TrustSourceVouch { Vouched = true });
    }

    [Fact]
    public void Resolve_RegisteredKind_ReturnsResolver()
    {
        var registry = new TrustResolverRegistry([new StubResolver(TrustSourceKind.Register)]);
        registry.Resolve(TrustSourceKind.Register).Should().NotBeNull();
        registry.Resolve(TrustSourceKind.X509Tenant).Should().BeNull();
    }

    [Fact]
    public void Register_SameKindTwice_LastWins()
    {
        var registry = new TrustResolverRegistry();
        var first = new StubResolver(TrustSourceKind.Register);
        var second = new StubResolver(TrustSourceKind.Register);
        registry.Register(first);
        registry.Register(second);
        registry.Resolve(TrustSourceKind.Register).Should().BeSameAs(second);
    }
}
