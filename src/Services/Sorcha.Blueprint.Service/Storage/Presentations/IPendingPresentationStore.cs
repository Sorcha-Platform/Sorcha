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
    /// <param name="validityWindowSeconds">The pending-presentation validity window
    /// for this attempt. Sentinel TTL overshoots this by an implementation-defined
    /// grace (1h) so late callbacks after abandonment still find the sentinel.</param>
    Task<bool> TryClaimOutcomeSentinelAsync(
        Guid presentationRequestId,
        string claimantValue,
        int validityWindowSeconds,
        CancellationToken ct = default);

    /// <summary>
    /// Read the current outcome sentinel value. Returns null if unset.
    /// </summary>
    /// <remarks>
    /// Known values:
    /// <list type="bullet">
    ///   <item><c>outcome-pending-write</c> — writer claimed; inline submission in flight (Feature 111).</item>
    ///   <item><c>success</c>, <c>decline</c> — terminal outcome (Feature 111).</item>
    ///   <item><c>abandoned</c>, <c>abandoned+outcome</c> — terminal abandonment, optionally with a late outcome (Feature 111).</item>
    ///   <item><c>outcome-pending-seal</c> — writer claimed; outcome submission deferred until predecessor seals (Feature 119).</item>
    ///   <item><c>failed-predecessor-not-sealed</c> — never-seals timeout fired by the recovery sweeper (Feature 119).</item>
    ///   <item><c>failed-validator-reject</c> — should-not-happen path: queued tx rejected on drain (Feature 119).</item>
    /// </list>
    /// </remarks>
    Task<string?> GetOutcomeSentinelAsync(Guid presentationRequestId, CancellationToken ct = default);

    /// <summary>
    /// Force-set the sentinel value (used to escalate "outcome-pending-write" to
    /// "success"/"decline" after the outcome tx is written, or to mark
    /// "abandoned+outcome" after a late callback).
    /// </summary>
    /// <param name="validityWindowSeconds">The pending-presentation validity window
    /// for this attempt. Sentinel TTL overshoots this by an implementation-defined
    /// grace (1h).</param>
    Task SetOutcomeSentinelAsync(
        Guid presentationRequestId,
        string value,
        int validityWindowSeconds,
        CancellationToken ct = default);

    /// <summary>
    /// Delete the outcome sentinel. Used to roll back a claim when the subsequent
    /// transaction write failed, so later callers aren't misled by a sentinel
    /// whose corresponding register transaction never actually landed.
    /// </summary>
    Task DeleteOutcomeSentinelAsync(Guid presentationRequestId, CancellationToken ct = default);

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

    /// <summary>
    /// TxId of the PresentationInitiated transaction. Used as
    /// previousTransactionId when the later PresentationOutcome or
    /// PresentationAbandoned tx is written — preserves chain integrity on the
    /// register. Set after the initiated tx is submitted.
    /// </summary>
    public string? InitiatedTransactionId { get; init; }

    // ── #1195 Phase 2 (Task 6b, T032) — verifier-session fields ─────────────────────────
    // Persisted at initiation so the callback path can rebuild the VerifierSession the
    // sorcha-wallet consumer's validator needs (nonce echo, vct + required-claim checks,
    // KB-JWT audience binding). All nullable: legacy pending entries written before the
    // session wiring deserialize with nulls and keep their previous behaviour
    // (session-missing decline). Single-use + TTL-bound by construction — the pending row
    // IS the session record (deleted post-outcome; Redis TTL = validity window).

    /// <summary>The nonce the consumer embedded in the request object; the KB-JWT must echo it.</summary>
    public string? Nonce { get; init; }

    /// <summary>The verifier client_id served in the request object (null ⇒ the consumer's placeholder fallback).</summary>
    public string? VerifierClientId { get; init; }

    /// <summary>Required credential type (vct) from the gating requirement.</summary>
    public string? CredentialType { get; init; }

    /// <summary>Required claim names from the gating requirement.</summary>
    public List<string>? RequiredClaimNames { get; init; }
}
