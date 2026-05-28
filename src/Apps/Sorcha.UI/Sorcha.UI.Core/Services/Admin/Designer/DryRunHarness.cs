// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Engine.Models;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using BlueprintAction = Sorcha.Blueprint.Models.Action;

namespace Sorcha.UI.Core.Services.Designer;

/// <summary>
/// Designer-only, in-WASM quick dry-run harness for the Feature 142 "Rehearse" stage (D3 / T024).
/// Given a <see cref="BlueprintModel"/>, it drives the portable <see cref="IExecutionEngine"/>
/// step-by-step — validate → calculate → route → disclose — entirely in the browser with NO
/// register and NO backend round-trip, accumulating the walk-through's state in memory via the
/// engine-local <see cref="IWalkState"/> (T023). It exposes a per-step model the designer UI
/// (T025/T031) renders, aligned with the data-model <c>RehearsalStep</c> shape.
/// </summary>
/// <remarks>
/// <para>
/// Scope (Clarification Q3 / FR-018): the dry-run covers schema validation, calculations, routing
/// and disclosure ONLY. It does NOT exercise credential prerequisites or credential issuance — when
/// an action involves credentials, the produced step is flagged
/// <see cref="DryRunStepper.CheckedInFullRehearsalNote"/> ("checked in full rehearsal") rather than
/// executed, so the dry-run is never mistaken for full fidelity. The cryptographic / delivery
/// behaviour belongs to the full rehearsal (D1/D2).
/// </para>
/// <para>
/// Pure in-memory: no <c>HttpClient</c>, no platform APIs. A harness instance drives one walk-through
/// at a time; call <see cref="Start"/> (or <see cref="Reset"/>) to begin a fresh run.
/// </para>
/// </remarks>
public sealed class DryRunHarness : IDryRunHarness
{
    private readonly IExecutionEngine _engine;
    private readonly InMemoryWalkState _walkState = new();
    private readonly List<DryRunStep> _steps = new();

    private BlueprintModel? _blueprint;
    private DryRunStepper? _stepper;
    private BlueprintAction? _currentAction;

    /// <summary>
    /// Creates a harness over a portable execution engine. The engine is dependency-free and
    /// WASM-safe; use <see cref="CreateDefault"/> when no engine is registered in DI.
    /// </summary>
    /// <param name="engine">The portable blueprint execution engine.</param>
    public DryRunHarness(IExecutionEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <summary>
    /// Builds a <see cref="DryRunHarness"/> with a freshly-composed default engine. Convenient for
    /// the designer client where the engine is not otherwise registered. The engine has no I/O
    /// dependencies, so this is safe to call in WASM.
    /// </summary>
    public static DryRunHarness CreateDefault()
    {
        var jsonLogic = new JsonLogicEvaluator();
        var schema = new SchemaValidator();
        var disclosure = new DisclosureProcessor();
        var routing = new RoutingEngine(jsonLogic);
        var processor = new ActionProcessor(schema, jsonLogic, disclosure, routing);
        var engine = new ExecutionEngine(processor, schema, jsonLogic, disclosure, routing);
        return new DryRunHarness(engine);
    }

    /// <inheritdoc />
    public IReadOnlyList<DryRunStep> Steps => _steps;

    /// <inheritdoc />
    public int? CurrentActionId => _currentAction?.Id;

    /// <inheritdoc />
    public string? CurrentActingRole => _currentAction?.Sender;

    /// <inheritdoc />
    public bool IsComplete => _stepper is not null && _currentAction is null;

    /// <inheritdoc />
    public bool IsStarted => _stepper is not null;

    /// <inheritdoc />
    public void Start(BlueprintModel blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);

        _blueprint = blueprint;
        _stepper = new DryRunStepper(_engine, _walkState);
        _walkState.Reset();
        _steps.Clear();
        _currentAction = DryRunStepper.ResolveStartAction(blueprint);
    }

    /// <inheritdoc />
    public async Task<DryRunStep> SubmitCurrentStepAsync(
        IReadOnlyDictionary<string, object> submittedPayload,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(submittedPayload);

        if (_stepper is null || _blueprint is null)
        {
            throw new InvalidOperationException(
                $"Call {nameof(Start)} before submitting a dry-run step.");
        }

        if (_currentAction is null)
        {
            throw new InvalidOperationException(
                "The dry-run walk-through is already complete; call Start or Reset to run again.");
        }

        var step = await _stepper.ProcessStepAsync(_blueprint, _currentAction, submittedPayload, ct);
        _steps.Add(step);

        if (step.Status == DryRunStepStatus.Failed)
        {
            // Validation failed — stay on the current action so the author can fix the payload.
            return step;
        }

        // Advance to the routed next action (null ends the walk-through).
        _currentAction = DryRunStepper.ResolveNextAction(_blueprint, step.RoutingOutcome!);
        return step;
    }

    /// <inheritdoc />
    public void Reset()
    {
        if (_blueprint is null)
        {
            _stepper = null;
            _currentAction = null;
            _steps.Clear();
            _walkState.Reset();
            return;
        }

        Start(_blueprint);
    }
}
