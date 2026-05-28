// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Service.Services;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="DirectedBuildStarter"/> — Feature 142 US4 (T042 / T043 / FR-010 / FR-012).
/// </summary>
/// <remarks>
/// The starter helper turns a stable starter id (e.g. <c>grant</c>) into a deterministic seed
/// <see cref="Sorcha.Blueprint.Models.Blueprint"/>. The mapping is plain-language → constructs:
/// the administrator never names "starting action", "credential requirement" or "issue credential",
/// they pick a recognisable shape and the helper produces the right underlying constructs.
/// </remarks>
public class DirectedBuildStarterTests
{
    private readonly DirectedBuildStarter _starter = new();

    [Theory]
    [InlineData("grant")]
    [InlineData("permit")]
    [InlineData("certify-then-apply")]
    public void TryCreateSeed_KnownStarter_ReturnsDeterministicBlueprint(string starterId)
    {
        var first = _starter.TryCreateSeed(starterId);
        var second = _starter.TryCreateSeed(starterId);

        first.Should().NotBeNull("the starter id is a recognised directed-build choice");
        second.Should().NotBeNull();

        // Determinism — same id ⇒ same shape (titles, participants, actions, credential affordances).
        first!.Title.Should().Be(second!.Title);
        first.Participants.Count.Should().Be(second.Participants.Count);
        first.Actions.Count.Should().Be(second.Actions.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    [InlineData("free-text-not-a-starter")]
    public void TryCreateSeed_UnknownOrEmpty_ReturnsNull(string? starterId)
    {
        _starter.TryCreateSeed(starterId).Should().BeNull();
    }

    [Fact]
    public void TryCreateSeed_Grant_HasOpenStartingActionAndAtLeastTwoParticipants()
    {
        var bp = _starter.TryCreateSeed("grant");

        bp.Should().NotBeNull();
        bp!.Participants.Count.Should().BeGreaterThanOrEqualTo(2,
            "a grant workflow needs at least the applicant and a reviewer");
        bp.Actions.Should().NotBeEmpty();
        bp.Actions.Should().Contain(a => a.IsStartingAction,
            "the applicant's submission must be marked as a starting action (open submission)");
    }

    [Fact]
    public void TryCreateSeed_Permit_IssuesACredential()
    {
        var bp = _starter.TryCreateSeed("permit");

        bp.Should().NotBeNull();
        bp!.Actions.Should().Contain(
            a => a.CredentialIssuanceConfig != null,
            "the permit pattern translates 'we hand them a permit' into issue_credential");
    }

    [Fact]
    public void TryCreateSeed_CertifyThenApply_RequiresACredentialOnTheStartingAction()
    {
        var bp = _starter.TryCreateSeed("certify-then-apply");

        bp.Should().NotBeNull();
        var starting = bp!.Actions.FirstOrDefault(a => a.IsStartingAction);
        starting.Should().NotBeNull("certify-then-apply has a gated starting action");
        starting!.CredentialRequirements.Should().NotBeNullOrEmpty(
            "'they must be certified' translates into require_credential on the starting action — without the administrator naming the construct");
    }

    [Fact]
    public void KnownStarterIds_ContainsGrantPermitAndCertifyThenApply()
    {
        DirectedBuildStarter.KnownStarterIds.Should().Contain(["grant", "permit", "certify-then-apply"]);
    }
}
