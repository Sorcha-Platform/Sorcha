// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Json.Schema;

namespace Sorcha.Blueprint.Models.Schemas;

/// <summary>
/// The JSON Schema dialect Sorcha evaluates action payload schemas under: draft 2020-12 plus the
/// <c>formatMaximum</c> / <c>formatMinimum</c> date bounds.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a dialect and not a global keyword.</b> JsonSchema.Net resolves keyword handlers through
/// the dialect named by the document's <c>$schema</c>, and it refuses to overwrite an official
/// dialect (<c>"Cannot overwrite official dialects"</c>) — so the handlers cannot simply be bolted
/// onto 2020-12. Supplying a dialect via <c>BuildOptions</c> does not help either: a document's own
/// <c>$schema</c> wins, and ours declare 2020-12. Both were verified against 9.4.0 before this was
/// written.
/// </para>
/// <para>
/// <b>Why rewriting <c>$schema</c> at read time is the only workable delivery.</b> A blueprint
/// already sealed on a ledger cannot be re-flattened, so its baked-in schema will forever declare
/// 2020-12. The dialect a document is READ under is an evaluation concern rather than part of the
/// signed content — which is exactly the reasoning #1473 used to declare the dialect at read time
/// in the first place. Pointing that same step at this dialect is what makes the bounds apply
/// retroactively to every blueprint already on a ledger.
/// </para>
/// <para>
/// This dialect is a strict SUPERSET of 2020-12: it adds two keywords and changes nothing else, so
/// a schema that does not use them evaluates identically. Registration is process-wide and
/// idempotent; every site that parses an action schema must call <see cref="EnsureRegistered"/>
/// before <c>JsonSchema.FromText</c>.
/// </para>
/// <para>
/// <b>Both evaluation sites move together</b> — <c>ValidationEngine</c> (Validator Service) and the
/// <c>Sorcha.Blueprint.Engine.SchemaValidator</c> mirror. A bound enforced in one and not the other
/// is worse than neither, because the two would disagree about whether a payload is valid.
/// </para>
/// </remarks>
public static class SorchaSchemaDialect
{
    /// <summary>The dialect identifier Sorcha action schemas are evaluated under.</summary>
    public const string Id = "https://schemas.sorcha.dev/dialect/2020-12";

    /// <summary>The standard draft 2020-12 identifier this dialect extends.</summary>
    public const string Draft202012Id = "https://json-schema.org/draft/2020-12/schema";

    private static readonly Lock Gate = new();
    private static bool _registered;

    /// <summary>
    /// Registers the Sorcha dialect in the process-wide registry. Idempotent and thread-safe;
    /// safe to call on every schema parse.
    /// </summary>
    public static void EnsureRegistered()
    {
        if (Volatile.Read(ref _registered)) return;

        lock (Gate)
        {
            if (_registered) return;

            var dialect = Dialect.Draft202012.With(
                new IKeywordHandler[]
                {
                    FormatBoundKeywordHandler.Maximum,
                    FormatBoundKeywordHandler.Minimum
                },
                id: new Uri(Id));

            DialectRegistry.Global.Register(dialect);
            Volatile.Write(ref _registered, true);
        }
    }

    /// <summary>
    /// Returns the dialect a document declaring <paramref name="declared"/> should be READ under.
    /// </summary>
    /// <remarks>
    /// A document declaring nothing, or plain 2020-12, is upgraded to the Sorcha dialect — that is
    /// the whole population of Sorcha action schemas, including every blueprint already sealed.
    /// A document declaring some OTHER draft (07, 2019-09, …) is left alone: it was authored
    /// against different semantics and silently re-dialecting it would change more than the two
    /// keywords this exists for.
    /// </remarks>
    public static string ResolveReadDialect(string? declared) =>
        string.IsNullOrWhiteSpace(declared) || declared == Draft202012Id || declared == Id
            ? Id
            : declared;
}
