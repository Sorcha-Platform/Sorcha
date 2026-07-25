// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Serialization;
using Sorcha.Tenant.Service.Models;
using Sorcha.UI.Components.User.Services.Shared;
using Xunit;

namespace Sorcha.UI.ContractTests;

/// <summary>
/// Pins the inbox severity/category wire contract between the Tenant Service enums and the client
/// code that classifies them (issue #1273).
/// <para>
/// The client held hand-written PascalCase strings (<c>"Warning"</c>, <c>"ActionRequired"</c>,
/// <c>"Action"</c>) while <c>SorchaJson</c> registers a kebab-case <c>JsonStringEnumConverter</c> in
/// <c>Converters</c> — which System.Text.Json ranks ABOVE a type-level attribute. So the wire says
/// <c>"warning"</c> and <c>"action-required"</c>, and none of the client comparisons ever matched.
/// Nothing failed loudly: the feed just drew the generic bell for every row, never coloured a
/// severity, and never showed the Actionable chip.
/// </para>
/// <para>
/// These tests serialise the <b>real server enums through the server's own options</b>, so they
/// cannot drift from a hand-maintained list. Renaming an enum member or changing the converter
/// breaks the build here instead of silently flattening the feed again.
/// </para>
/// </summary>
public sealed class InboxSeverityWireContractTests
{
    private static string Wire<T>(T value) where T : struct, Enum
        => JsonSerializer.Serialize(value, SorchaJson.Options).Trim('"');

    /// <summary>Every severity the server can emit must be recognised by the client's ranking.</summary>
    [Fact]
    public void EverySeverityTheServerEmits_IsRecognisedByTheClient()
    {
        var unrecognised = new List<string>();

        foreach (var severity in Enum.GetValues<InboxSeverity>())
        {
            var wire = Wire(severity);
            if (ActivityClassification.SeverityRank(wire) < 0)
            {
                unrecognised.Add($"{severity} -> \"{wire}\"");
            }
        }

        unrecognised.Should().BeEmpty(
            "a severity the client cannot rank silently renders as routine information — which is how "
            + "a rejected identity application came to read like \"Profile updated\" (#1273)");
    }

    /// <summary>
    /// The specific pairing that broke: the wire value is lower-case, so a case-sensitive comparison
    /// against "Warning" fails. Pinning the literal proves the client sees what the server sends.
    /// </summary>
    [Fact]
    public void WarningSeverity_GoesOnTheWireLowerCase_AndIsTreatedAsElevated()
    {
        var wire = Wire(InboxSeverity.Warning);

        wire.Should().Be("warning", "this is the value the client must match, not \"Warning\"");
        ActivityClassification.IsElevated(wire).Should().BeTrue();
    }

    /// <summary>
    /// `ActionRequired` is the one that kebab-cases, and it is the one that gates the Actionable chip.
    /// Before the fix this returned false for every severity, making that chip unreachable.
    /// </summary>
    [Fact]
    public void ActionRequiredSeverity_KebabCasesOnTheWire_AndStillCountsAsActionable()
    {
        var wire = Wire(InboxSeverity.ActionRequired);

        wire.Should().Be("action-required");
        ActivityClassification.IsActionable("workflow", wire).Should().BeTrue();
    }

    /// <summary>Ordering must survive normalisation — Critical outranks Warning outranks Info.</summary>
    [Fact]
    public void SeverityRanking_PreservesTheServersOrdinalOrder()
    {
        var info = ActivityClassification.SeverityRank(Wire(InboxSeverity.Info));
        var warning = ActivityClassification.SeverityRank(Wire(InboxSeverity.Warning));
        var actionRequired = ActivityClassification.SeverityRank(Wire(InboxSeverity.ActionRequired));
        var critical = ActivityClassification.SeverityRank(Wire(InboxSeverity.Critical));

        info.Should().BeLessThan(warning);
        warning.Should().BeLessThan(actionRequired);
        actionRequired.Should().BeLessThan(critical);
    }

    /// <summary>
    /// Every category the server can emit must resolve to a glyph token, or the feed is a wall of
    /// identical bells — the reported symptom.
    /// </summary>
    /// <remarks>
    /// <see cref="InboxCategory.Custom"/> is the deliberate exception: a caller-defined category has
    /// no inherent glyph, so the generic notification bell is the correct answer for it. Every OTHER
    /// category must map, so adding a new one without giving it a glyph fails here.
    /// </remarks>
    [Fact]
    public void EveryCategoryTheServerEmits_IsNormalisedToAKnownToken()
    {
        var known = new[] { "action", "credential", "membership", "security", "system", "workflow" };
        var unmapped = new List<string>();

        foreach (var category in Enum.GetValues<InboxCategory>())
        {
            if (category == InboxCategory.Custom) continue;

            var wire = Wire(category);
            if (!known.Contains(ActivityClassification.Normalise(wire)))
            {
                unmapped.Add($"{category} -> \"{wire}\"");
            }
        }

        unmapped.Should().BeEmpty(
            "an unmapped category falls through to the generic bell glyph, which is what made all "
            + "five of the citizen's entries look identical (#1273)");
    }

    /// <summary>Informational stays informational — the fix must not inflate every row's weight.</summary>
    [Fact]
    public void InfoSeverity_IsNeitherElevatedNorActionable()
    {
        var wire = Wire(InboxSeverity.Info);

        ActivityClassification.IsElevated(wire).Should().BeFalse();
        ActivityClassification.IsActionable("workflow", wire).Should().BeFalse();
    }
}
