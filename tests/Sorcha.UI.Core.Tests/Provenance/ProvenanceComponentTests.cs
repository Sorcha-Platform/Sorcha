// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.UI.Core.Components.Provenance;
using Sorcha.UI.Core.Models.Provenance;
using Sorcha.UI.Core.Services;
using Xunit;

namespace Sorcha.UI.Core.Tests.Provenance;

/// <summary>
/// bUnit tests for the provenance surfaces (Feature 188).
/// </summary>
/// <remarks>
/// <para>
/// The tests that matter most are the ones about <b>not verifiable</b>. A partial replica, a
/// single-validator deployment and a docket predating Feature 187 all produce unverified rows, and
/// rendering those as failures — or as ticks — is the core misreading of the whole feature. So:
/// </para>
/// <list type="bullet">
///   <item><description>A failed row must be visually distinct from an unverified one.</description></item>
///   <item><description>An empty signer set must read as "not verifiable", not as a tick and not as an error.</description></item>
/// </list>
/// </remarks>
public class ProvenanceComponentTests : BunitContext
{
    private const string RegisterId = "b388e51816e34d4ea7ce275ca7e8219c";

    private readonly Mock<IProvenanceService> _provenance = new();

    public ProvenanceComponentTests()
    {
        Services.AddSingleton(_provenance.Object);
    }

    private static ProvenanceCheckView Check(
        string layer,
        ProvenanceStatus status,
        string headline = "headline",
        string? reason = null,
        string? detail = null) =>
        new(layer, status, headline, $"the {layer} evidence", detail, reason);

    private static ProvenanceTrailView Trail(params ProvenanceCheckView[] checks) =>
        new(RegisterId, 10, checks);

