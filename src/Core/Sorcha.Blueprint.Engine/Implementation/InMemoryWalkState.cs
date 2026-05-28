// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Engine.Interfaces;

namespace Sorcha.Blueprint.Engine.Implementation;

/// <summary>
/// Engine-local, dependency-free, in-memory holder for the accumulated state of a single
/// dry-run walk-through (Feature 142 / D3 / T023). It records what each completed action
/// submitted (payload + calculated values) and tracks the current action pointer, so that
/// routing and disclosure can see prior-action data across steps — the same accumulated
/// state that <c>ActionExecutionService</c> reconstructs from sealed transactions server-side,
/// here reconstructed purely in memory.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately <b>not</b> the Blueprint Service <c>IInstanceStore</c>/<c>IActionStore</c>
/// (which live above the Engine and pull HTTP/encryption/transaction dependencies the Engine
/// cannot reference). It is a minimal engine-local abstraction (<see cref="IWalkState"/>) holding
/// a single flat dictionary of accumulated fields, matching how the engine's routing/calculation
/// pipeline consumes state: a flat <see cref="Dictionary{TKey,TValue}"/> of <c>string</c>→<c>object</c>
/// merged with the current action's payload before routing (see <c>ActionExecutionService</c> step 7).
/// </para>
/// <para>
/// WASM-safe: no <c>HttpClient</c>, no platform APIs, no I/O. Not thread-safe — a walk-through is
/// driven sequentially, one step at a time.
/// </para>
/// </remarks>
public sealed class InMemoryWalkState : IWalkState
{
    // Flat accumulated state across all completed actions (payload + calculations), keyed by field.
    // Later actions' values overwrite earlier ones, mirroring ActionExecutionService's merge order.
    private readonly Dictionary<string, object> _accumulated = new(StringComparer.Ordinal);

    // Per-action record of what that action contributed, for inspection / step modelling.
    private readonly Dictionary<int, IReadOnlyDictionary<string, object>> _byAction = new();

    // Ordered list of action ids that have been recorded, in completion order.
    private readonly List<int> _completedOrder = new();

    /// <inheritdoc />
    public int? CurrentActionId { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<int> CompletedActionIds => _completedOrder;

    /// <inheritdoc />
    public void SetCurrentAction(int actionId) => CurrentActionId = actionId;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object> GetAccumulatedState()
        => new Dictionary<string, object>(_accumulated, StringComparer.Ordinal);

    /// <inheritdoc />
    public Dictionary<string, object> BuildMergedInput(IReadOnlyDictionary<string, object> submittedPayload)
    {
        ArgumentNullException.ThrowIfNull(submittedPayload);

        // Prior accumulated state first, then the current submission overwrites — exactly the
        // order ActionExecutionService uses (accumulated state, then request.PayloadData).
        var merged = new Dictionary<string, object>(_accumulated, StringComparer.Ordinal);
        foreach (var kvp in submittedPayload)
        {
            merged[kvp.Key] = kvp.Value;
        }

        return merged;
    }

    /// <inheritdoc />
    public void RecordCompletedAction(int actionId, IReadOnlyDictionary<string, object> processedData)
    {
        ArgumentNullException.ThrowIfNull(processedData);

        var snapshot = new Dictionary<string, object>(processedData, StringComparer.Ordinal);
        _byAction[actionId] = snapshot;

        if (!_completedOrder.Contains(actionId))
        {
            _completedOrder.Add(actionId);
        }

        // Fold the action's processed data (submitted payload + calculations) into the running state.
        foreach (var kvp in snapshot)
        {
            _accumulated[kvp.Key] = kvp.Value;
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object>? GetActionData(int actionId)
        => _byAction.TryGetValue(actionId, out var data) ? data : null;

    /// <inheritdoc />
    public void Reset()
    {
        _accumulated.Clear();
        _byAction.Clear();
        _completedOrder.Clear();
        CurrentActionId = null;
    }
}
