// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.Reflection;
using System.Text.RegularExpressions;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

namespace Sorcha.Cli.ContractTests;

/// <summary>
/// Asserts that every option named in a command's own help examples is an option that command
/// actually accepts.
/// </summary>
/// <remarks>
/// <para>
/// The CLI embeds usage examples in its command descriptions, so <c>sorcha --help</c> is the first
/// thing an operator reads. Those examples had rotted: <c>sorcha tx submit</c> advertised
/// <c>--data '{"key":"value"}'</c>, an option it has never declared, so the documented invocation
/// failed with <c>Unrecognized command or argument '--data'</c>. Others named options belonging to a
/// different command group entirely.
/// </para>
/// <para>
/// Nothing caught this. The DRIFT-002 harness next door compares DTO property names across the
/// CLI↔server boundary; an example string is neither side of a wire contract, it is just text that
/// happens to be wrong.
/// </para>
/// <para>
/// <b>This test derives its expectation from the live command tree</b> — it builds the real
/// <see cref="RootCommand"/> and reads each command's declared options — so it cannot itself drift.
/// Rename an option and the examples that mention it fail here rather than in a user's terminal.
/// </para>
/// </remarks>
public sealed class CliHelpExampleTests
{
    /// <summary>
    /// An option named in an example, together with the command whose description named it.
    /// </summary>
    private sealed record ExampleOption(string CommandPath, string Option, string ExampleLine);

    [Fact]
    public void EveryOptionNamedInAHelpExample_IsAcceptedByThatCommand()
    {
        var root = BuildRealRootCommand();

        var offenders = new List<ExampleOption>();
        foreach (var (command, path) in Walk(root, root.Name))
        {
            foreach (var (line, target, options) in ExamplesIn(command, path, root))
            {
                // An unresolvable path is reported by the companion test; skip it here so one
                // defect does not produce two failures.
                if (target is null)
                {
                    continue;
                }

                var accepted = AcceptedOptionNames(target, root);
                foreach (var opt in options.Where(o => !accepted.Contains(o)))
                {
                    offenders.Add(new ExampleOption(path, opt, line));
                }
            }
        }

        offenders.Should().BeEmpty(
            "a help example that names a non-existent option documents an invocation that cannot "
            + "work — the operator's first attempt fails.\n"
            + Format(offenders));
    }

    [Fact]
    public void EveryHelpExample_NamesACommandThatExists()
    {
        var root = BuildRealRootCommand();

        var unresolved = new List<string>();
        foreach (var (command, path) in Walk(root, root.Name))
        {
            foreach (var (line, target, _) in ExamplesIn(command, path, root))
            {
                if (target is null)
                {
                    unresolved.Add($"  [{path}]  {line}");
                }
            }
        }

        unresolved.Should().BeEmpty(
            "an example whose command path does not resolve cannot be run at all:\n"
            + string.Join("\n", unresolved));
    }

    [Fact]
    public void TheCommandTree_IsNonTrivial()
    {
        // Anti-vacuity. If reflection silently stopped finding Program.BuildRootCommand, both tests
        // above would pass over an empty tree.
        var root = BuildRealRootCommand();
        var all = Walk(root, root.Name).ToList();

        all.Should().HaveCountGreaterThan(30, "the CLI has dozens of commands");
        all.Where(x => ExamplesIn(x.Command, x.Path, root).Any())
            .Should().HaveCountGreaterThan(10, "most command groups carry usage examples");
    }

    // ---- the live command tree -------------------------------------------------------------

    /// <summary>
    /// Builds the CLI's real root command. <c>Program</c> is internal and its builders private, so
    /// this goes through reflection rather than widening production visibility for a test.
    /// </summary>
    private static RootCommand BuildRealRootCommand()
    {
        var assembly = typeof(Sorcha.Cli.Commands.BaseCommand).Assembly;
        var program = assembly.GetType("Sorcha.Cli.Program")
            ?? throw new InvalidOperationException("Sorcha.Cli.Program not found — has it been renamed?");

        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;

        var configure = program.GetMethod("ConfigureServices", flags)
            ?? throw new InvalidOperationException("Program.ConfigureServices not found.");
        var build = program.GetMethod("BuildRootCommand", flags)
            ?? throw new InvalidOperationException("Program.BuildRootCommand not found.");

        var services = new ServiceCollection();
        configure.Invoke(null, [services]);
        var provider = services.BuildServiceProvider();

        return (RootCommand)build.Invoke(null, [provider])!;
    }

