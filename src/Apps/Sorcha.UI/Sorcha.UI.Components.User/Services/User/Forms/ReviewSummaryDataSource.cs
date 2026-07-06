// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Models.Forms;

namespace Sorcha.UI.Core.Services.Forms;

/// <summary>
/// Reads prior-page values from the form context and shapes them into an
/// <see cref="IdCardLayoutConfig"/> the review card can render. Feature 107
/// Assured Identity v1 (T021). Stateless and reusable across citizen-side,
/// assessor-side, and wallet-side review renders. <c>sealed</c> — tests
/// instantiate directly; there is no documented extension point.
/// </summary>
public sealed class ReviewSummaryDataSource
{
    /// <summary>
    /// Builds the config for a single review card. Pulls the set of fields
    /// declared on <paramref name="priorPages"/> (not the review page itself)
    /// from <paramref name="formContext"/> and derives the watermark from
    /// <paramref name="runtimeState"/>.
    /// </summary>
    public IdCardLayoutConfig BuildConfig(
        XReviewExtension extension,
        FormContext formContext,
        ActionRuntimeState runtimeState,
        IReadOnlyList<BlueprintPageDefinition> priorPages)
    {
        ArgumentNullException.ThrowIfNull(extension);
        ArgumentNullException.ThrowIfNull(formContext);
        ArgumentNullException.ThrowIfNull(priorPages);

        var sections = new List<IdCardSection>();
        var fieldValues = new Dictionary<string, object?>(StringComparer.Ordinal);

        for (var pageIndex = 0; pageIndex < priorPages.Count; pageIndex++)
        {
            var page = priorPages[pageIndex];
            if (page.Sections is not { Count: > 0 }) continue;

            foreach (var section in page.Sections)
            {
                var fieldPointers = section.Fields
                    .Select(f => PointerFromFieldName(f))
                    .ToList();

                foreach (var pointer in fieldPointers)
                {
                    // ContainsKey distinguishes "unfilled" (present as null)
                    // from "filled with null value" — both render as empty
                    // on the card, but the dictionary explicitly lists every
                    // field the citizen was asked about.
                    if (formContext.FormData.TryGetValue(pointer, out var value) && value is not null)
                    {
                        fieldValues[pointer] = value;
                    }
                    else
                    {
                        // Complex core-primitive ($ref) fields — PersonName, PostalAddress,
                        // DateOfBirth, etc. — are stored FLATTENED at leaf pointers
                        // ("/name/givenName", "/address/line1"), never at the parent pointer the
                        // card looks up. Without this the whole id-card renders empty for every
                        // $ref field. Gather the human-readable leaf values (in encounter order)
                        // and join them. Long/base64 leaves are excluded: the portrait token
                        // ("/portrait/tokenImageBase64") is rendered as the card image separately,
                        // and holder-key material is not display data.
                        fieldValues[pointer] = GatherFlattenedDisplayValue(formContext.FormData, pointer);
                    }

                    // Carry the embedded portrait token through to the card. IdCardLayout renders the
                    // photo by scanning FieldValues for a "<field>/tokenImageBase64" entry, but that
                    // token lives at a child pointer that is never itself a section field — so without
                    // copying it here the card shows the placeholder even when a portrait was captured.
                    var tokenPointer = pointer + "/tokenImageBase64";
                    if (formContext.FormData.TryGetValue(tokenPointer, out var tokenValue) && tokenValue is not null)
                    {
                        fieldValues[tokenPointer] = tokenValue;
                    }
                }

                sections.Add(new IdCardSection(
                    Title: section.Title ?? page.Title,
                    OriginatingPageIndex: pageIndex,
                    FieldPointers: fieldPointers));
            }
        }

        return new IdCardLayoutConfig
        {
            IssuerName = extension.Header.IssuerName,
            CredentialName = extension.Header.CredentialName,
            ColourTheme = extension.Header.ColourTheme,
            Watermark = ReviewSummaryDispatch.ResolveWatermark(runtimeState),
            FieldValues = fieldValues,
            Sections = sections,
            Editable = extension.Editable
        };
    }

    /// <summary>
    /// Schema field names in sections are declared without the leading
    /// slash (<c>"givenName"</c>); the form context keys values by JSON
    /// Pointer (<c>"/givenName"</c>). This helper bridges the two.
    /// Already-pointer-shaped names pass through unchanged so nested
    /// section-scoped fields (<c>"/address/line1"</c>) also work.
    /// Internal so <c>ReviewSummaryRenderer</c> can share the exact same
    /// normalisation in its tabular-fallback path.
    /// </summary>
    internal static string PointerFromFieldName(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName)) return "/";
        return fieldName.StartsWith('/') ? fieldName : "/" + fieldName;
    }

    // Longest human-readable leaf we keep on the card. Real display detail (names, dates, emails,
    // address lines) is short; embedded portrait tokens and holder-key material are far longer, so
    // this length gate cleanly excludes them from the card's text fields.
    private const int MaxDisplayLeafLength = 256;

    /// <summary>
    /// Reconstructs a display value for a complex ($ref) field whose parent pointer isn't in the
    /// form data because its renderer wrote flattened leaf pointers (e.g. <c>/name/givenName</c>,
    /// <c>/address/line1</c>). Joins the non-empty, human-readable leaves under
    /// <paramref name="pointer"/> in encounter order (which follows input/schema order), excluding
    /// long/base64 leaves (portrait token image, holder-key material). Returns null when nothing
    /// displayable was captured.
    /// </summary>
    internal static string? GatherFlattenedDisplayValue(
        IReadOnlyDictionary<string, object?> formData,
        string pointer)
    {
        var prefix = pointer + "/";
        var parts = new List<string>();
        foreach (var kv in formData)
        {
            if (!kv.Key.StartsWith(prefix, StringComparison.Ordinal) || kv.Value is null) continue;
            var text = kv.Value as string ?? kv.Value.ToString();
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxDisplayLeafLength) continue;
            parts.Add(text);
        }

        return parts.Count > 0 ? string.Join(" ", parts) : null;
    }
}
