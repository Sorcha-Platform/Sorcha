// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.ServiceClients.Did;

namespace Sorcha.ServiceClients.Tests.Did;

/// <summary>
/// Feature 120 US4 — exercises the deterministic six-step cross-resolution algorithm
/// per <c>contracts/did-resolver-registry-contract.md</c> "Testing" section. Covers all
/// 10 contract test cases.
/// </summary>
public sealed class DidResolverRegistryCrossResolutionTests
{
    private const string PrimaryDid = "did:sorcha:org:abc";
    private const string LinkedDidA = "did:web:example.com:orgs:abc";
    private const string LinkedDidB = "did:web:other.example:orgs:abc";

    private const string KeyJwkA = """{"kty":"OKP","crv":"Ed25519","x":"11qYAYKxCrfVS_7TyWQHOg7hcvPapiMlrwIaaPcHURo"}""";
    private const string KeyJwkB = """{"kty":"OKP","crv":"Ed25519","x":"DIFFERENTKEYBYTESxxxxxxxxxxxxxxxxxxxxxxxxxx"}""";

    private static JsonElement Jwk(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static VerificationMethod Vm(string id, string did, string jwkRaw) => new()
    {
        Id = id, Type = "JsonWebKey2020", Controller = did, PublicKeyJwk = Jwk(jwkRaw)
    };

    private sealed class FakeResolver : IDidResolver
    {
        private readonly string _method;
        private readonly Dictionary<string, DidDocument?> _docs;
        public int Calls;

        public FakeResolver(string method, Dictionary<string, DidDocument?> docs)
        {
            _method = method;
            _docs = docs;
        }

        public bool CanResolve(string method) => string.Equals(method, _method, StringComparison.OrdinalIgnoreCase);

        public Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_docs.TryGetValue(did, out var d) ? d : null);
        }
    }

    private static DidResolverRegistry BuildRegistry(params (string method, Dictionary<string, DidDocument?> docs)[] resolvers)
    {
        var reg = new DidResolverRegistry(NullLogger<DidResolverRegistry>.Instance);
        foreach (var (m, d) in resolvers) reg.Register(new FakeResolver(m, d));
        return reg;
    }

    [Fact]
    public async Task NoLinks_PassesThrough()
    {
        var primary = new DidDocument
        {
            Id = PrimaryDid,
            VerificationMethod = [Vm($"{PrimaryDid}#k1", PrimaryDid, KeyJwkA)]
        };
        var registry = BuildRegistry(("sorcha", new() { [PrimaryDid] = primary }));

        var result = await registry.ResolveWithAlsoKnownAsAsync(PrimaryDid);

        result.Should().NotBeNull();
        result!.Id.Should().Be(PrimaryDid);
        result.VerificationMethod.Should().HaveCount(1);
    }

    [Fact]
    public async Task OneLinkMatching_ReturnsMerged()
    {
        var primary = new DidDocument
        {
            Id = PrimaryDid,
            AlsoKnownAs = [LinkedDidA],
            VerificationMethod = [Vm($"{PrimaryDid}#k1", PrimaryDid, KeyJwkA)]
        };
        var linked = new DidDocument
        {
            Id = LinkedDidA,
            VerificationMethod = [Vm($"{LinkedDidA}#k1", LinkedDidA, KeyJwkA)]
        };
        var registry = BuildRegistry(
            ("sorcha", new() { [PrimaryDid] = primary }),
            ("web", new() { [LinkedDidA] = linked }));

        var result = await registry.ResolveWithAlsoKnownAsAsync(PrimaryDid);

        result.Should().NotBeNull();
        result!.AlsoKnownAs.Should().Contain(LinkedDidA);
        result.VerificationMethod.Should().HaveCount(1);
    }

    [Fact]
    public async Task OneLinkUnreachable_ReturnsPrimaryWithEquivalenceWithheld()
    {
        // W3C DID Core §5.1.3 — alsoKnownAs is an unverifiable assertion, so an
        // unreachable link must NOT invalidate a primary document that resolved.
        // It is skipped: the primary's own key material survives intact, and the
        // unverified link is absent from the result so no caller can read it as
        // verified equivalence.
        var primary = new DidDocument
        {
            Id = PrimaryDid,
            AlsoKnownAs = [LinkedDidA],
            VerificationMethod = [Vm($"{PrimaryDid}#k1", PrimaryDid, KeyJwkA)]
        };
        var registry = BuildRegistry(
            ("sorcha", new() { [PrimaryDid] = primary }),
            ("web", new() { [LinkedDidA] = null })); // unreachable

        var result = await registry.ResolveWithAlsoKnownAsAsync(PrimaryDid);

        result.Should().NotBeNull();
        result!.Id.Should().Be(PrimaryDid);
        result.VerificationMethod.Should().HaveCount(1);
        result.AlsoKnownAs.Should().NotContain(LinkedDidA,
            "an unresolvable link must not be reported as verified equivalence");
    }

    [Fact]
    public async Task OrgIssuerDid_WithUnservedDidWebLink_StillYieldsIssuanceKey()
    {
        // Regression for the live n1 failure (2026-07-28): every Sorcha org publishes a
        // did:web:{PlatformDomain}:orgs:{orgId} alsoKnownAs that nothing serves. That
        // unreachable hint was discarding the whole issuer document, so EVERY org-issued
        // credential failed issuer-signature verification the first time a verifier ran.
        const string orgDid = "did:sorcha:org:ws11qz8yulqml9wq";
        const string unservedWebDid = "did:web:sorcha.dev:orgs:1065431a-a5b0-4c61-a7ed-38ec08337c81";
        var kid = $"{orgDid}#vc-issuance-1";

        var primary = new DidDocument
        {
            Id = orgDid,
            AlsoKnownAs = [unservedWebDid],
            VerificationMethod = [Vm(kid, orgDid, KeyJwkA)],
            AssertionMethod = [kid]
        };
        var registry = BuildRegistry(
            ("sorcha", new() { [orgDid] = primary }),
            ("web", new() { [unservedWebDid] = null })); // nothing serves did:web

        var result = await registry.ResolveWithAlsoKnownAsAsync(orgDid);

        result.Should().NotBeNull("an unserved federation hint must not veto the issuer DID");
        result!.AssertionMethod.Should().Contain(kid,
            "the issuance key must remain usable for issuer-signature verification");
        result.VerificationMethod.Should().ContainSingle(v => v.Id == kid);
    }

    [Fact]
    public async Task OneLinkMismatch_ReturnsNull()
    {
        var primary = new DidDocument
        {
            Id = PrimaryDid,
            AlsoKnownAs = [LinkedDidA],
            VerificationMethod = [Vm($"{PrimaryDid}#k1", PrimaryDid, KeyJwkA)]
        };
        var attacker = new DidDocument
        {
            Id = LinkedDidA,
            VerificationMethod = [Vm($"{LinkedDidA}#k1", LinkedDidA, KeyJwkB)]
        };
        var registry = BuildRegistry(
            ("sorcha", new() { [PrimaryDid] = primary }),
            ("web", new() { [LinkedDidA] = attacker }));

        var result = await registry.ResolveWithAlsoKnownAsAsync(PrimaryDid);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TwoLinks_PartialMatch_ReturnsNull()
    {
        var primary = new DidDocument
        {
            Id = PrimaryDid,
            AlsoKnownAs = [LinkedDidA, LinkedDidB],
            VerificationMethod = [Vm($"{PrimaryDid}#k1", PrimaryDid, KeyJwkA)]
        };
        var matchingLink = new DidDocument
        {
            Id = LinkedDidA,
            VerificationMethod = [Vm($"{LinkedDidA}#k1", LinkedDidA, KeyJwkA)]
        };
        var mismatchingLink = new DidDocument
        {
            Id = LinkedDidB,
            VerificationMethod = [Vm($"{LinkedDidB}#k1", LinkedDidB, KeyJwkB)]
        };
        var registry = BuildRegistry(
            ("sorcha", new() { [PrimaryDid] = primary }),
            ("web", new() { [LinkedDidA] = matchingLink, [LinkedDidB] = mismatchingLink }));

        var result = await registry.ResolveWithAlsoKnownAsAsync(PrimaryDid);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Cycle_DoesNotInfiniteLoop()
    {
        var primary = new DidDocument
        {
            Id = PrimaryDid,
            AlsoKnownAs = [LinkedDidA, PrimaryDid], // self-reference + linked
            VerificationMethod = [Vm($"{PrimaryDid}#k1", PrimaryDid, KeyJwkA)]
        };
        var linked = new DidDocument
        {
            Id = LinkedDidA,
            AlsoKnownAs = [PrimaryDid], // back-reference
            VerificationMethod = [Vm($"{LinkedDidA}#k1", LinkedDidA, KeyJwkA)]
        };
        var registry = BuildRegistry(
            ("sorcha", new() { [PrimaryDid] = primary }),
            ("web", new() { [LinkedDidA] = linked }));

        var result = await registry.ResolveWithAlsoKnownAsAsync(PrimaryDid);

        result.Should().NotBeNull();
        result!.AlsoKnownAs.Should().Contain(LinkedDidA);
        // Self-reference skipped — no infinite loop
    }

    [Fact]
    public async Task EmptyDid_ReturnsNull()
    {
        var registry = BuildRegistry();
        (await registry.ResolveWithAlsoKnownAsAsync(string.Empty)).Should().BeNull();
    }

    [Fact]
    public async Task PrimaryUnresolved_ReturnsNull()
    {
        var registry = BuildRegistry(("sorcha", new() { [PrimaryDid] = null }));
        (await registry.ResolveWithAlsoKnownAsAsync(PrimaryDid)).Should().BeNull();
    }

    [Fact]
    public async Task MergedDocument_FiltersAssertionMethodToMatchedVms()
    {
        var primary = new DidDocument
        {
            Id = PrimaryDid,
            AlsoKnownAs = [LinkedDidA],
            VerificationMethod =
            [
                Vm($"{PrimaryDid}#k1", PrimaryDid, KeyJwkA),
                Vm($"{PrimaryDid}#k2", PrimaryDid, KeyJwkB)  // not in linked doc — gets filtered
            ],
            AssertionMethod = [$"{PrimaryDid}#k1", $"{PrimaryDid}#k2"]
        };
        var linked = new DidDocument
        {
            Id = LinkedDidA,
            VerificationMethod = [Vm($"{LinkedDidA}#k1", LinkedDidA, KeyJwkA)]
        };
        var registry = BuildRegistry(
            ("sorcha", new() { [PrimaryDid] = primary }),
            ("web", new() { [LinkedDidA] = linked }));

        var result = await registry.ResolveWithAlsoKnownAsAsync(PrimaryDid);

        result.Should().NotBeNull();
        result!.VerificationMethod.Should().ContainSingle(v => v.Id == $"{PrimaryDid}#k1");
        result.AssertionMethod.Should().ContainSingle().Which.Should().Be($"{PrimaryDid}#k1");
    }
}
