// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Engine.Models;
using Sorcha.Blueprint.Models;
using ActionModel = Sorcha.Blueprint.Models.Action;

namespace Sorcha.Blueprint.Engine.Implementation;

/// <summary>
/// The single place that decides which schemas an action's submitted data must satisfy,
/// and applies them (issue #1573).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Two independently sensible components were joined on different
/// properties and nothing verified the join. Callers gated on <see cref="ActionModel.DataSchemas"/> —
/// the property every published blueprint populates — while the engine validated
/// <c>Action.Form.Schema</c>, which no blueprint sets and which defaults to null on a layout-only
/// <see cref="Control"/>. The caller saw schemas, the callee saw none, and every payload validated
/// successfully. It failed open, silently, at three separate call sites.
/// </para>
/// <para>
/// <b>Semantics are the Validator's, deliberately.</b> The data validated is the action's own
/// submitted payload, and it must satisfy EVERY entry in <c>dataSchemas</c> — the same rule
/// <c>ValidationEngine.ValidateSchemaAsync</c> applies on the ledger side. Any other choice here
/// means the two halves of the platform disagree about the contract, and the disagreement only
/// surfaces as a transaction that is accepted and then never seals.
/// </para>
/// <para>
/// <b>Do not "fix" this by populating <c>Form.Schema</c> at publish time.</b> <c>form</c> is
/// serialised into the canonical published definition, so writing to it changes the canonical
/// bytes and moves every publication id on every register (CLAUDE.md pattern 22) — while leaving
/// <c>execDefHash</c> untouched, because <c>form</c> is presentational and excluded from it. The
/// identity would move, the behavioural signature would say nothing had changed, and any
/// <c>RehearsalPass</c> would survive. Reading <c>dataSchemas</c> changes no bytes at all.
/// </para>
/// </remarks>
internal static class ActionSchemaValidation
{
    /// <summary>
    /// Validates <paramref name="data"/> against every schema the action declares.
    /// </summary>
    /// <remarks>
    /// <c>dataSchemas</c> is the canonical source. <c>Form.Schema</c> is retained only as a
    /// fallback for actions built in code rather than published — the Fluent API, the demo app
    /// and older tests — where dropping the one schema they do set would be a second fail-open
    /// in the opposite direction. An action declaring neither constrains nothing.
    /// </remarks>
    public static async Task<ValidationResult> ValidateAsync(
        ISchemaValidator validator,
        Dictionary<string, object> data,
        ActionModel action,
        CancellationToken ct = default)
    {
        var errors = new List<ValidationError>();

        foreach (var schema in ResolveSchemas(action))
        {
            var result = await validator.ValidateAsync(data, schema, ct);
            if (!result.IsValid)
            {
                errors.AddRange(result.Errors);
            }
        }

        return errors.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(errors);
    }

    /// <summary>
    /// The schemas an action's submitted data must satisfy, in declaration order.
    /// </summary>
    private static IReadOnlyList<JsonNode> ResolveSchemas(ActionModel action)
    {
        // DataSchemas are JsonDocument; ISchemaValidator takes JsonNode. Round-tripping the raw
        // text is the only conversion between them, and it also detaches the node from the
        // document's lifetime — a shared Action outlives any using-scope here.
        var declared = action.DataSchemas?
            .Select(d => JsonNode.Parse(d.RootElement.GetRawText()))
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList() ?? [];

        if (declared.Count > 0)
        {
            return declared;
        }

        return action.Form?.Schema is { } formSchema ? [formSchema] : [];
    }
}
