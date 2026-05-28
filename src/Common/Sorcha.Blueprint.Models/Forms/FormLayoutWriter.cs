// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sorcha.Blueprint.Models.Forms;

/// <summary>
/// Shared, pure-JSON writer for the <b>presentational</b> form-layout <c>x-*</c>
/// keywords on an Action's data schema. Used by both the designer UI's
/// <c>FormLayoutAuthoringService</c> (direct manipulation, Feature 142 US5
/// T049/T050) and the server-side <c>BlueprintToolExecutor</c> chat tools
/// (<c>set_form_layout</c>, <c>set_field_autofill</c>, <c>set_review_page</c>;
/// T051), so direct-manipulation and chat edits converge byte-equivalently on
/// the same schema JSON (FR-016 / T047).
/// </summary>
/// <remarks>
/// <para>
/// Every public method is guarded by <see cref="FormKeywordClassifier"/>:
/// attempting to write a non-presentational <c>x-*</c> key throws
/// <see cref="InvalidOperationException"/>. That guard is the structural
/// promise of FR-023 — presentational edits MUST NOT re-lock the rehearsal
/// gate, so this writer cannot be used to alter the executable definition.
/// Behavioural keywords (<c>x-file</c>, <c>x-credential-offer</c>) remain the
/// responsibility of upstream typed tools that knowingly re-lock.
/// </para>
/// <para>
/// Input is a JSON Schema root <see cref="JsonDocument"/> (typically
/// <c>Action.DataSchemas[0]</c>); output is a fresh <see cref="JsonDocument"/>
/// with the mutation applied. The writer never mutates the input document.
/// Returning <see cref="JsonDocument"/> keeps the call-site contract aligned
/// with <see cref="Sorcha.Blueprint.Models.Action.DataSchemas"/>.
/// </para>
/// </remarks>
public static class FormLayoutWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    private static void GuardPresentational(string keyword)
    {
        if (!FormKeywordClassifier.IsPresentational(keyword))
        {
            throw new InvalidOperationException(
                $"FormLayoutWriter refuses to write keyword '{keyword}' — it is not classified " +
                "presentational. Behavioural keywords (e.g. x-file, x-credential-offer) re-lock " +
                "the rehearsal gate and must go through their dedicated upstream tooling.");
        }
    }

    /// <summary>
    /// Clones the given schema as a mutable <see cref="JsonObject"/> root.
    /// Throws if the schema's root is not an object (a JSON Schema invariant).
    /// </summary>
    private static JsonObject CloneRoot(JsonDocument schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var node = JsonNode.Parse(schema.RootElement.GetRawText())
            ?? throw new InvalidOperationException("Schema root could not be parsed as a JSON node.");
        if (node is not JsonObject obj)
        {
            throw new InvalidOperationException("Schema root must be a JSON object.");
        }
        return obj;
    }

    private static JsonDocument Materialise(JsonObject root)
    {
        var json = root.ToJsonString(SerializerOptions);
        return JsonDocument.Parse(json);
    }

    /// <summary>
    /// Writes (or clears, when <paramref name="sections"/> is null) the
    /// presentational <c>x-sections</c> array on the schema root.
    /// </summary>
    public static JsonDocument SetSections(JsonDocument schema, JsonArray? sections)
    {
        GuardPresentational("x-sections");
        var root = CloneRoot(schema);
        if (sections is null)
        {
            root.Remove("x-sections");
        }
        else
        {
            // Detach from any prior parent so we can graft it onto the new root.
            var detached = JsonNode.Parse(sections.ToJsonString()) as JsonArray
                ?? new JsonArray();
            root["x-sections"] = detached;
        }
        return Materialise(root);
    }

    /// <summary>
    /// Writes (or clears) the presentational <c>x-pages</c> array on the schema root.
    /// </summary>
    public static JsonDocument SetPages(JsonDocument schema, JsonArray? pages)
    {
        GuardPresentational("x-pages");
        var root = CloneRoot(schema);
        if (pages is null)
        {
            root.Remove("x-pages");
        }
        else
        {
            var detached = JsonNode.Parse(pages.ToJsonString()) as JsonArray
                ?? new JsonArray();
            root["x-pages"] = detached;
        }
        return Materialise(root);
    }

    /// <summary>
    /// Writes (or clears, when <paramref name="introduction"/> is null) the
    /// presentational <c>x-introduction</c> string on the schema root.
    /// </summary>
    public static JsonDocument SetIntroduction(JsonDocument schema, string? introduction)
    {
        GuardPresentational("x-introduction");
        var root = CloneRoot(schema);
        if (string.IsNullOrWhiteSpace(introduction))
        {
            root.Remove("x-introduction");
        }
        else
        {
            root["x-introduction"] = introduction;
        }
        return Materialise(root);
    }

    /// <summary>
    /// Writes the presentational <c>x-width</c> hint on a top-level property's
    /// schema. <paramref name="fieldPath"/> is a JSON Pointer (e.g. <c>/email</c>)
    /// or a bare property name. Valid widths are <c>full</c> / <c>half</c> / <c>third</c>
    /// (case-insensitive); pass null to clear. Throws if the property does not
    /// exist on the schema.
    /// </summary>
    public static JsonDocument SetFieldWidth(JsonDocument schema, string fieldPath, string? width)
    {
        GuardPresentational("x-width");
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);

        var root = CloneRoot(schema);
        var propertyObj = NavigateToProperty(root, fieldPath);

        if (string.IsNullOrWhiteSpace(width))
        {
            propertyObj.Remove("x-width");
        }
        else
        {
            propertyObj["x-width"] = width.ToLowerInvariant();
        }
        return Materialise(root);
    }

    /// <summary>
    /// Writes (or clears) the presentational <c>x-persona</c> autofill binding on a
    /// top-level property's schema. Pass null to opt out (clear the binding).
    /// </summary>
    public static JsonDocument SetFieldPersona(JsonDocument schema, string fieldPath, string? personaKey)
    {
        GuardPresentational("x-persona");
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);

        var root = CloneRoot(schema);
        var propertyObj = NavigateToProperty(root, fieldPath);

        if (string.IsNullOrWhiteSpace(personaKey))
        {
            propertyObj.Remove("x-persona");
        }
        else
        {
            propertyObj["x-persona"] = personaKey;
        }
        return Materialise(root);
    }

    /// <summary>
    /// Marks the wizard page at <paramref name="pageIndex"/> as the review/x-review
    /// summary page (Feature 107 id-card review). Requires <c>x-pages</c> to be
    /// present and <paramref name="pageIndex"/> to be a valid index. The
    /// <c>x-review</c> object carries a minimal default header (issuer/credential
    /// names) so the renderer's <c>ReviewSummaryRenderer</c> contract is honoured;
    /// callers may overwrite via <see cref="SetPages"/> for richer metadata.
    /// </summary>
    public static JsonDocument SetReviewPage(JsonDocument schema, int pageIndex,
        string issuerName = "Issuer",
        string credentialName = "Summary")
    {
        GuardPresentational("x-review");
        var root = CloneRoot(schema);
        if (root["x-pages"] is not JsonArray pages || pages.Count == 0)
        {
            throw new InvalidOperationException(
                "SetReviewPage requires x-pages to be present — split the form into wizard pages first.");
        }
        if (pageIndex < 0 || pageIndex >= pages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex),
                $"pageIndex {pageIndex} is out of range for {pages.Count} pages.");
        }

        // Clear x-review from every page first so only the chosen page carries it (single review page).
        for (var i = 0; i < pages.Count; i++)
        {
            if (pages[i] is JsonObject pageObj)
            {
                pageObj.Remove("x-review");
            }
        }

        if (pages[pageIndex] is JsonObject target)
        {
            target["x-review"] = new JsonObject
            {
                ["layout"] = "id-card",
                ["editable"] = true,
                ["header"] = new JsonObject
                {
                    ["issuerName"] = issuerName,
                    ["credentialName"] = credentialName,
                },
            };
        }
        return Materialise(root);
    }

    /// <summary>
    /// Writes the presentational <c>x-address-lookup</c> boolean flag on a
    /// top-level property's schema (enables the address-lookup affordance).
    /// </summary>
    public static JsonDocument SetAddressLookup(JsonDocument schema, string fieldPath, bool enabled)
    {
        GuardPresentational("x-address-lookup");
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);

        var root = CloneRoot(schema);
        var propertyObj = NavigateToProperty(root, fieldPath);

        if (!enabled)
        {
            propertyObj.Remove("x-address-lookup");
        }
        else
        {
            propertyObj["x-address-lookup"] = true;
        }
        return Materialise(root);
    }

    /// <summary>
    /// Bulk-apply a presentational layout in one shot. Any argument that is
    /// non-null is written; nulls are skipped (not cleared). To clear a
    /// keyword, call the single-purpose method with null. This is the
    /// <c>set_form_layout</c> chat-tool shape (T051).
    /// </summary>
    /// <param name="schema">Source schema.</param>
    /// <param name="sections"><c>x-sections</c> array, or null to leave unchanged.</param>
    /// <param name="pages"><c>x-pages</c> array, or null to leave unchanged.</param>
    /// <param name="introduction"><c>x-introduction</c> string, or null to leave unchanged.</param>
    /// <param name="widths">Map of fieldPath → width to apply via <see cref="SetFieldWidth"/>; null to leave unchanged.</param>
    public static JsonDocument SetFormLayout(
        JsonDocument schema,
        JsonArray? sections = null,
        JsonArray? pages = null,
        string? introduction = null,
        IReadOnlyDictionary<string, string>? widths = null)
    {
        var current = schema;
        if (sections is not null) current = SetSections(current, sections);
        if (pages is not null) current = SetPages(current, pages);
        if (introduction is not null) current = SetIntroduction(current, introduction);
        if (widths is not null)
        {
            foreach (var (path, width) in widths)
            {
                current = SetFieldWidth(current, path, width);
            }
        }
        return current;
    }

    /// <summary>
    /// Navigates the schema root to a top-level property's object. Accepts both
    /// JSON Pointer (<c>/email</c>) and bare property name (<c>email</c>) forms.
    /// Throws when the schema has no <c>properties</c> map or the property does
    /// not exist — refusing to silently create properties keeps the writer
    /// firmly presentational (the property must already be in the schema).
    /// </summary>
    private static JsonObject NavigateToProperty(JsonObject root, string fieldPath)
    {
        var name = fieldPath.StartsWith('/') ? fieldPath[1..] : fieldPath;
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("fieldPath must reference a property name.", nameof(fieldPath));
        }

        if (root["properties"] is not JsonObject properties)
        {
            throw new InvalidOperationException(
                $"Schema has no 'properties' map — cannot write to '{fieldPath}'.");
        }

        if (properties[name] is not JsonObject propertyObj)
        {
            throw new InvalidOperationException(
                $"Property '{name}' does not exist on the schema.");
        }
        return propertyObj;
    }
}
