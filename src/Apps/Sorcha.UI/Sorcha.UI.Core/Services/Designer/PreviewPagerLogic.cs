// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Globalization;
using BlueprintAction = Sorcha.Blueprint.Models.Action;

namespace Sorcha.UI.Core.Services.Designer;

/// <summary>
/// Pure pager logic for the Form Preview pane. Operates over a readonly action list
/// and string-form action IDs so that the <see cref="DesignerContext.ActiveActionId"/>
/// can remain URL- and dropdown-friendly.
/// </summary>
public static class PreviewPagerLogic
{
    /// <summary>
    /// Returns the ID of the next action after <paramref name="currentId"/>, or <c>null</c>
    /// if the current action is the last. If <paramref name="currentId"/> is null or
    /// unknown, returns the first action's ID.
    /// </summary>
    public static string? Next(IReadOnlyList<BlueprintAction> actions, string? currentId)
    {
        if (actions is null || actions.Count == 0)
        {
            return null;
        }

        var index = IndexOf(actions, currentId);
        if (index < 0)
        {
            return actions[0].Id.ToString(CultureInfo.InvariantCulture);
        }

        if (index >= actions.Count - 1)
        {
            return null;
        }

        return actions[index + 1].Id.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Returns the ID of the previous action before <paramref name="currentId"/>, or
    /// <c>null</c> if the current action is the first. If <paramref name="currentId"/>
    /// is null or unknown, returns the first action's ID.
    /// </summary>
    public static string? Previous(IReadOnlyList<BlueprintAction> actions, string? currentId)
    {
        if (actions is null || actions.Count == 0)
        {
            return null;
        }

        var index = IndexOf(actions, currentId);
        if (index < 0)
        {
            return actions[0].Id.ToString(CultureInfo.InvariantCulture);
        }

        if (index <= 0)
        {
            return null;
        }

        return actions[index - 1].Id.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Returns <paramref name="targetId"/> if it matches an action's ID, otherwise <c>null</c>.
    /// </summary>
    public static string? Jump(IReadOnlyList<BlueprintAction> actions, string targetId)
    {
        if (actions is null || actions.Count == 0 || string.IsNullOrWhiteSpace(targetId))
        {
            return null;
        }

        return IndexOf(actions, targetId) >= 0 ? targetId : null;
    }

    private static int IndexOf(IReadOnlyList<BlueprintAction> actions, string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || !int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return -1;
        }

        for (var i = 0; i < actions.Count; i++)
        {
            if (actions[i].Id == parsed)
            {
                return i;
            }
        }

        return -1;
    }
}
