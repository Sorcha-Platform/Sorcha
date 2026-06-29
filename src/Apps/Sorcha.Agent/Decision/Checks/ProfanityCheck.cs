// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.RegularExpressions;

namespace Sorcha.Agent.Decision.Checks;

/// <summary>
/// Local, offline, dependency-free profanity scan of the configured free-text fields against a
/// bundled wordlist. <c>Value=true</c> if any whole-word match is found (case-insensitive).
/// <c>Detail</c> carries the matched term so the rejection copy can be specific. Nested fields
/// (e.g. an address object) are flattened to their string scalars before scanning.
/// </summary>
public sealed class ProfanityCheck : IExternalCheck
{
    private readonly string[] _fields;
    private readonly string[] _wordlist;

    /// <summary>Creates the check over <paramref name="fields"/> using <paramref name="wordlist"/>.</summary>
    /// <param name="name">Fact key (e.g. <c>profane</c>).</param>
    /// <param name="fields">JSON-Pointers to the free-text fields to scan.</param>
    /// <param name="wordlist">Lower-cased terms that constitute profanity/abuse.</param>
    public ProfanityCheck(string name, IEnumerable<string> fields, IEnumerable<string> wordlist)
    {
        Name = name;
        _fields = fields?.ToArray() ?? [];
        _wordlist = (wordlist ?? [])
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(w => w.Trim().ToLowerInvariant())
            .Distinct()
            .ToArray();
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public Task<ExternalCheckResult> EvaluateAsync(IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
    {
        if (_wordlist.Length == 0 || _fields.Length == 0)
            return Task.FromResult(new ExternalCheckResult(Name, false));

        foreach (var field in _fields)
        {
            var text = PayloadPointer.FlattenText(PayloadPointer.Resolve(payload, field));
            if (string.IsNullOrWhiteSpace(text))
                continue;

            foreach (var word in _wordlist)
            {
                // Whole-word, case-insensitive match so "Scunthorpe" doesn't trip a substring filter.
                if (Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase))
                    return Task.FromResult(new ExternalCheckResult(Name, true, word));
            }
        }

        return Task.FromResult(new ExternalCheckResult(Name, false));
    }
}
