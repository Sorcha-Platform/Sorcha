// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;

namespace Sorcha.Blueprint.Engine.Testing;

/// <summary>
/// Generates a string that MATCHES a regular expression, for the common subset of regex syntax
/// blueprint <c>pattern</c> constraints actually use (#1724).
/// </summary>
/// <remarks>
/// <para>
/// This is a generator, not a matcher — it walks the pattern once, producing text as it goes,
/// rather than building an automaton. That is sufficient for the supported subset: anchors
/// <c>^</c> <c>$</c>, literals, escapes <c>\d \w \s \.</c> (and any other <c>\x</c> as the literal
/// <c>x</c>), character classes <c>[...]</c> with ranges and simple negation, groups <c>(...)</c>
/// with <c>|</c> alternation, and quantifiers <c>? * + {n} {n,} {n,m}</c>.
/// </para>
/// <para>
/// <b>Deliberately unsupported</b>: lookaround <c>(?=...)</c> / <c>(?!...)</c> / <c>(?&lt;=...)</c>,
/// backreferences, and anything else this walk cannot parse. <see cref="TrySample"/> returns
/// <c>null</c> rather than throwing or producing text that does not actually match — the caller
/// (<see cref="SchemaSamplePayloadGenerator"/>) reports that as an unsatisfied field instead of
/// silently shipping a value nobody checked.
/// </para>
/// </remarks>
internal static class RegexSampler
{
    private const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";
    private const string WordChars = Letters + Digits + "_";
    private const string PrintableAscii =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>
    /// Attempts to generate a string matching <paramref name="pattern"/>, drawing choices from
    /// <paramref name="rng"/> so the result is deterministic for a given RNG state. Returns
    /// <c>null</c> when the pattern uses syntax outside the supported subset.
    /// </summary>
    public static string? TrySample(string pattern, Random rng)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        try
        {
            var p = pattern;
            if (p.StartsWith('^'))
            {
                p = p[1..];
            }
            if (p.EndsWith('$') && !p.EndsWith(@"\$", StringComparison.Ordinal))
            {
                p = p[..^1];
            }

            var sb = new StringBuilder();
            var idx = 0;
            if (!GenerateAlternation(p, ref idx, p.Length, rng, sb))
            {
                return null;
            }

            return idx == p.Length ? sb.ToString() : null;
        }
        catch
        {
            // A malformed or exotic pattern is reported by the caller, never thrown from here.
            return null;
        }
    }

    /// <summary>Generates one branch of a <c>|</c>-separated alternation within [start, end).</summary>
    private static bool GenerateAlternation(string p, ref int idx, int end, Random rng, StringBuilder sb)
    {
        var branches = SplitTopLevelAlternatives(p, idx, end);
        if (branches.Count == 0)
        {
            idx = end;
            return true;
        }

        var (branchStart, branchEnd) = branches[rng.Next(branches.Count)];
        var cursor = branchStart;
        while (cursor < branchEnd)
        {
            if (!GenerateAtom(p, ref cursor, branchEnd, rng, sb))
            {
                return false;
            }
        }

        idx = end;
        return true;
    }

    /// <summary>
    /// Finds the <c>|</c>-separated branch spans at THIS nesting level (ignoring <c>|</c> nested
    /// inside <c>[...]</c> or <c>(...)</c>).
    /// </summary>
    private static List<(int Start, int End)> SplitTopLevelAlternatives(string p, int start, int end)
    {
        var branches = new List<(int Start, int End)>();
        var branchStart = start;
        var bracketDepth = 0;
        var groupDepth = 0;
        var i = start;

        while (i < end)
        {
            var c = p[i];
            if (c == '\\')
            {
                i += 2;
                continue;
            }
            if (c == '[')
            {
                bracketDepth++;
            }
            else if (c == ']')
            {
                bracketDepth = Math.Max(0, bracketDepth - 1);
            }
            else if (c == '(' && bracketDepth == 0)
            {
                groupDepth++;
            }
            else if (c == ')' && bracketDepth == 0)
            {
                groupDepth = Math.Max(0, groupDepth - 1);
            }
            else if (c == '|' && bracketDepth == 0 && groupDepth == 0)
            {
                branches.Add((branchStart, i));
                branchStart = i + 1;
            }

            i++;
        }

        branches.Add((branchStart, end));
        return branches;
    }

    /// <summary>Generates one atom (literal / escape / class / group) plus its quantifier.</summary>
    private static bool GenerateAtom(string p, ref int idx, int end, Random rng, StringBuilder sb)
    {
        if (idx >= end)
        {
            return true;
        }

        var c = p[idx];

        if (c == '(')
        {
            var close = FindMatchingParen(p, idx, end);
            if (close < 0)
            {
                return false;
            }

            var innerStart = idx + 1;
            if (innerStart < close && p[innerStart] == '?')
            {
                // Non-capturing / named groups are fine (?:...) / (?<name>...); lookaround and
                // other (?X...) forms are not — bail rather than guess.
                var colon = p.IndexOf(':', innerStart, close - innerStart);
                var nameClose = p[innerStart..close].IndexOf('>');
                if (colon >= 0)
                {
                    innerStart = colon + 1;
                }
                else if (nameClose >= 0)
                {
                    innerStart = innerStart + nameClose + 1;
                }
                else
                {
                    return false;
                }
            }

            idx = close + 1;
            var repeats = ParseQuantifier(p, ref idx, rng);
            for (var n = 0; n < repeats; n++)
            {
                var groupIdx = innerStart;
                if (!GenerateAlternation(p, ref groupIdx, close, rng, sb))
                {
                    return false;
                }
            }
            return true;
        }

        if (c == '[')
        {
            var close = p.IndexOf(']', idx + 1);
            if (close < 0)
            {
                return false;
            }

            var classBody = p[(idx + 1)..close];
            idx = close + 1;
            var repeats = ParseQuantifier(p, ref idx, rng);
            for (var n = 0; n < repeats; n++)
            {
                var ch = SampleCharClass(classBody, rng);
                if (ch is null)
                {
                    return false;
                }
                sb.Append(ch.Value);
            }
            return true;
        }

        if (c == '\\')
        {
            if (idx + 1 >= end)
            {
                return false;
            }
            var esc = p[idx + 1];
            idx += 2;
            var repeats = ParseQuantifier(p, ref idx, rng);
            for (var n = 0; n < repeats; n++)
            {
                sb.Append(SampleEscape(esc, rng));
            }
            return true;
        }

        if (c == '.')
        {
            idx += 1;
            var repeats = ParseQuantifier(p, ref idx, rng);
            for (var n = 0; n < repeats; n++)
            {
                sb.Append(PrintableAscii[rng.Next(PrintableAscii.Length)]);
            }
            return true;
        }

        // Literal character (covers everything not otherwise special in this subset).
        idx += 1;
        var literalRepeats = ParseQuantifier(p, ref idx, rng);
        for (var n = 0; n < literalRepeats; n++)
        {
            sb.Append(c);
        }
        return true;
    }

    private static int FindMatchingParen(string p, int openIdx, int end)
    {
        var depth = 0;
        var i = openIdx;
        while (i < end)
        {
            if (p[i] == '\\')
            {
                i += 2;
                continue;
            }
            if (p[i] == '(')
            {
                depth++;
            }
            else if (p[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
            i++;
        }
        return -1;
    }

    /// <summary>Resolves <c>? * + {n} {n,} {n,m}</c> immediately following an atom into a repeat count.</summary>
    private static int ParseQuantifier(string p, ref int idx, Random rng)
    {
        if (idx >= p.Length)
        {
            return 1;
        }

        switch (p[idx])
        {
            case '?':
                idx++;
                return rng.Next(0, 2);
            case '*':
                idx++;
                return rng.Next(0, 4);
            case '+':
                idx++;
                return rng.Next(1, 4);
            case '{':
                var close = p.IndexOf('}', idx);
                if (close < 0)
                {
                    return 1;
                }
                var body = p[(idx + 1)..close];
                idx = close + 1;
                if (body.Contains(','))
                {
                    var parts = body.Split(',');
                    if (!int.TryParse(parts[0], out var lo))
                    {
                        return 1;
                    }
                    var hi = string.IsNullOrEmpty(parts[1])
                        ? lo + 3
                        : int.TryParse(parts[1], out var parsedHi) ? parsedHi : lo;
                    if (hi < lo)
                    {
                        hi = lo;
                    }
                    return rng.Next(lo, hi + 1);
                }
                return int.TryParse(body, out var exact) ? exact : 1;
            default:
                return 1;
        }
    }

    private static char SampleEscape(char esc, Random rng) => esc switch
    {
        'd' => Digits[rng.Next(Digits.Length)],
        'D' => Letters[rng.Next(Letters.Length)],
        'w' => WordChars[rng.Next(WordChars.Length)],
        'W' => "!@#$%^&*"[rng.Next(8)],
        's' => ' ',
        'S' => Letters[rng.Next(Letters.Length)],
        'n' => '\n',
        't' => '\t',
        _ => esc // \. \/ \( \) \\ etc. — the escaped character itself.
    };

    private static void AddEscapeClassChars(char esc, List<char> pool)
    {
        switch (esc)
        {
            case 'd':
                pool.AddRange(Digits);
                break;
            case 'w':
                pool.AddRange(WordChars);
                break;
            case 's':
                pool.Add(' ');
                pool.Add('\t');
                break;
            default:
                pool.Add(esc);
                break;
        }
    }

    private static char? SampleCharClass(string body, Random rng)
    {
        var negate = body.StartsWith('^');
        if (negate)
        {
            body = body[1..];
        }

        var pool = new List<char>();
        var i = 0;
        while (i < body.Length)
        {
            if (body[i] == '\\' && i + 1 < body.Length)
            {
                AddEscapeClassChars(body[i + 1], pool);
                i += 2;
                continue;
            }

            if (i + 2 < body.Length && body[i + 1] == '-' && body[i + 2] != ']')
            {
                for (var c = body[i]; c <= body[i + 2]; c++)
                {
                    pool.Add(c);
                }
                i += 3;
                continue;
            }

            pool.Add(body[i]);
            i++;
        }

        if (negate)
        {
            pool = PrintableAscii.Except(pool).ToList();
        }

        return pool.Count == 0 ? null : pool[rng.Next(pool.Count)];
    }
}
