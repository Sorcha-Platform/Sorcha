// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;

namespace Sorcha.Agent.Persona;

/// <summary>
/// Composes a <see cref="PersonaDefinition"/> with the correct <see cref="IPersonaLoop"/>
/// implementation based on trigger kind. Launched as a peer <see cref="Task"/> from
/// <see cref="Commands.RunCommand"/>, alongside the reactive inbox loop.
/// </summary>
public sealed class PersonaHost
{
    // Feature 110 note: the AuditLogger integration described in research.md R-007
    // is deferred — persona fires are observable via structured ILogger output but
    // do not currently land in the agent's *.jsonl audit stream alongside reactive
    // decisions. Tracked on GAP-021 in MASTER-TASKS.md for a follow-up.
    private readonly PersonaDefinition _definition;
    private readonly IPersonaSubmitter _submitter;
    private readonly IPayloadTokenResolver _resolver;
    private readonly IRandomSource _random;
    private readonly TimeProvider _timeProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PersonaHost> _logger;
    private IPersonaLoop? _loop;

    public PersonaHost(
        PersonaDefinition definition,
        IPersonaSubmitter submitter,
        IPayloadTokenResolver resolver,
        IRandomSource random,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        _definition = definition;
        _submitter = submitter;
        _resolver = resolver;
        _random = random;
        _timeProvider = timeProvider;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<PersonaHost>();
    }

    public int CompletedIterations => _loop?.CompletedIterations ?? 0;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _loop = _definition.Trigger switch
        {
            OnceTrigger once => new OnceTriggerLoop(
                _definition, once, _submitter, _resolver, _random, _timeProvider,
                _loggerFactory.CreateLogger<OnceTriggerLoop>()),
            IntervalTrigger interval => new IntervalTriggerLoop(
                _definition, interval, _submitter, _resolver, _random, _timeProvider,
                _loggerFactory.CreateLogger<IntervalTriggerLoop>()),
            _ => throw new NotSupportedException($"Trigger kind '{_definition.Trigger.GetType().Name}' not supported")
        };

        _logger.LogInformation("Persona {PersonaName} starting (trigger={TriggerKind})",
            _definition.Name, _definition.Trigger.GetType().Name);

        try
        {
            await _loop.RunAsync(cancellationToken);
            if (_loop.CompletedIterations == 0)
            {
                _logger.LogWarning(
                    "Persona {PersonaName} exited without any successful submissions — check earlier log lines for the failure reason",
                    _definition.Name);
            }
            else
            {
                _logger.LogInformation("Persona {PersonaName} completed after {Iterations} iteration(s)",
                    _definition.Name, _loop.CompletedIterations);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Persona {PersonaName} cancelled after {Iterations} iteration(s)",
                _definition.Name, _loop.CompletedIterations);
        }
    }
}