    // ── The trail ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The whole point: <c>failed</c> and <c>unverified</c> must not share a treatment. They differ
    /// in status class, in the word shown, and therefore in colour.
    /// </summary>
    [Fact]
    public void FailedRow_RendersDistinctlyFromUnverifiedRow()
    {
        _provenance.Setup(p => p.GetTrailAsync(RegisterId, 10UL, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Trail(
                Check("seal", ProvenanceStatus.Failed, "Contents do NOT match the sealed commitment"),
                Check("signers", ProvenanceStatus.Unverified, "No quorum evidence was recorded",
                    reason: "this docket carries no consensus votes")));

        var cut = Render<DocketProvenanceTrail>(p => p
            .Add(c => c.RegisterId, RegisterId)
            .Add(c => c.DocketNumber, 10UL));

        var seal = cut.Find("[data-testid='provenance-status-seal']");
        var signers = cut.Find("[data-testid='provenance-status-signers']");

        seal.TextContent.Trim().Should().Be("Failed");
        signers.TextContent.Trim().Should().Be("Not verifiable");

        seal.ClassList.Should().Contain("bad");
        signers.ClassList.Should().Contain("unv");
        signers.ClassList.Should().NotContain("bad",
            "an unverified check must not share the failure treatment — a partial replica would " +
            "otherwise render as a compromised one");
    }

    /// <summary>
    /// SC-005 at the rendering layer: an empty signer set reads as "not verifiable", never as a tick
    /// and never as an error.
    /// </summary>
    [Fact]
    public void EmptySignerSet_RendersAsNotVerifiable_NotATickAndNotAnError()
    {
        _provenance.Setup(p => p.GetTrailAsync(RegisterId, 10UL, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Trail(Check(
                "signers",
                ProvenanceStatus.Unverified,
                "No quorum evidence was recorded",
                reason: "this docket carries no consensus votes, so there is no quorum evidence to check")));

        var cut = Render<DocketProvenanceTrail>(p => p
            .Add(c => c.RegisterId, RegisterId)
            .Add(c => c.DocketNumber, 10UL));

        var status = cut.Find("[data-testid='provenance-status-signers']");
        status.TextContent.Trim().Should().Be("Not verifiable");
        status.ClassList.Should().NotContain("ok", "an unrun check must never render as a tick");
        status.ClassList.Should().NotContain("bad", "a supported configuration is not an error");

        cut.Find("[data-testid='provenance-reason-signers']").TextContent
            .Should().Contain("no consensus votes");
    }

    /// <summary>
    /// FR-002 / SC-006: a reviewer must be able to restate the basis of any result from the view
    /// alone, so every row shows what it was compared against — including rows that could not run.
    /// </summary>
    [Fact]
    public void EveryRow_ShowsWhatItWasCheckedAgainst()
    {
        _provenance.Setup(p => p.GetTrailAsync(RegisterId, 10UL, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Trail(
                Check("anchor", ProvenanceStatus.Verified),
                Check("chain", ProvenanceStatus.Unverified, reason: "predecessor not held")));

        var cut = Render<DocketProvenanceTrail>(p => p
            .Add(c => c.RegisterId, RegisterId)
            .Add(c => c.DocketNumber, 10UL));

        cut.Find("[data-testid='provenance-against-anchor']").TextContent
            .Should().Contain("the anchor evidence");
        cut.Find("[data-testid='provenance-against-chain']").TextContent
            .Should().Contain("the chain evidence");
    }

    /// <summary>FR-020: an unreachable trail says so, rather than rendering nothing.</summary>
    [Fact]
    public void TrailThatCannotBeLoaded_SaysSo()
    {
        _provenance.Setup(p => p.GetTrailAsync(RegisterId, 10UL, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProvenanceTrailView?)null);

        var cut = Render<DocketProvenanceTrail>(p => p
            .Add(c => c.RegisterId, RegisterId)
            .Add(c => c.DocketNumber, 10UL));

        cut.Find("[data-testid='provenance-trail-unavailable']").TextContent
            .Should().Contain("Could not load");
    }

    // ── The spine ────────────────────────────────────────────────────────────────────────────

    private void SetupSpine(params DocketSpineView[] dockets) =>
        _provenance.Setup(p => p.GetSpineAsync(
                RegisterId, It.IsAny<ulong?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterSpinePage(RegisterId, dockets, false, null));

    /// <summary>FR-007 — a validator-set change is visible at the point in history where it happened.</summary>
    [Fact]
    public void SpineWithARosterChange_RendersTheMarkerAtThatDocket()
    {
        SetupSpine(
            new DocketSpineView(11, DateTime.UnixEpoch, "validator-a", 1, RosterChanged: false),
            new DocketSpineView(12, DateTime.UnixEpoch, "validator-a", 1, RosterChanged: true));

        var cut = Render<RegisterLineage>(p => p.Add(c => c.RegisterId, RegisterId));

        cut.Find("[data-testid='roster-change-12']").TextContent
            .Should().Contain("Validator set changed at docket 12");

        cut.FindAll("[data-testid='roster-change-11']").Should().BeEmpty(
            "only the docket where the set actually changed carries the marker");
    }

    /// <summary>
    /// Zero signers is a plain statement of fact on a single-validator deployment. It must not read
    /// as missing or invalid signatures.
    /// </summary>
    [Fact]
    public void SpineEntryWithNoSigners_ReadsAsNoQuorumEvidence()
    {
        SetupSpine(new DocketSpineView(3, DateTime.UnixEpoch, "validator-a", 0, RosterChanged: false));

        var cut = Render<RegisterLineage>(p => p.Add(c => c.RegisterId, RegisterId));

        cut.Find("[data-testid='spine-signers-3']").TextContent.Trim()
            .Should().Be("no quorum evidence");
    }

    /// <summary>
    /// Plan D6: listing must not verify. The spine renders without the trail client being touched;
    /// only selecting a docket triggers verification.
    /// </summary>
    [Fact]
    public void RenderingTheSpine_VerifiesNothing()
    {
        SetupSpine(
            new DocketSpineView(1, DateTime.UnixEpoch, "validator-a", 1, false),
            new DocketSpineView(2, DateTime.UnixEpoch, "validator-a", 1, false),
            new DocketSpineView(3, DateTime.UnixEpoch, "validator-a", 1, false));

        Render<RegisterLineage>(p => p.Add(c => c.RegisterId, RegisterId));

        _provenance.Verify(
            p => p.GetTrailAsync(It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the spine must run no verification — verifying on list is O(n·m) and SC-007 requires " +
            "5,000 dockets to stay usable");
    }

    /// <summary>Selecting a docket is what triggers verification — and only for that docket.</summary>
    [Fact]
    public void SelectingADocket_VerifiesThatDocketOnly()
    {
        SetupSpine(
            new DocketSpineView(1, DateTime.UnixEpoch, "validator-a", 1, false),
            new DocketSpineView(2, DateTime.UnixEpoch, "validator-a", 1, false));

        _provenance.Setup(p => p.GetTrailAsync(RegisterId, It.IsAny<ulong>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Trail(Check("seal", ProvenanceStatus.Verified)));

        var cut = Render<RegisterLineage>(p => p.Add(c => c.RegisterId, RegisterId));

        cut.Find("[data-testid='spine-docket-2'] button").Click();

        _provenance.Verify(
            p => p.GetTrailAsync(RegisterId, 2UL, It.IsAny<CancellationToken>()), Times.Once);
        _provenance.Verify(
            p => p.GetTrailAsync(RegisterId, 1UL, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void SpineThatCannotBeLoaded_SaysSo()
    {
        _provenance.Setup(p => p.GetSpineAsync(
                RegisterId, It.IsAny<ulong?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegisterSpinePage?)null);

        var cut = Render<RegisterLineage>(p => p.Add(c => c.RegisterId, RegisterId));

        cut.Find("[data-testid='lineage-unavailable']").TextContent
            .Should().Contain("Could not load");
    }
}
