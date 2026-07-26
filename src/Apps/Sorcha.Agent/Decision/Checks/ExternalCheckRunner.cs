// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Microsoft.Extensions.Logging;

namespace Sorcha.Agent.Decision.Checks;

/// <summary>
/// Runs the configured external checks and merges their results into a flat fact dictionary that
/// <see cref="RulesDecisionEngine"/> exposes under the <c>checks</c> key BEFORE evaluating JSON
/// Logic. Checks run concurrently; an individual check that faults unexpectedly resolves to a safe
/// default (its <c>Value=false</c>) and is logged — it never crashes the decision.
/// </summary>
public sealed class ExternalCheckRunner : IExternalCheckRunner
{
    private readonly IReadOnlyList<IExternalCheck> _checks;
    private readonly ILogger? _logger;

    /// <summary>Creates a runner over <paramref name="checks"/>.</summary>
    public ExternalCheckRunner(IEnumerable<IExternalCheck> checks, ILogger? logger = null)
    {
        _checks = checks?.ToArray() ?? [];
        _logger = logger;
    }

    /// <summary>True when at least one check is configured.</summary>
    public bool HasChecks => _checks.Count > 0;

    /// <summary>
    /// Runs every configured check against <paramref name="payload"/> and returns the merged facts:
    /// <c>{ "postcodeExists": true, "postcodeExistsDetail": "...", "profane": false, ... }</c>.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, object?>> RunAsync(
        IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
    {
        var results = await Task.WhenAll(_checks.Select(check => SafeEvaluateAsync(check, payload, ct)));

        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            // A numeric check merges its number; everything else merges its boolean. This is an
            // EITHER/OR per fact key, not an either-and-both: when Numeric is set, Value is NOT
            // separately reachable from rules — a check that sets both a meaningful boolean and a
            // number loses the boolean entirely. That's intentional (one fact key per check name),
            // not a defect; see the Value/Numeric XML docs on ExternalCheckResult. A check that needs
            // both visible to rules must expose them as two distinct fact keys.
            //
            // A check that faults is contained by SafeEvaluateAsync into a boolean false (Numeric
            // null), which JSON Logic coerces to 0 — so a broken scorer lands in the lowest band
            // rather than passing.
            merged[result.Name] = result.Numeric.HasValue
                ? result.Numeric.Value
                : result.Value;
            if (result.Detail is not null)
                merged[$"{result.Name}Detail"] = result.Detail;
        }

        return merged;
    }

    private async Task<ExternalCheckResult> SafeEvaluateAsync(
        IExternalCheck check, IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
    {
        try
        {
            return await check.EvaluateAsync(payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger?.LogWarning(ex, "External check '{Check}' faulted; resolving to false", check.Name);
            return new ExternalCheckResult(check.Name, false);
        }
    }
}
