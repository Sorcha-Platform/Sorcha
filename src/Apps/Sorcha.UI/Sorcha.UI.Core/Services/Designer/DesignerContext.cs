// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.UI.Core.Services.Designer;

/// <summary>
/// Client-side shared state for the AI Designer unified shell. One instance is registered
/// as scoped per circuit/window; panes (AI, Diagram, Preview) and the shared toolbar all
/// read and mutate this state and subscribe to <see cref="Changed"/> to refresh.
/// </summary>
/// <remarks>
/// Invariants:
/// <list type="number">
/// <item>Each public mutation fires <see cref="Changed"/> at most once.</item>
/// <item>Manual cursor is sticky until <see cref="FollowAi"/> releases it.</item>
/// <item>Tracking of the last AI-edited action persists across manual overrides.</item>
/// <item><see cref="IsDirty"/> reflects unsaved blueprint edits, not unsaved chat.</item>
/// </list>
/// </remarks>
public class DesignerContext
{
    private string? _lastAiEditedActionId;

    /// <summary>The blueprint currently being edited, or <c>null</c> when none is loaded.</summary>
    public BlueprintModel? Blueprint { get; set; }

    /// <summary>Most recent validation result from AI or Diagram revalidation.</summary>
    public ValidationResult? Validation { get; set; }

    /// <summary>Active chat session identifier (set by the AI pane).</summary>
    public string? ChatSessionId { get; set; }

    /// <summary>ID of the action currently displayed in Preview (nullable string for URL/dropdown friendliness).</summary>
    public string? ActiveActionId { get; set; }

    /// <summary>True once the user takes manual cursor control; reset by <see cref="FollowAi"/>.</summary>
    public bool IsManualCursor { get; set; }

    /// <summary>True when the in-memory blueprint differs from the last saved copy.</summary>
    public bool IsDirty { get; set; }

    /// <summary>Fires exactly once per public mutation. Subscribers should call <c>StateHasChanged</c>.</summary>
    public event Action? Changed;

    /// <summary>
    /// Adopts a freshly loaded blueprint. Clears the active action and releases manual cursor.
    /// Does NOT set <see cref="IsDirty"/> — a loaded blueprint has nothing unsaved.
    /// </summary>
    public void SetBlueprint(BlueprintModel bp)
    {
        Blueprint = bp;
        ActiveActionId = null;
        IsManualCursor = false;
        Changed?.Invoke();
    }

    /// <summary>
    /// Applies an AI-produced blueprint update. Always records <paramref name="editedActionId"/>
    /// as the last AI edit; auto-cursors to it only when the user has not taken manual control.
    /// Marks dirty.
    /// </summary>
    public void ApplyAiUpdate(BlueprintModel bp, ValidationResult? val, string? editedActionId)
    {
        Blueprint = bp;
        Validation = val;
        _lastAiEditedActionId = editedActionId;
        if (!IsManualCursor)
        {
            ActiveActionId = editedActionId;
        }
        IsDirty = true;
        Changed?.Invoke();
    }

    /// <summary>Selects an action manually (pager click, diagram node click) and locks manual cursor.</summary>
    public void SetActiveActionManual(string actionId)
    {
        ActiveActionId = actionId;
        IsManualCursor = true;
        Changed?.Invoke();
    }

    /// <summary>Releases manual cursor and snaps to the most recent AI-edited action (may be null).</summary>
    public void FollowAi()
    {
        IsManualCursor = false;
        ActiveActionId = _lastAiEditedActionId;
        Changed?.Invoke();
    }

    /// <summary>Marks the blueprint dirty. Fires <see cref="Changed"/> only on transition.</summary>
    public void MarkDirty()
    {
        if (IsDirty)
        {
            return;
        }
        IsDirty = true;
        Changed?.Invoke();
    }

    /// <summary>Marks the blueprint clean (post-save). Fires <see cref="Changed"/> only on transition.</summary>
    public void MarkClean()
    {
        if (!IsDirty)
        {
            return;
        }
        IsDirty = false;
        Changed?.Invoke();
    }

    /// <summary>Updates validation independently of blueprint changes (Diagram-initiated revalidate).</summary>
    public void UpdateValidation(ValidationResult? val)
    {
        Validation = val;
        Changed?.Invoke();
    }
}
