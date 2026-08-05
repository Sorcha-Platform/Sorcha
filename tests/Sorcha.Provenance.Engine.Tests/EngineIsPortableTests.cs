// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using FluentAssertions;
using Sorcha.Provenance.Engine;

namespace Sorcha.Provenance.Engine.Tests;

/// <summary>
/// Guards the one property the whole engine/service split exists for: <c>Sorcha.Provenance.Engine</c>
/// must stay runnable where the service cannot run.
/// </summary>
/// <remarks>
/// <para>
/// Two assemblies foreclose that, and both would do it silently:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>Sorcha.Cryptography</c> P/Invokes libsodium and cannot load under browser-wasm — the
///     documented reason <c>Sorcha.Mdoc</c> was extracted from it during Feature 185.
///   </description></item>
///   <item><description>
///     <c>Sorcha.ServiceClients.*</c> drags HTTP clients in, which makes the engine boundary
///     decorative rather than real.
///   </description></item>
/// </list>
/// <para>
/// Nothing else in the build would object to either arriving. There is no compile error, no warning,
/// and no runtime failure on the server — the cost lands entirely on the Phase-3 portable export,
/// which would stop being an addition and become a rewrite. A single casual <c>using</c> is enough,
/// which is why this is a test rather than a comment in the csproj.
/// </para>
/// <para>
/// <b>This guard has been mutation-tested</b> (task T005): a temporary <c>ProjectReference</c> to
/// <c>Sorcha.Cryptography</c> plus one line of code using it made both tests below fail, naming the
/// assembly, and the reference was then removed. A guard never shown to fail is not a guard.
/// </para>
/// <para>
/// <b>If you repeat that mutation, use the reference in a way that survives compilation.</b> The
/// first attempt used <c>nameof(MerkleTree)</c> and these tests stayed green — correctly.
/// <c>nameof</c> is resolved at compile time and emits nothing, and an unused
/// <c>ProjectReference</c> is pruned from the assembly manifest, so there was genuinely no runtime
/// dependency to find. <c>typeof(MerkleTree)</c> emits a real manifest reference and turns both
/// tests red. This is a property of what is being checked, not a hole in it: these tests read the
/// assembly manifest, which is exactly the list the runtime must resolve when loading the engine —
/// the thing that decides whether it loads under browser-wasm.
/// </para>
/// </remarks>
public class EngineIsPortableTests
{
    /// <summary>Assembly-name prefixes the engine may never depend on, directly or transitively.</summary>
    private static readonly string[] ForbiddenPrefixes = ["Sorcha.Cryptography", "Sorcha.ServiceClients"];

    [Fact]
    public void Engine_DoesNotReferenceForbiddenAssemblies()
    {
        var engine = typeof(ProvenanceCheck).Assembly;

        var offenders = engine.GetReferencedAssemblies()
            .Where(r => ForbiddenPrefixes.Any(p => r.Name!.StartsWith(p, StringComparison.Ordinal)))
            .Select(r => r.Name!)
            .ToList();

        offenders.Should().BeEmpty(
            "Sorcha.Provenance.Engine must stay dependency-free so it can run where the service " +
            "cannot (plan D1). Referencing {0} forecloses the Phase-3 portable export path, and " +
            "nothing else in the build will tell you.",
            string.Join(", ", offenders));
    }

    /// <summary>
    /// The same rule over the whole Sorcha closure, not just direct references.
    /// </summary>
    /// <remarks>
    /// A direct-reference check alone would pass while <c>Sorcha.Cryptography</c> arrived through an
    /// otherwise-innocent leaf. The engine's reachable Sorcha graph is tiny by design, so walking it
    /// is cheap; if it ever stops being tiny, that is itself worth noticing.
    /// </remarks>
    [Fact]
    public void Engine_TransitiveSorchaClosure_ContainsNoForbiddenAssembly()
    {
        var engine = typeof(ProvenanceCheck).Assembly;

        var offenders = new List<string>();
        var unloadable = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>();
        pending.Enqueue(engine);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();

            foreach (var reference in current.GetReferencedAssemblies())
            {
                var name = reference.Name!;

                if (ForbiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
                {
                    offenders.Add($"{current.GetName().Name} → {name}");
                }

                // Only walk Sorcha assemblies: the BCL closure is large, irrelevant and slow.
                if (!name.StartsWith("Sorcha.", StringComparison.Ordinal) || !seen.Add(name))
                {
                    continue;
                }

                try
                {
                    pending.Enqueue(Assembly.Load(reference));
                }
                catch (Exception ex)
                {
                    // Recorded rather than swallowed: an unwalkable branch is an unchecked branch,
                    // and a silently-skipped one would make this guard quietly weaker over time.
                    unloadable.Add($"{name} ({ex.GetType().Name})");
                }
            }
        }

        offenders.Should().BeEmpty(
            "no assembly reachable from Sorcha.Provenance.Engine may pull in cryptography P/Invoke " +
            "or HTTP clients (plan D1); found {0}",
            string.Join(", ", offenders));

        unloadable.Should().BeEmpty(
            "every Sorcha assembly reachable from the engine must be walkable, or this guard is " +
            "checking less than it appears to; could not load {0}",
            string.Join(", ", unloadable));
    }
}
