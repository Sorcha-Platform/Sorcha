// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq;
using FluentAssertions;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Verifier.Engine.Dcql;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Feature 181 US2 (T029) — the requirement → DCQL mapping (contract §4). Single/AND
/// requirements produce a plain <c>credentials</c> list; <c>anyOfGroup</c> requirements
/// ride <c>credential_sets</c> alternatives while ungrouped asks stay required.
/// </summary>
public sealed class RequirementDcqlMapperTests
{
    private static CredentialRequirement Req(
        string type, string? anyOfGroup = null, string? description = null, params string[] requiredClaims) => new()
    {
        Type = type,
        AnyOfGroup = anyOfGroup,
        Description = description,
        RequiredClaims = requiredClaims.Length == 0
            ? null
            : requiredClaims.Select(c => new ClaimConstraint { ClaimName = c }).ToList(),
    };

    [Fact]
    public void Build_SingleRequirement_ProducesOneQuery_NoCredentialSets()
    {
        var query = RequirementDcqlMapper.Build([Req("AssuredIdentityCredential", requiredClaims: ["givenName", "familyName"])]);

        query.Validate().Should().BeEmpty();
        query.Credentials.Should().HaveCount(1);
        query.Credentials[0].Format.Should().Be(DcqlFormats.SdJwtVc);
        query.Credentials[0].Meta.VctValues.Should().ContainSingle().Which.Should().Be("AssuredIdentityCredential");
        query.CredentialSets.Should().BeNull("a single required ask needs no credential_sets");
    }

