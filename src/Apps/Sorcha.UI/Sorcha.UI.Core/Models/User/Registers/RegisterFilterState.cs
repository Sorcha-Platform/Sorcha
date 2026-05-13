// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models.Enums;

namespace Sorcha.UI.Core.Models.Registers;

/// <summary>
/// State for register list filtering and search.
/// </summary>
public record RegisterFilterState
{
    /// <summary>
    /// Text to search for in register names.
    /// </summary>
    public string SearchText { get; init; } = string.Empty;

    /// <summary>
    /// Selected status filters. Empty means all statuses shown.
    /// </summary>
    public IReadOnlySet<RegisterStatus> SelectedStatuses { get; init; } =
        new HashSet<RegisterStatus>();

    /// <summary>
    /// Returns true if any filters are active.
    /// </summary>
    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(SearchText) || SelectedStatuses.Count > 0;

    /// <summary>
    /// Applies filters to a collection of registers.
    /// </summary>
    public IEnumerable<RegisterViewModel> Apply(IEnumerable<RegisterViewModel> registers)
    {
        var filtered = registers;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(r =>
                r.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedStatuses.Count > 0)
        {
            filtered = filtered.Where(r => SelectedStatuses.Contains(r.Status));
        }

        return filtered;
    }

    /// <summary>
    /// Returns a new state with updated search text.
    /// </summary>
    public RegisterFilterState WithSearchText(string text) =>
        this with { SearchText = text };

    /// <summary>
    /// Returns a new state with toggled status filter.
    /// </summary>
    public RegisterFilterState ToggleStatus(RegisterStatus status)
    {
        var newStatuses = new HashSet<RegisterStatus>(SelectedStatuses);
        if (!newStatuses.Remove(status))
        {
            newStatuses.Add(status);
        }
        return this with { SelectedStatuses = newStatuses };
    }

    /// <summary>
    /// Returns a new state with all filters cleared.
    /// </summary>
    public RegisterFilterState Clear() => new();
}
