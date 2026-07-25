// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using MudBlazor;
using Sorcha.UI.Components.User.Components.Activity;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Activity;

/// <summary>
/// Guards issue #1273 at the row-treatment level: the PWA Activity feed chipped an AIAS rejection as
/// "Informational" and drew the same generic bell for every entry.
/// <para>
/// The cause was that every comparison was case-sensitive PascalCase while the wire sends kebab and
/// lower-case, so no severity or category ever matched. These cases use the REAL wire values read
/// from the stored inbox row in the issue: <c>severity: "warning"</c>,
/// <c>iconKey: "workflow.rejected"</c>.
/// </para>
/// </summary>
public sealed class ActivityFeedSeverityTests
{
    private const string RejectionIconKey = "workflow.rejected";

    /// <summary>The reported row: a warning must not read as routine information.</summary>
    [Fact]
    public void WarningSeverity_ChipsAsWarning_NotInformational()
    {
        ActivityFeed.ChipLabel("workflow", "warning").Should().Be(
            "Warning",
            "a rejected identity application must not carry the same weight as \"Profile updated\" (#1273)");
        ActivityFeed.ChipColor("workflow", "warning").Should().Be(Color.Warning);
    }

    [Fact]
    public void WarningSeverity_ColoursTheRowAccent()
        => ActivityFeed.SeverityColor("warning").Should().Be(
            Color.Warning, "the lower-case wire value must match — it previously fell through to Default");

    [Fact]
    public void CriticalSeverity_ReadsAsCritical()
    {
        ActivityFeed.ChipLabel("security", "critical").Should().Be("Critical");
        ActivityFeed.ChipColor("security", "critical").Should().Be(Color.Error);
        ActivityFeed.SeverityColor("critical").Should().Be(Color.Error);
    }

    [Fact]
    public void ActionRequiredSeverity_ReadsAsActionNeeded_FromItsKebabWireValue()
    {
        ActivityFeed.ChipLabel("workflow", "action-required").Should().Be("Action needed");
        ActivityFeed.ChipColor("workflow", "action-required").Should().Be(Color.Warning);
    }

    /// <summary>Routine entries must stay quiet — the fix must not inflate everything.</summary>
    [Fact]
    public void InfoSeverity_StaysInformational()
    {
        ActivityFeed.ChipLabel("system", "info").Should().Be("Informational");
        ActivityFeed.ChipColor("system", "info").Should().Be(Color.Default);
        ActivityFeed.SeverityColor("info").Should().Be(Color.Default);
    }

    /// <summary>An Action-category entry is still Actionable regardless of a quiet severity.</summary>
    [Fact]
    public void ActionCategory_IsActionable_EvenAtInfoSeverity()
        => ActivityFeed.ChipLabel("action", "info").Should().Be("Actionable");

    /// <summary>
    /// iconKey was ignored entirely — a rejection drew the same glyph as everything else. It must now
    /// win over the category glyph.
    /// </summary>
    [Fact]
    public void IconKey_DrivesTheGlyph_AndDiffersFromTheCategoryDefault()
    {
        var byKey = ActivityFeed.IconFor(RejectionIconKey, "workflow");
        var byCategory = ActivityFeed.IconFor(null, "workflow");

        byKey.Should().NotBe(byCategory, "a rejection must be visually distinguishable (#1273)");
        byKey.Should().Be(Icons.Material.Filled.Cancel);
    }

    /// <summary>
    /// The category glyph is the fallback, and it must actually resolve from the lower-case wire
    /// value — previously every row fell through to the generic bell.
    /// </summary>
    [Fact]
    public void CategoryGlyph_ResolvesFromTheLowerCaseWireValue()
    {
        ActivityFeed.IconFor(null, "workflow").Should().Be(Icons.Material.Filled.AccountTree);
        ActivityFeed.IconFor(null, "credential").Should().Be(Icons.Material.Filled.VerifiedUser);
        ActivityFeed.IconFor(null, "security").Should().Be(Icons.Material.Filled.Security);
    }

    /// <summary>An unknown iconKey degrades to the category glyph rather than to nothing.</summary>
    [Fact]
    public void UnknownIconKey_FallsBackToTheCategoryGlyph()
        => ActivityFeed.IconFor("something.nobody.mapped", "credential")
            .Should().Be(Icons.Material.Filled.VerifiedUser);

    /// <summary>A genuinely unknown category still yields a usable glyph, never an empty icon.</summary>
    [Fact]
    public void UnknownCategory_StillYieldsAGlyph()
        => ActivityFeed.IconFor(null, "brand-new-category")
            .Should().Be(Icons.Material.Filled.Notifications);

    /// <summary>
    /// PascalCase must keep working. Normalising for the kebab wire values must not break a caller or
    /// a stored row that uses the enum's own spelling.
    /// </summary>
    [Fact]
    public void PascalCaseValues_StillResolve()
    {
        ActivityFeed.SeverityColor("Warning").Should().Be(Color.Warning);
        ActivityFeed.ChipLabel("Workflow", "Critical").Should().Be("Critical");
        ActivityFeed.IconFor(null, "Workflow").Should().Be(Icons.Material.Filled.AccountTree);
    }
}
