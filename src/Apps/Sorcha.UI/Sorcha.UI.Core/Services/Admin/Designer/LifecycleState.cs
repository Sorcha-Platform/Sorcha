// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Designer;

/// <summary>
/// Per-Blueprint authoring state that drives the Feature 142 lifecycle rail (data-model.md →
/// LifecycleState). This is transient client/session state held on <see cref="DesignerContext"/>;
/// it is NOT the authoritative gate. The server-side <c>RehearsalPass</c> record (keyed on the
/// executable-definition hash) is the truth — this state only mirrors it to drive the Go-live UI lock.
/// </summary>
/// <remarks>
/// Re-lock granularity (FR-023): <see cref="RehearsalPassedForCurrentExecDef"/> is computed by
/// comparing <see cref="PassedExecDefHash"/> with the current <see cref="ExecDefHash"/>. Because the
/// hash excludes presentational <c>x-*</c> keywords, a purely presentational edit leaves the hash
/// unchanged and the pass survives; an executable-definition change alters the hash and re-locks Go live.
/// </remarks>
public sealed class LifecycleState
{
    /// <summary>The stage currently shown in the workspace canvas.</summary>
    public LifecycleStage CurrentStage { get; set; } = LifecycleStage.Describe;

    /// <summary>
    /// Hash of the current Blueprint's executable definition, recomputed by
    /// <see cref="DesignerContext"/> on every Blueprint change. Null when no Blueprint is loaded.
    /// </summary>
    public string? ExecDefHash { get; set; }

    /// <summary>
    /// The executable-definition hash that last passed a full rehearsal (mirrors the server
    /// <c>RehearsalPass</c>). Null until a pass is recorded for the current service.
    /// </summary>
    public string? PassedExecDefHash { get; set; }

    /// <summary>Lineage when amending an already-published service; null for a fresh service.</summary>
    public AmendContext? AmendContext { get; set; }

    /// <summary>
    /// True when the current executable definition has a recorded passing full rehearsal — i.e.
    /// <see cref="PassedExecDefHash"/> matches <see cref="ExecDefHash"/>. Drives the Go-live UI lock.
    /// </summary>
    public bool RehearsalPassedForCurrentExecDef =>
        PassedExecDefHash is not null && PassedExecDefHash == ExecDefHash;
}