    private static IEnumerable<(Command Command, string Path)> Walk(Command command, string path)
    {
        yield return (command, path);
        foreach (var child in command.Subcommands)
        {
            foreach (var descendant in Walk(child, $"{path} {child.Name}"))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Options a command accepts: its own, plus every ancestor's (global options are declared on
    /// the root and inherited).
    /// </summary>
    private static HashSet<string> AcceptedOptionNames(Command target, Command root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var chain in ChainTo(root, target) ?? [])
        {
            foreach (var option in chain.Options)
            {
                names.Add(option.Name);
                foreach (var alias in option.Aliases)
                {
                    names.Add(alias);
                }
            }
        }

        // System.CommandLine supplies these on every command without declaring them.
        names.Add("--help");
        names.Add("--version");
        return names;
    }

    /// <summary>The command chain from <paramref name="root"/> down to <paramref name="target"/>.</summary>
    private static List<Command>? ChainTo(Command root, Command target)
    {
        if (ReferenceEquals(root, target))
        {
            return [root];
        }

        foreach (var child in root.Subcommands)
        {
            var below = ChainTo(child, target);
            if (below is not null)
            {
                below.Insert(0, root);
                return below;
            }
        }

        return null;
    }

    // ---- example parsing ------------------------------------------------------------------

    private static readonly Regex ExampleLine = new(@"^\s*sorcha\s+(?<rest>\S.*)$", RegexOptions.Compiled);
    private static readonly Regex OptionToken = new(@"(?<!\S)(--[a-z0-9][a-z0-9-]*)", RegexOptions.Compiled);

    /// <summary>
    /// The <c>sorcha …</c> example lines in a command's description, each resolved to the command it
    /// names and the long options it passes.
    /// </summary>
    private static IEnumerable<(string Line, Command? Target, IReadOnlyList<string> Options)> ExamplesIn(
        Command command, string path, Command root)
    {
        var description = command.Description;
        if (string.IsNullOrWhiteSpace(description))
        {
            yield break;
        }

        foreach (var raw in description.Split('\n'))
        {
            var match = ExampleLine.Match(raw.TrimEnd('\r'));
            if (!match.Success)
            {
                continue;
            }

            var rest = match.Groups["rest"].Value.Trim();

            // Leading bare words are the command path; everything from the first option or
            // placeholder onward is arguments.
            var words = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var pathWords = words
                .TakeWhile(w => !w.StartsWith('-') && !w.StartsWith('<') && !w.StartsWith('"')
                                && !w.StartsWith('\'') && !w.Contains('|') && !w.Contains('$'))
                .ToList();

            var target = Resolve(root, pathWords);
            var options = OptionToken.Matches(rest).Select(m => m.Groups[1].Value).Distinct().ToList();

            yield return ($"sorcha {rest}", target, options);
        }
    }

    /// <summary>Walks a bare-word path down the command tree, or null if it does not resolve.</summary>
    private static Command? Resolve(Command root, IReadOnlyList<string> pathWords)
    {
        var current = root;
        foreach (var word in pathWords)
        {
            var next = current.Subcommands.FirstOrDefault(
                c => string.Equals(c.Name, word, StringComparison.Ordinal)
                     || c.Aliases.Contains(word));

            // Trailing words may be arguments rather than subcommands (e.g. `completion bash`), so
            // stop descending rather than failing — the command reached so far is the target.
            if (next is null)
            {
                return current.Subcommands.Count == 0 || current != root ? current : null;
            }

            current = next;
        }

        return current;
    }

    private static string Format(IEnumerable<ExampleOption> offenders) =>
        string.Join("\n", offenders
            .GroupBy(o => o.CommandPath, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"  [{g.Key}]\n"
                         + string.Join("\n", g
                             .Select(o => $"      {o.Option}  ->  {o.ExampleLine}")
                             .Distinct()
                             .OrderBy(x => x, StringComparer.Ordinal))));
}
