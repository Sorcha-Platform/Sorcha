// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.UI.Core.Models.Chat;
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
    private readonly ExecutableDefinitionHasher _execDefHasher = new();

    /// <summary>
    /// Feature 142 lifecycle state driving the rail (current stage, exec-def hash, rehearsal-pass
    /// mirror, amend lineage). The exec-def hash is recomputed on every Blueprint change so the
    /// Go-live lock re-locks on executable edits but not on presentational ones (FR-023).
    /// </summary>
    public LifecycleState Lifecycle { get; } = new();

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
        RecomputeExecDefHash();
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
        RecomputeExecDefHash();
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

    /// <summary>Moves the lifecycle rail to <paramref name="stage"/>. Fires <see cref="Changed"/>.</summary>
    public void SetStage(LifecycleStage stage)
    {
        Lifecycle.CurrentStage = stage;
        Changed?.Invoke();
    }

    /// <summary>
    /// Records that a full rehearsal passed for the current executable definition by snapshotting
    /// the current <see cref="LifecycleState.ExecDefHash"/> as the passed hash. After this, the
    /// Go-live lock opens and stays open across presentational edits, re-locking only on an
    /// executable-definition change (FR-023). Mirrors the authoritative server <c>RehearsalPass</c>.
    /// </summary>
    public void RecordRehearsalPassed()
    {
        Lifecycle.PassedExecDefHash = Lifecycle.ExecDefHash;
        Changed?.Invoke();
    }

    /// <summary>Recomputes the executable-definition hash from the current Blueprint (null when none).</summary>
    private void RecomputeExecDefHash()
    {
        Lifecycle.ExecDefHash = Blueprint is null ? null : _execDefHasher.ComputeHash(Blueprint);
    }
}
