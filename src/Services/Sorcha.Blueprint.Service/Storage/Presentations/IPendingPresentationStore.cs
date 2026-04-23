// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Storage.Presentations;

/// <summary>
/// Transient store for pending presentation attempts (Feature 111). Maps
/// presentationRequestId to the originating action context; Redis-backed with
/// TTL = the blueprint's validity window.
/// </summary>
public interface IPendingPresentationStore
{
    /// <summary>
    /// Store a pending attempt. TTL = validityWindowSeconds.
    /// </summary>
    Task StoreAsync(PendingPresentation pending, CancellationToken ct = default);

    /// <summary>
    /// Retrieve a pending attempt. Returns null if expired, unknown, or deleted.
    /// </summary>
    Task<PendingPresentation?> GetAsync(Guid presentationRequestId, CancellationToken ct = default);

    /// <summary>
    /// Delete the pending hash (post-outcome or post-abandonment cleanup).
    /// </summary>
    Task DeleteAsync(Guid presentationRequestId, CancellationToken ct = default);

    /// <summary>
    /// Atomically claim the outcome sentinel for a requestId via SET NX. Returns
    /// true if the caller is the first writer (expected to proceed with writing
    /// the outcome transaction); false if another party already claimed it.
    /// </summary>
    Task<bool> TryClaimOutcomeSentinelAsync(Guid presentationRequestId, string claimantValue, CancellationToken ct = default);

    /// <summary>
    /// Read the current outcome sentinel value. Returns null if unset.
    /// Values: "outcome-pending-write", "success", "decline", "abandoned", "abandoned+outcome".
    /// </summary>
    Task<string?> GetOutcomeSentinelAsync(Guid presentationRequestId, CancellationToken ct = default);

    /// <summary>
    /// Force-set the sentinel value (used to escalate "outcome-pending-write" to
    /// "success"/"decline" after the outcome tx is written, or to mark
    /// "abandoned+outcome" after a late callback).
    /// </summary>
    Task SetOutcomeSentinelAsync(Guid presentationRequestId, string value, CancellationToken ct = default);

    /// <summary>
    /// List keys of pending attempts whose TTL is at or below the given threshold,
    /// for the abandonment sweeper. Callers should treat the set as a snapshot —
    /// entries may have expired by the time the caller acts on them.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListPendingNearExpiryAsync(TimeSpan withinDuration, int max, CancellationToken ct = default);
}

/// <summary>
/// Snapshot of the pending-presentation state stored against a requestId.
/// </summary>
public sealed record PendingPresentation
{
    public required Guid PresentationRequestId { get; init; }
    public required Guid InstanceId { get; init; }
    public required int ActionId { get; init; }
    public required string RegisterId { get; init; }
    public required string BlueprintId { get; init; }
    public required string SubmitterWallet { get; init; }
    public required string ConsumerName { get; init; }

    /// <summary>Non-credential action fields, JSON-encoded for Redis round-trip.</summary>
    public required string DraftPayloadJson { get; init; }

    /// <summary>SHA-256 hex of canonical credential requirements at submission time.</summary>
    public required string CredentialRequirementDigestHex { get; init; }

    /// <summary>Scoped JWT used to resume action execution when the callback arrives.</summary>
    public string? DelegationToken { get; init; }

    public required bool RecordAbandonment { get; init; }
    public required string OutcomeDetailLevel { get; init; }   // "minimal" | "verbose"
    public required int ValidityWindowSeconds { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