    [Fact]
    public void Build_MultipleUngrouped_ProducesAndOfQueries_NoCredentialSets()
    {
        // Two requirements, no anyOf tags → both required (AND), no credential_sets.
        var query = RequirementDcqlMapper.Build(
        [
            Req("AssuredIdentityCredential", requiredClaims: ["givenName"]),
            Req("ProofOfAddressCredential", requiredClaims: ["postcode"]),
        ]);

        query.Validate().Should().BeEmpty();
        query.Credentials.Should().HaveCount(2);
        query.CredentialSets.Should().BeNull();
        query.Credentials.Select(c => c.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Build_AnyOfGroup_MapsToCredentialSetAlternative()
    {
        // Two alternatives in one group → one required credential_set, each member a singleton option.
        var query = RequirementDcqlMapper.Build(
        [
            Req("PassportCredential", anyOfGroup: "id", description: "Prove your identity", requiredClaims: ["givenName"]),
            Req("DrivingLicenceCredential", anyOfGroup: "id", requiredClaims: ["givenName"]),
        ]);

        query.Validate().Should().BeEmpty();
        query.Credentials.Should().HaveCount(2);
        query.CredentialSets.Should().ContainSingle();

        var set = query.CredentialSets![0];
        set.Required.Should().BeTrue();
        set.Purpose.Should().Be("Prove your identity");
        set.Options.Should().HaveCount(2);
        set.Options.Should().OnlyContain(o => o.Count == 1);
        // Each option references a real credential id, and together they cover both asks.
        set.Options.SelectMany(o => o).Should().BeEquivalentTo(query.Credentials.Select(c => c.Id));
    }

    [Fact]
    public void Build_MixedAndPlusOr_MakesEveryAskRequiredExplicitly()
    {
        // One anyOf pair + one standalone required requirement. Because credential_sets is present,
        // the standalone ask must get its own required singleton set so it stays AND-required.
        var query = RequirementDcqlMapper.Build(
        [
            Req("PassportCredential", anyOfGroup: "id", requiredClaims: ["givenName"]),
            Req("DrivingLicenceCredential", anyOfGroup: "id", requiredClaims: ["givenName"]),
            Req("ProofOfAddressCredential", requiredClaims: ["postcode"]),
        ]);

        query.Validate().Should().BeEmpty();
        query.Credentials.Should().HaveCount(3);

        // One set for the "id" group + one singleton set for the standalone address ask.
        query.CredentialSets.Should().HaveCount(2);
        query.CredentialSets!.Should().OnlyContain(s => s.Required);

        var groupSet = query.CredentialSets!.Single(s => s.Options.Count == 2);
        groupSet.Options.Should().OnlyContain(o => o.Count == 1);

        var addressId = query.Credentials.Single(c => c.Meta.VctValues![0] == "ProofOfAddressCredential").Id;
        var standaloneSet = query.CredentialSets!.Single(s => s.Options.Count == 1);
        standaloneSet.Options[0].Should().ContainSingle().Which.Should().Be(addressId);
    }

    [Fact]
    public void Build_DuplicateTypes_ProducesUniqueDcqlIds()
    {
        var query = RequirementDcqlMapper.Build(
        [
            Req("IdentityCredential", requiredClaims: ["givenName"]),
            Req("IdentityCredential", requiredClaims: ["familyName"]),
        ]);

        query.Validate().Should().BeEmpty();
        query.Credentials.Select(c => c.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Build_SlugifiesNonAsciiTypeIntoLegalId()
    {
        // A type carrying slashes/colons (e.g. a vct URI) must still yield a DCQL-legal id.
        var query = RequirementDcqlMapper.Build([Req("https://sorcha.dev/vc/assured-identity/v1", requiredClaims: ["givenName"])]);

        query.Validate().Should().BeEmpty("the produced id must match the DCQL id charset");
        query.Credentials[0].Id.Should().MatchRegex("^[a-zA-Z0-9_-]+$");
    }

    [Fact]
    public void Build_EmptyRequirements_Throws()
    {
        var act = () => RequirementDcqlMapper.Build([]);
        act.Should().Throw<System.ArgumentException>();
    }
}

/// <summary>
/// AIAS M2 Task 3 — <see cref="CredentialRequirement.OptionalClaims"/> threading through
/// <see cref="RequirementDcqlMapper.Build"/> into the DCQL ask's optional-claims position
/// (contract §4, feature 174). Asserts the actual DCQL structure — which claim_sets option a
/// claim id lands in — rather than merely that the serialized JSON mentions the claim name.
/// </summary>
public sealed class RequirementDcqlMapperOptionalClaimsTests
{
    [Fact]
    public void Map_RequirementWithOptionalClaims_PlacesPortraitInOptionalClaimSetOnly()
    {
        var requirement = new CredentialRequirement
        {
            Type = "https://sorcha.dev/vc/assured-identity/v1",
            RequiredClaims = [new ClaimConstraint { ClaimName = "givenName" }],
            OptionalClaims = [new ClaimConstraint { ClaimName = "portrait" }],
        };

        var query = RequirementDcqlMapper.Build([requirement]);

        query.Validate().Should().BeEmpty();
        var credential = query.Credentials.Should().ContainSingle().Subject;

        credential.Claims.Should().NotBeNull();
        var portraitClaim = credential.Claims!.Should().ContainSingle(c => c.Path.SequenceEqual(new[] { "portrait" })).Subject;
        var givenNameClaim = credential.Claims!.Should().ContainSingle(c => c.Path.SequenceEqual(new[] { "givenName" })).Subject;

        // Both claims carry an id once any optional claim exists, so claim_sets can reference them.
        portraitClaim.Id.Should().NotBeNullOrEmpty();
        givenNameClaim.Id.Should().NotBeNullOrEmpty();

        // R2 mapping: first (preferred) claim_sets option = required + optional; second = required-only.
        // Asserting this directly proves "portrait" lands specifically in the optional position,
        // not merely that the word "portrait" appears somewhere in the serialized document.
        credential.ClaimSets.Should().HaveCount(2);
        credential.ClaimSets![0].Should().Contain(givenNameClaim.Id!).And.Contain(portraitClaim.Id!);
        credential.ClaimSets![1].Should().Contain(givenNameClaim.Id!).And.NotContain(portraitClaim.Id!);
    }

    [Fact]
    public void Build_RequirementWithoutOptionalClaims_BehavesAsBefore()
    {
        var requirement = new CredentialRequirement
        {
            Type = "https://sorcha.dev/vc/assured-identity/v1",
            RequiredClaims = [new ClaimConstraint { ClaimName = "givenName" }],
        };

        var query = RequirementDcqlMapper.Build([requirement]);

        query.Validate().Should().BeEmpty();
        var credential = query.Credentials.Should().ContainSingle().Subject;

        // Omitting optionalClaims must reproduce today's exact wire shape: no claim_sets, and the
        // single claim carries no id (ids are only assigned once an optional claim exists).
        credential.ClaimSets.Should().BeNull("omitting optionalClaims must not change the pre-existing wire shape");
        credential.Claims.Should().ContainSingle(c => c.Path.SequenceEqual(new[] { "givenName" }) && c.Id == null);

        var json = DcqlRequestBuilder.ToJson(query);
        json.Should().NotContain("claim_sets");
    }
}
