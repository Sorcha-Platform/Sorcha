// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json.Nodes;

namespace Sorcha.Agent.Decision.Checks;

/// <summary>
/// One banded range for a numeric answer. <paramref name="Max"/> is an INCLUSIVE upper bound;
/// a null <paramref name="Max"/> is the catch-all and must be last.
/// </summary>
public sealed record ScoreRange(int? Max, int Points);

/// <summary>
/// Sums a questionnaire into a single numeric fact. Two scoring modes, because the two answer
/// shapes differ: <c>answers</c> maps an exact submitted string to points (graded multiple
/// choice), <c>ranges</c> maps a submitted number into a band (slider).
///
/// There is deliberately no "could not score" outcome. Every question is schema-<c>required</c>,
/// so the validator guarantees the answers are present before the agent sees the payload, and an
/// unrecognised or missing answer simply scores 0. A hard fault is contained by
/// <see cref="ExternalCheckRunner"/> into a boolean false, which JSON Logic coerces to 0 — so a
/// broken scorer lands in the lowest band and issues nothing.
/// </summary>
public sealed class ScoredQuestionnaireCheck : IExternalCheck
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> _answers;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ScoreRange>> _ranges;

    /// <summary>Creates the scorer.</summary>
    /// <param name="name">Fact key (e.g. <c>cyberScore</c>).</param>
    /// <param name="answers">JSON-Pointer → (exact answer string → points).</param>
    /// <param name="ranges">JSON-Pointer → ordered inclusive-upper-bound ranges.</param>
    public ScoredQuestionnaireCheck(
        string name,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> answers,
        IReadOnlyDictionary<string, IReadOnlyList<ScoreRange>> ranges)
    {
        Name = name;
        _answers = answers ?? throw new ArgumentNullException(nameof(answers));
        _ranges = ranges ?? throw new ArgumentNullException(nameof(ranges));
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public Task<ExternalCheckResult> EvaluateAsync(
        IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
    {
        var total = 0;
        var breakdown = new List<string>();

        foreach (var (pointer, table) in _answers)
        {
            var answer = PayloadPointer.ResolveString(payload, pointer);
            var points = answer is not null && table.TryGetValue(answer, out var p) ? p : 0;
            total += points;
            breakdown.Add($"{pointer}={points}");
        }

        foreach (var (pointer, bands) in _ranges)
        {
            var points = ScoreRangeValue(PayloadPointer.Resolve(payload, pointer), bands);
            total += points;
            breakdown.Add($"{pointer}={points}");
        }

        var detail = $"score {total} ({string.Join(", ", breakdown)})";
        return Task.FromResult(new ExternalCheckResult(Name, true, detail, total));
    }

    private static int ScoreRangeValue(JsonNode? node, IReadOnlyList<ScoreRange> bands)
    {
        if (node is not JsonValue value || !TryReadInt(value, out var submitted))
        {
            // Absent or non-numeric: score the catch-all, or 0 when none is declared.
            return bands.FirstOrDefault(b => b.Max is null)?.Points ?? 0;
        }

        foreach (var band in bands)
        {
            if (band.Max is null || submitted <= band.Max.Value)
                return band.Points;
        }

        return 0;
    }

    private static bool TryReadInt(JsonValue value, out int result)
    {
        if (value.TryGetValue(out int i)) { result = i; return true; }
        if (value.TryGetValue(out double d)) { result = (int)Math.Round(d); return true; }
        result = 0;
        return false;
    }
}
