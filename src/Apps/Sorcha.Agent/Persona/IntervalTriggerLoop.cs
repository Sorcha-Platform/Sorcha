// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Sorcha.Agent.Persona;

/// <summary>
/// Fires a persona repeatedly at a declared interval, bounded by
/// <see cref="IntervalTrigger.MaxIterations"/> and/or <see cref="IntervalTrigger.Until"/>.
/// Used for scenario register-data generation (Feature 110 User Story 2).
/// </summary>
public sealed class IntervalTriggerLoop : IPersonaLoop
{
    private const int HardFailureStrikeLimit = 3;

    private readonly PersonaDefinition _definition;
    private readonly IntervalTrigger _trigger;
    private readonly IPersonaSubmitter _submitter;
    private readonly IPayloadTokenResolver _resolver;
    private readonly IRandomSource _random;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IntervalTriggerLoop> _logger;
    private int _completed;

    public IntervalTriggerLoop(
        PersonaDefinition definition,
        IntervalTrigger trigger,
        IPersonaSubmitter submitter,
        IPayloadTokenResolver resolver,
        IRandomSource random,
        TimeProvider timeProvider,
        ILogger<IntervalTriggerLoop> logger)
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
        if (_trigger.Until is DateTimeOffset until && until <= _timeProvider.GetUtcNow())
        {
            _logger.LogWarning(
                "Persona {PersonaName} interval 'until' ({Until}) is in the past — exiting without firing",
                _definition.Name, until);
            return;
        }

        if (_trigger.StartDelaySeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(_trigger.StartDelaySeconds), _timeProvider, cancellationToken);

        var intervalSec = _trigger.IntervalSeconds;
        var strikes = 0;
        var counter = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_trigger.MaxIterations is int max && _completed >= max)
            {
                _logger.LogInformation(
                    "Persona {PersonaName} reached maxIterations={MaxIterations}; exiting",
                    _definition.Name, max);
                return;
            }
            if (_trigger.Until is DateTimeOffset untilTs && _timeProvider.GetUtcNow() >= untilTs)
            {
                _logger.LogInformation(
                    "Persona {PersonaName} passed 'until' ({Until}); exiting after {Iterations} iteration(s)",
                    _definition.Name, untilTs, _completed);
                return;
            }

            counter++;
            var ctx = new PersonaFireContext
            {
                Iteration = counter,
                Now = _timeProvider.GetUtcNow(),
                RandomSource = _random
            };

            JsonObject payload;
            try
            {
                payload = _resolver.Resolve(_definition.PayloadTemplate, ctx);
            }
            catch (InvalidOperationException ex)
            {
                // Load-time validation should have caught this; treat as fatal.
                _logger.LogError(ex, "Persona {PersonaName} payload resolution failed — exiting", _definition.Name);
                return;
            }

            _logger.LogInformation(
                "Persona {PersonaName} fire #{Iteration} -> blueprint={BlueprintId}",
                _definition.Name, counter, _definition.Target.BlueprintId);

            var result = await _submitter.SubmitAsync(_definition, payload, cancellationToken);

            switch (result.Outcome)
            {
                case PersonaSubmissionOutcome.Submitted:
                    _completed++;
                    strikes = 0;
                    _logger.LogInformation(
                        "Persona {PersonaName} fire #{Iteration} -> Submitted ({DurationMs} ms)",
                        _definition.Name, counter, result.DurationMs);
                    break;
                case PersonaSubmissionOutcome.TransientFailure:
                    counter--; // do not advance counter on transient failure
                    _logger.LogWarning(
                        "Persona {PersonaName} transient failure: {Error}",
                        _definition.Name, result.Error);
                    break;
                case PersonaSubmissionOutcome.HardFailure:
                    strikes++;
                    _logger.LogError(
                        "Persona {PersonaName} hard failure ({Strikes}/{Limit}): {Error}",
                        _definition.Name, strikes, HardFailureStrikeLimit, result.Error);
                    if (strikes >= HardFailureStrikeLimit)
                    {
                        _logger.LogError(
                            "Persona {PersonaName} exceeded hard-failure strike limit; exiting persona loop",
                            _definition.Name);
                        return;
                    }
                    break;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSec), _timeProvider, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
