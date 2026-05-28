// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Models.Forms;
using BlueprintAction = Sorcha.Blueprint.Models.Action;

namespace Sorcha.UI.Core.Services.Forms;

/// <summary>
/// Feature 142 / US5 — direct-manipulation writer that applies presentational
/// <c>x-*</c> form-layout keywords to an Action's first data schema. Wraps the
/// shared <see cref="FormLayoutWriter"/> so that direct-manipulation edits
/// (LayoutToolsPanel) and chat-tool edits (<c>set_form_layout</c> /
/// <c>set_field_autofill</c> / <c>set_review_page</c> in <c>BlueprintToolExecutor</c>)
/// converge byte-equivalently on the same schema JSON (FR-016 / T047).
/// </summary>
/// <remarks>
/// <para>
/// Every method returns the mutated <see cref="BlueprintAction"/> so the caller
/// can hand it to <c>DesignerContext.SetBlueprint</c>. Because every keyword
/// touched is classified <b>presentational</b> by <see cref="FormKeywordClassifier"/>,
/// the <c>ExecDefHash</c> recomputed on Blueprint change does NOT alter, and a
/// passing rehearsal is preserved (FR-023).
/// </para>
/// <para>
/// The service operates on <c>action.DataSchemas[0]</c>. When the Action has no
/// schemas, calls throw <see cref="InvalidOperationException"/> — the caller is
/// expected to seed at least one schema first (e.g. via Import or
/// <see cref="IFormSchemaService.AutoGenerateForm"/>).
/// </para>
/// </remarks>
public interface IFormLayoutAuthoringService
{
    /// <summary>Writes (or clears) the presentational <c>x-sections</c> array.</summary>
    BlueprintAction SetSections(BlueprintAction action, JsonArray? sections);

    /// <summary>Writes (or clears) the presentational <c>x-pages</c> array.</summary>
    BlueprintAction SetPages(BlueprintAction action, JsonArray? pages);

    /// <summary>Writes the presentational <c>x-width</c> hint on a top-level field.</summary>
    BlueprintAction SetFieldWidth(BlueprintAction action, string fieldPath, string? width);

    /// <summary>
    /// Writes (or, with <paramref name="personaKey"/> null, clears) the presentational
    /// <c>x-persona</c> autofill binding on a top-level field.
    /// </summary>
    BlueprintAction SetFieldPersona(BlueprintAction action, string fieldPath, string? personaKey);

    /// <summary>Writes (or clears) the presentational <c>x-introduction</c> string.</summary>
    BlueprintAction SetIntroduction(BlueprintAction action, string? introduction);

    /// <summary>Marks a wizard page as the review/<c>x-review</c> summary page.</summary>
    BlueprintAction SetReviewPage(BlueprintAction action, int pageIndex);

    /// <summary>Writes the presentational <c>x-address-lookup</c> flag on a top-level field.</summary>
    BlueprintAction SetAddressLookup(BlueprintAction action, string fieldPath, bool enabled);
}

/// <inheritdoc cref="IFormLayoutAuthoringService"/>
public sealed class FormLayoutAuthoringService : IFormLayoutAuthoringService
{
    /// <inheritdoc />
    public BlueprintAction SetSections(BlueprintAction action, JsonArray? sections)
        => Mutate(action, schema => FormLayoutWriter.SetSections(schema, sections));

    /// <inheritdoc />
    public BlueprintAction SetPages(BlueprintAction action, JsonArray? pages)
        => Mutate(action, schema => FormLayoutWriter.SetPages(schema, pages));

    /// <inheritdoc />
    public BlueprintAction SetFieldWidth(BlueprintAction action, string fieldPath, string? width)
        => Mutate(action, schema => FormLayoutWriter.SetFieldWidth(schema, fieldPath, width));

    /// <inheritdoc />
    public BlueprintAction SetFieldPersona(BlueprintAction action, string fieldPath, string? personaKey)
        => Mutate(action, schema => FormLayoutWriter.SetFieldPersona(schema, fieldPath, personaKey));

    /// <inheritdoc />
    public BlueprintAction SetIntroduction(BlueprintAction action, string? introduction)
        => Mutate(action, schema => FormLayoutWriter.SetIntroduction(schema, introduction));

    /// <inheritdoc />
    public BlueprintAction SetReviewPage(BlueprintAction action, int pageIndex)
        => Mutate(action, schema => FormLayoutWriter.SetReviewPage(schema, pageIndex));

    /// <inheritdoc />
    public BlueprintAction SetAddressLookup(BlueprintAction action, string fieldPath, bool enabled)
        => Mutate(action, schema => FormLayoutWriter.SetAddressLookup(schema, fieldPath, enabled));

    private static BlueprintAction Mutate(BlueprintAction action, Func<JsonDocument, JsonDocument> writer)
    {
        ArgumentNullException.ThrowIfNull(action);
        var schemas = action.DataSchemas?.ToList()
            ?? throw new InvalidOperationException(
                "Action has no DataSchemas — import or generate a schema first.");
        if (schemas.Count == 0)
        {
            throw new InvalidOperationException(
                "Action.DataSchemas is empty — import or generate a schema first.");
        }

        var updated = writer(schemas[0]);
        schemas[0] = updated;
        action.DataSchemas = schemas;
        return action;
    }
}
