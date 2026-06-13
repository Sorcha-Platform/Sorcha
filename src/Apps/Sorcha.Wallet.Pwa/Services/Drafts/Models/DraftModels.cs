// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Pwa.Services.Drafts.Models;

/// <summary>Feature 152 — lifecycle state of a local action draft.</summary>
public enum DraftStatus
{
    /// <summary>Being filled; autosaved locally.</summary>
    Editing = 0,
    /// <summary>Marked complete by the citizen, awaiting submission.</summary>
    ReadyToSubmit = 1,
    /// <summary>Handed to the submit queue.</summary>
    Queued = 2,
    /// <summary>Successfully submitted (draft normally cleared).</summary>
    Submitted = 3,
    /// <summary>A deferred submit could not be applied; held for the citizen to resolve.</summary>
    NeedsAttention = 4,
}

/// <summary>Feature 152 — why a held submission could not be applied (detect/hold/ask).</summary>
public enum ConflictReason
{
    /// <summary>No conflict.</summary>
    None = 0,
    /// <summary>This action was already submitted (here or on another device).</summary>
    AlreadySubmitted = 1,
    /// <summary>The workflow has moved on; this is no longer the current action.</summary>
    StepMovedOn = 2,
    /// <summary>The instance is closed (completed / rejected / cancelled).</summary>
    InstanceClosed = 3,
}

/// <summary>Feature 152 — a captured photo/file persisted with a draft (encrypted at rest).</summary>
/// <param name="FileName">Original file name.</param>
/// <param name="ContentType">MIME type.</param>
/// <param name="ContentBase64">Base64 bytes (encrypted in the store; held in memory while editing).</param>
/// <param name="CapturedAt">When captured.</param>
/// <param name="Scope">The JSON-Pointer form field this media attaches to (e.g. "/proofOfAddress").</param>
public sealed record DraftMedia(
    string FileName, string ContentType, string ContentBase64, DateTimeOffset CapturedAt, string Scope = "");

/// <summary>
/// Feature 152 — a citizen's in-progress action draft, persisted encrypted on the device. Keyed by
/// <see cref="Key"/> (<c>instanceId:actionId</c>).
/// </summary>
public sealed record ActionDraft
{
    /// <summary>The action's instance id.</summary>
    public required string InstanceId { get; init; }
    /// <summary>The action id within the instance.</summary>
    public required int ActionId { get; init; }
    /// <summary>Flat JSON-Pointer-keyed form values, as the form renderer emits.</summary>
    public Dictionary<string, object?> FormData { get; init; } = new();
    /// <summary>Captured photos/files held with the draft.</summary>
    public List<DraftMedia> Media { get; init; } = new();
    /// <summary>Lifecycle state.</summary>
    public DraftStatus Status { get; init; } = DraftStatus.Editing;
    /// <summary>Set when <see cref="Status"/> is <see cref="DraftStatus.NeedsAttention"/>.</summary>
    public ConflictReason ConflictReason { get; init; } = ConflictReason.None;
    /// <summary>Last local save time.</summary>
    public DateTimeOffset SavedAt { get; init; }

    /// <summary>Composite store key for an instance+action.</summary>
    public string Key => MakeKey(InstanceId, ActionId);

    /// <summary>Builds the composite store key for an instance+action.</summary>
    public static string MakeKey(string instanceId, int actionId) => $"{instanceId}:{actionId}";
}

/// <summary>Feature 152 — a locally cached action form context enabling offline open.</summary>
public sealed record CachedActionContext
{
    /// <summary>The action's instance id.</summary>
    public required string InstanceId { get; init; }
    /// <summary>The action id.</summary>
    public required int ActionId { get; init; }
    /// <summary>Blueprint id (for re-resolution).</summary>
    public required string BlueprintId { get; init; }
    /// <summary>The action definition JSON (schema + layout) to render offline.</summary>
    public required string ActionJson { get; init; }
    /// <summary>Register the action submits to.</summary>
    public string RegisterId { get; init; } = string.Empty;
    /// <summary>The citizen's own wallet address (open-participant sender).</summary>
    public string SenderWallet { get; init; } = string.Empty;
    /// <summary>User-facing title.</summary>
    public string Title { get; init; } = string.Empty;
    /// <summary>When this context was cached (freshness).</summary>
    public DateTimeOffset CachedAt { get; init; }

    /// <summary>Composite store key.</summary>
    public string Key => ActionDraft.MakeKey(InstanceId, ActionId);
}

/// <summary>Feature 152 — lifecycle state of a queued (deferred) submission.</summary>
public enum QueuedSubmissionState
{
    /// <summary>Awaiting a flush.</summary>
    Queued = 0,
    /// <summary>Currently being sent.</summary>
    Submitting = 1,
    /// <summary>Accepted by the server.</summary>
    Submitted = 2,
    /// <summary>Held — could not be applied; see <see cref="QueuedSubmission.ConflictReason"/>.</summary>
    NeedsAttention = 3,
}

/// <summary>
/// Feature 152 — a completed action awaiting submission (outbox). Persisted encrypted; keyed by an
/// autoincrement id assigned by the store.
/// </summary>
public sealed record QueuedSubmission
{
    /// <summary>Store key (a GUID string assigned on enqueue; empty until persisted).</summary>
    public string QueuedKey { get; init; } = string.Empty;
    /// <summary>Target instance id.</summary>
    public required string InstanceId { get; init; }
    /// <summary>Target action id.</summary>
    public required int ActionId { get; init; }
    /// <summary>Blueprint id.</summary>
    public required string BlueprintId { get; init; }
    /// <summary>Register address.</summary>
    public string RegisterId { get; init; } = string.Empty;
    /// <summary>The citizen's sender wallet.</summary>
    public string SenderWallet { get; init; } = string.Empty;
    /// <summary>Nested submission payload (as the execute body expects).</summary>
    public Dictionary<string, object?> Payload { get; init; } = new();
    /// <summary>Captured media to submit as attachments.</summary>
    public List<DraftMedia> Attachments { get; init; } = new();
    /// <summary>Reused server idempotency key so a re-flush cannot double-submit.</summary>
    public string IdempotencyKey { get; init; } = string.Empty;
    /// <summary>Current state.</summary>
    public QueuedSubmissionState State { get; init; } = QueuedSubmissionState.Queued;
    /// <summary>Why it is held, when <see cref="State"/> is <see cref="QueuedSubmissionState.NeedsAttention"/>.</summary>
    public ConflictReason ConflictReason { get; init; } = ConflictReason.None;
    /// <summary>Flush attempts (for backoff).</summary>
    public int Attempts { get; init; }
    /// <summary>Last transient error / conflict detail.</summary>
    public string? LastError { get; init; }
}
