// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Forms;

/// <summary>
/// Decides whether a layout field is covered by the persona-autofill result set.
/// </summary>
/// <remarks>
/// <para>
/// Issue #1271 (UT-008): the per-field "filled from your profile" cue never appeared. The markup and
/// the CSS both existed — <c>SorchaFormRenderer</c> emits <c>class="… autofilled"</c> plus a
/// <c>persona-tick</c> pill, and the scoped stylesheet carries the cream background and warm border.
/// What was broken is the MATCH: the renderer asks about a field name from the layout's
/// <c>x-sections</c> ("email", "address") and compared it for exact equality against the resolver's
/// filled set, which holds full JSON Pointers to leaves ("/email/email", "/address/postcode").
/// </para>
/// <para>
/// The cue therefore only ever lit up for a fill that landed on a top-level scalar. Every real form
/// groups fields into objects, so in practice it never lit up at all — and the citizen got no
/// per-field indication that a value came from their profile.
/// </para>
/// <para>
/// Matching is on pointer SEGMENTS, not on a bare string prefix: "/addressLine1" must not satisfy
/// "address", or the form would tell the citizen their address came from their profile when it did
/// not. A false cue about provenance is worse than no cue.
/// </para>
/// </remarks>
public static class PersonaFillScope
{
    /// <summary>
    /// True when any filled pointer is the field itself or sits beneath it.
    /// <paramref name="fieldName"/> may be a bare name or already pointer-shaped.
    /// </summary>
    public static bool CoversField(IReadOnlyCollection<string> filledPointers, string fieldName)
    {
        if (filledPointers is null || filledPointers.Count == 0) return false;
        if (string.IsNullOrWhiteSpace(fieldName)) return false;

        var root = fieldName.StartsWith('/') ? fieldName : "/" + fieldName;
        var beneath = root + "/";

        foreach (var pointer in filledPointers)
        {
            if (string.Equals(pointer, root, StringComparison.Ordinal)) return true;
            if (pointer.StartsWith(beneath, StringComparison.Ordinal)) return true;
        }

        return false;
    }
}
