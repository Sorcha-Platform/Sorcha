// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

/// <summary>
/// Feature 137 (C1): selects which published blueprint version a node should use for instance
/// creation when the draft/editable store has no copy — the replica case, where the blueprint
/// exists only in the replicated published store. Picks the highest <c>Version</c> (newest
/// <c>PublishedAt</c> as a tiebreak) and ignores entries with no blueprint payload. Extracted as
/// an internal static so the selection contract is unit-testable without the full endpoint.
/// </summary>
internal static class PublishedBlueprintSelector
{
    internal static PublishedBlueprint? SelectLatest(IEnumerable<PublishedBlueprint> versions)
        => versions
            .Where(p => p.Blueprint is not null)
            .OrderByDescending(p => p.Version)
            .ThenByDescending(p => p.PublishedAt)
            .FirstOrDefault();
}
