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
                    if (formContext.FormData.TryGetValue(pointer, out var value))
                    {
                        fieldValues[pointer] = value;
                    }
                    else
                    {
                        fieldValues[pointer] = null;
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
}
