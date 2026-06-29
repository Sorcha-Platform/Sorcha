// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Agent.Decision.Checks;

/// <summary>
/// Runs a set of external checks against an action payload and returns the merged fact dictionary.
/// Consumed by <see cref="Decision.RulesDecisionEngine"/> to populate the <c>checks</c> context
/// key before JSON Logic evaluation. The interface enables test injection of faulting runners.
/// </summary>
public interface IExternalCheckRunner
{
    /// <summary>True when at least one check is configured.</summary>
    bool HasChecks { get; }

    /// <summary>
    /// Runs every configured check against <paramref name="payload"/> and returns the merged facts.
    /// Individual check faults are contained and resolved to <c>false</c> by the implementation;
    /// runner-infrastructure faults propagate as exceptions.
    /// </summary>
    Task<IReadOnlyDictionary<string, object?>> RunAsync(
        IReadOnlyDictionary<string, object?> payload, CancellationToken ct);
}
