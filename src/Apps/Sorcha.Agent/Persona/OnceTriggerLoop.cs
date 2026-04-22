// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;

namespace Sorcha.Agent.Persona;

/// <summary>
/// Fires a persona exactly once (after an optional startup delay) then exits.
/// Used for walkthrough-starting-action kickoff (Feature 110 User Story 1).
/// </summary>
public sealed class OnceTriggerLoop : IPersonaLoop
{
    private readonly PersonaDefinition _definition;
    private readonly OnceTrigger _trigger;
    private readonly IPersonaSubmitter _submitter;
    private readonly IPayloadTokenResolver _resolver;
    private readonly IRandomSource _random;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OnceTriggerLoop> _logger;
    private int _completed;

    public OnceTriggerLoop(
        PersonaDefinition definition,
        OnceTrigger trigger,
        IPersonaSubmitter submitter,
        IPayloadTokenResolver resolver,
        IRandomSource random,
        TimeProvider timeProvider,
        ILogger<OnceTriggerLoop> logger)
    {
        _definition = definition;
        _trigger = trigger;
        _submitter = submitter;
        _resolver = resolver;
        _random = random;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public int CompletedIterations => _completed;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_trigger.DelaySeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(_trigger.DelaySeconds), _timeProvider, cancellationToken);
        }

        var ctx = new PersonaFireContext
        {
            Iteration = 1,
            Now = _timeProvider.GetUtcNow(),
            RandomSource = _random
        };

        var payload = _resolver.Resolve(_definition.PayloadTemplate, ctx);

        _logger.LogInformation(
            "Persona {PersonaName} fire #1 -> blueprint={BlueprintId} action={ActionName}",
            _definition.Name, _definition.Target.BlueprintId,
            _definition.Target.ActionName ?? $"(index {_definition.Target.ActionIndex})");

        var result = await _submitter.SubmitAsync(_definition, payload, cancellationToken);

        switch (result.Outcome)
        {
            case PersonaSubmissionOutcome.Submitted:
                _completed = 1;
                _logger.LogInformation(
                    "Persona {PersonaName} fire #1 -> Submitted ({DurationMs} ms)",
                    _definition.Name, result.DurationMs);
                break;
            case PersonaSubmissionOutcome.TransientFailure:
                // Once-trigger does not retry. For walkthrough/demo contexts a startup 429 or 503
                // that silently produces 0 submissions is confusing, so this is logged at Error
                // level (not Warning) to surface loudly alongside hard failures.
                _logger.LogError(
                    "Persona {PersonaName} fire #1 -> TransientFailure: {Error} — once-trigger does not retry; walkthrough will not progress without manual intervention",
                    _definition.Name, result.Error);
                break;
            case PersonaSubmissionOutcome.HardFailure:
                _logger.LogError(
                    "Persona {PersonaName} fire #1 -> HardFailure: {Error}",
                    _definition.Name, result.Error);
                break;
        }
    }
}
