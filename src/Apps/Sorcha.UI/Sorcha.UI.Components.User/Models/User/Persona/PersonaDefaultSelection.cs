// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Persona;

/// <summary>
/// Normalises a persona list so exactly one row is flagged as the default.
/// </summary>
/// <remarks>
/// Issue #1271 (UT-008): the Default radio rendered with nothing selected even when the citizen had
/// exactly one email. The editor hydrates its rows verbatim from the read model, and a persona whose
/// default was never explicitly set comes back with every row's <c>IsDefault</c> false. "One value,
/// and it is not the default" is not a state that means anything to a citizen — it just reads as a
/// broken control. Applied at hydrate rather than at render so the value the citizen sees selected is
/// the value that gets saved.
/// </remarks>
public static class PersonaDefaultSelection
{
    /// <summary>
    /// Ensures exactly one row is default, in place. An explicit default is preserved; when none is
    /// set the first row is promoted; when several are set all but the first are cleared (the read
    /// model does not promise exactly one, and two selected radios in one group is a worse control
    /// than none). An empty list is left empty — a default is never invented out of nothing.
    /// </summary>
    public static void EnsureOne<T>(IList<T> rows, Func<T, bool> isDefault, Func<T, bool, T> withDefault)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(isDefault);
        ArgumentNullException.ThrowIfNull(withDefault);

        if (rows.Count == 0) return;

        var chosen = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (!isDefault(rows[i])) continue;
            chosen = i;
            break;
        }

        if (chosen < 0) chosen = 0;

        for (var i = 0; i < rows.Count; i++)
        {
            var shouldBeDefault = i == chosen;
            if (isDefault(rows[i]) != shouldBeDefault)
            {
                rows[i] = withDefault(rows[i], shouldBeDefault);
            }
        }
    }
}
