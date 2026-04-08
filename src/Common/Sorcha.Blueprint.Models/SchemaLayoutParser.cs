// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Static parser that extracts layout information (<c>x-pages</c>, <c>x-sections</c>,
/// <c>x-introduction</c>, <c>x-width</c>) from an action's JSON Schema and returns
/// a structured <see cref="SchemaLayoutInfo"/> for the UI to consume.
/// </summary>
/// <remarks>
/// Follows the defensive-parsing style of <see cref="FileSchemaExtension.TryParseFromSchema"/>:
/// malformed or unexpected JSON produces <see cref="SchemaLayoutInfo.Empty"/> rather
/// than throwing, so the UI degrades gracefully to a flat form.
/// </remarks>
public static class SchemaLayoutParser
{
    private static readonly HashSet<string> ValidFieldWidths = new(StringComparer.OrdinalIgnoreCase)
    {
        "full", "half", "third"
    };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Parses an action's root JSON Schema element and extracts any layout
    /// extensions present. Returns <see cref="SchemaLayoutInfo.Empty"/> if the
    /// element is not an object, has no extensions, or cannot be parsed.
    /// </summary>
    /// <param name="actionSchema">The action's root JSON Schema element.</param>
    /// <returns>A populated <see cref="SchemaLayoutInfo"/>; never null.</returns>
    public static SchemaLayoutInfo Parse(JsonElement actionSchema)
    {
        if (actionSchema.ValueKind != JsonValueKind.Object)
            return SchemaLayoutInfo.Empty;

        try
        {
            var pages = TryParsePages(actionSchema);
            var sections = TryParseSections(actionSchema);
            var introduction = TryParseIntroduction(actionSchema);
            var fieldWidths = TryParseFieldWidths(actionSchema);

            if (pages is null && sections is null && introduction is null && fieldWidths is null)
                return SchemaLayoutInfo.Empty;

            return new SchemaLayoutInfo
            {
                Pages = pages,
                Sections = sections,
                Introduction = introduction,
                FieldWidths = fieldWidths
            };
        }
        catch (JsonException)
        {
            return SchemaLayoutInfo.Empty;
        }
    }

    /// <summary>
    /// Attempts to read the <c>x-width</c> hint from an individual property schema.
    /// Valid values are <c>full</c>, <c>half</c>, and <c>third</c> (case-insensitive).
    /// </summary>
    /// <param name="propertySchema">The property's JSON Schema element.</param>
    /// <param name="width">
    /// When this method returns <see langword="true"/>, contains the lowercased
    /// width value; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if <c>x-width</c> is present and valid;
    /// <see langword="false"/> otherwise.
    /// </returns>
    public static bool TryGetFieldWidth(JsonElement propertySchema, out string? width)
    {
        width = null;

        if (propertySchema.ValueKind != JsonValueKind.Object)
            return false;

        if (!propertySchema.TryGetProperty("x-width", out var widthElement))
            return false;

        if (widthElement.ValueKind != JsonValueKind.String)
            return false;

        var raw = widthElement.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (!ValidFieldWidths.Contains(raw))
            return false;

        width = raw.ToLowerInvariant();
        return true;
    }

    private static List<BlueprintPageDefinition>? TryParsePages(JsonElement root)
    {
        if (!root.TryGetProperty("x-pages", out var pagesElement))
            return null;

        if (pagesElement.ValueKind != JsonValueKind.Array)
            return null;

        var pages = new List<BlueprintPageDefinition>();
        foreach (var pageElement in pagesElement.EnumerateArray())
        {
            if (pageElement.ValueKind != JsonValueKind.Object)
                continue;

            if (!pageElement.TryGetProperty("title", out var titleElement) ||
                titleElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var title = titleElement.GetString();
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var description = pageElement.TryGetProperty("description", out var descEl) &&
                              descEl.ValueKind == JsonValueKind.String
                ? descEl.GetString()
                : null;

            var layout = pageElement.TryGetProperty("layout", out var layoutEl) &&
                         layoutEl.ValueKind == JsonValueKind.String
                ? layoutEl.GetString()
                : "single-column";

            var pageSections = TryParseSections(pageElement);

            pages.Add(new BlueprintPageDefinition
            {
                Title = title,
                Description = description,
                Layout = layout,
                Sections = pageSections
            });
        }

        return pages.Count > 0 ? pages : null;
    }

    private static List<BlueprintSectionDefinition>? TryParseSections(JsonElement parent)
    {
        if (!parent.TryGetProperty("x-sections", out var sectionsElement))
            return null;

        if (sectionsElement.ValueKind != JsonValueKind.Array)
            return null;

        var sections = new List<BlueprintSectionDefinition>();
        foreach (var sectionElement in sectionsElement.EnumerateArray())
        {
            if (sectionElement.ValueKind != JsonValueKind.Object)
                continue;

            if (!sectionElement.TryGetProperty("title", out var titleElement) ||
                titleElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var title = titleElement.GetString();
            if (string.IsNullOrWhiteSpace(title))
                continue;

            if (!sectionElement.TryGetProperty("fields", out var fieldsElement) ||
                fieldsElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var fields = new List<string>();
            foreach (var fieldEl in fieldsElement.EnumerateArray())
            {
                if (fieldEl.ValueKind == JsonValueKind.String)
                {
                    var name = fieldEl.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                        fields.Add(name);
                }
            }

            if (fields.Count == 0)
                continue;

            var description = sectionElement.TryGetProperty("description", out var descEl) &&
                              descEl.ValueKind == JsonValueKind.String
                ? descEl.GetString()
                : null;

            var help = sectionElement.TryGetProperty("help", out var helpEl) &&
                       helpEl.ValueKind == JsonValueKind.String
                ? helpEl.GetString()
                : null;

            var layout = sectionElement.TryGetProperty("layout", out var layoutEl) &&
                         layoutEl.ValueKind == JsonValueKind.String
                ? layoutEl.GetString()
                : "vertical";

            sections.Add(new BlueprintSectionDefinition
            {
                Title = title,
                Description = description,
                Help = help,
                Layout = layout,
                Fields = fields
            });
        }

        return sections.Count > 0 ? sections : null;
    }

    private static string? TryParseIntroduction(JsonElement root)
    {
        if (!root.TryGetProperty("x-introduction", out var introElement))
            return null;

        if (introElement.ValueKind != JsonValueKind.String)
            return null;

        var value = introElement.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static Dictionary<string, string>? TryParseFieldWidths(JsonElement root)
    {
        if (!root.TryGetProperty("properties", out var propertiesElement))
            return null;

        if (propertiesElement.ValueKind != JsonValueKind.Object)
            return null;

        var widths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in propertiesElement.EnumerateObject())
        {
            if (TryGetFieldWidth(property.Value, out var width) && width is not null)
                widths[property.Name] = width;
        }

        return widths.Count > 0 ? widths : null;
    }
}
