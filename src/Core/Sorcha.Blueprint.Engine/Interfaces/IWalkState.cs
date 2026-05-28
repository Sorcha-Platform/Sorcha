// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Engine.Interfaces;

/// <summary>
/// Engine-local accumulated-state holder for a single dry-run walk-through (Feature 142 / T023).
/// Holds the submitted payloads + calculations of completed actions plus the current action pointer
/// so that the validate → calculate → route → disclose pipeline can see prior-action data across a
/// multi-step walk — the in-memory equivalent of the server's state reconstruction from sealed
/// transactions.
/// </summary>
/// <remarks>
/// This abstraction is intentionally minimal and dependency-free so it can run in Blazor WASM with
/// no backend round-trip. It is <b>not</b> the Blueprint Service <c>IInstanceStore</c>/<c>IActionStore</c>
/// (which sit above the Engine and cannot be referenced from here).
/// </remarks>
public interface IWalkState
{
    /// <summary>The action the walk-through is currently positioned on, or null before the first step.</summary>
    int? CurrentActionId { get; }

    /// <summary>Action ids recorded as completed, in completion order.</summary>
    IReadOnlyList<int> CompletedActionIds { get; }

    /// <summary>Moves the walk-through pointer to <paramref name="actionId"/>.</summary>
    void SetCurrentAction(int actionId);

    /// <summary>
    /// Returns a snapshot copy of the flat accumulated state folded from all completed actions
    /// (submitted payload + calculated values). Later actions overwrite earlier fields.
    /// </summary>
    IReadOnlyDictionary<string, object> GetAccumulatedState();

    /// <summary>
    /// Produces the input dictionary for processing the current action: prior accumulated state
    /// with <paramref name="submittedPayload"/> merged on top (the submission wins on conflict),
    /// matching the server merge order.
    /// </summary>
    /// <param name="submittedPayload">The payload the acting role submits for the current step.</param>
    Dictionary<string, object> BuildMergedInput(IReadOnlyDictionary<string, object> submittedPayload);

    /// <summary>
    /// Records the result of completing <paramref name="actionId"/> — the engine's processed data
    /// (submitted payload + calculations) — folding it into the running accumulated state.
    /// </summary>
    /// <param name="actionId">The completed action's id.</param>
    /// <param name="processedData">The processed data the engine produced for the action.</param>
    void RecordCompletedAction(int actionId, IReadOnlyDictionary<string, object> processedData);

    /// <summary>Returns the recorded processed data for <paramref name="actionId"/>, or null if not completed.</summary>
    IReadOnlyDictionary<string, object>? GetActionData(int actionId);

    /// <summary>Discards all accumulated state and resets the pointer (re-run a fresh walk-through).</summary>
    void Reset();
}
