// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Framing guidance for a portrait field, authored in the <c>x-file</c> block and rendered as a
/// post-capture review overlay.
/// </summary>
/// <remarks>
/// <para>
/// Issue #1277 (UT-014). Before this, the only framing help was a list of written tips shown
/// alongside the file picker — advice the citizen read <i>before</i> taking a photo and could not
/// check their photo against afterwards. The overlay shows the picture they actually captured inside
/// the bounds it needs to meet, so "is my head the right size in the frame?" becomes something they
/// can see rather than estimate.
/// </para>
/// <para>
/// The numbers live in the blueprint rather than in the component because they are a property of the
/// credential being issued, not of the renderer: an issuer with looser or stricter portrait rules
/// than ICAO should be able to say so without a UI change. Defaults follow ICAO/ISO 19794-5
/// conventions (head occupying roughly 70–80% of frame height, centred).
/// </para>
/// <para>
/// Guidance only — this is a visual aid, never a gate. Nothing here rejects a photo: the citizen can
/// always accept what they took. A hard client-side reject would be worse than the silent drop this
/// issue is about, because it would stop them submitting at all.
/// </para>
/// </remarks>
public sealed record PortraitFramingRules
{
    /// <summary>Width of the guide oval, as a percentage of frame width.</summary>
    [JsonPropertyName("ovalWidthPct")]
    public double OvalWidthPct { get; init; } = 62;

    /// <summary>Where the top of the head should sit, as a percentage down from the frame top.</summary>
    [JsonPropertyName("headTopPct")]
    public double HeadTopPct { get; init; } = 8;

    /// <summary>Where the chin should sit, as a percentage down from the frame top.</summary>
    [JsonPropertyName("headBottomPct")]
    public double HeadBottomPct { get; init; } = 82;

    /// <summary>The ICAO-shaped default used when a blueprint authors no framing block.</summary>
    public static PortraitFramingRules Default { get; } = new();

    /// <summary>
    /// Reads the <c>framing</c> object from an <c>x-file</c> element. Returns <see cref="Default"/>
    /// when absent or unreadable — framing is guidance, so a malformed block must degrade to the
    /// standard overlay rather than removing the citizen's only visual check.
    /// </summary>
    public static PortraitFramingRules FromXFile(JsonElement xFile)
    {
        if (xFile.ValueKind != JsonValueKind.Object) return Default;
        if (!xFile.TryGetProperty("framing", out var framing)) return Default;
        if (framing.ValueKind != JsonValueKind.Object) return Default;

        var parsed = new PortraitFramingRules
        {
            OvalWidthPct = Read(framing, "ovalWidthPct", Default.OvalWidthPct),
            HeadTopPct = Read(framing, "headTopPct", Default.HeadTopPct),
            HeadBottomPct = Read(framing, "headBottomPct", Default.HeadBottomPct),
        };

        return parsed.Clamped();
    }

    /// <summary>
    /// Brings the values back into a renderable range. An author who writes an inverted or
    /// out-of-range band gets the default rather than an overlay drawn outside its own frame —
    /// nonsense guidance is worse than standard guidance.
    /// </summary>
    public PortraitFramingRules Clamped()
    {
        var oval = Math.Clamp(OvalWidthPct, 20, 100);
        var top = Math.Clamp(HeadTopPct, 0, 100);
        var bottom = Math.Clamp(HeadBottomPct, 0, 100);

        if (bottom - top < 10)
        {
            // A band under ten percent of the frame cannot describe a head. Treat it as unauthored.
            top = Default.HeadTopPct;
            bottom = Default.HeadBottomPct;
        }

        return new PortraitFramingRules
        {
            OvalWidthPct = oval,
            HeadTopPct = top,
            HeadBottomPct = bottom,
        };
    }

    private static double Read(JsonElement obj, string name, double fallback) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number
        && el.TryGetDouble(out var value)
            ? value
            : fallback;
}
