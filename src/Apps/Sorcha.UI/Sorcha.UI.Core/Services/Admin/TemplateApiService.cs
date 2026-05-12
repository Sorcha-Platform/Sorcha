// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Models.Blueprints;
using Sorcha.UI.Core.Models.Templates;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Implementation of <see cref="ITemplateApiService"/> calling the Blueprint Service API.
/// </summary>
public class TemplateApiService : ITemplateApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TemplateApiService> _logger;

    public TemplateApiService(HttpClient httpClient, ILogger<TemplateApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<TemplateListItemViewModel>> GetTemplatesAsync(string? category = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = "/api/templates";
            if (!string.IsNullOrEmpty(category)) url += $"?category={Uri.EscapeDataString(category)}";

            var templates = await _httpClient.GetFromJsonAsync<List<BlueprintTemplate>>(url, cancellationToken);
            return templates?.Select(MapToViewModel).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching templates");
            return [];
        }
    }

    public async Task<TemplateListItemViewModel?> GetTemplateAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var template = await _httpClient.GetFromJsonAsync<BlueprintTemplate>($"/api/templates/{id}", cancellationToken);
            return template is not null ? MapToViewModel(template) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching template {Id}", id);
            return null;
        }
    }

    public async Task<BlueprintListItemViewModel?> EvaluateTemplateAsync(string id, Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/templates/evaluate",
                new { templateId = id, parameters }, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<BlueprintListItemViewModel>(cancellationToken: cancellationToken);
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating template {Id}", id);
            return null;
        }
    }

    public async Task<Sorcha.Blueprint.Models.Blueprint?> EvaluateTemplateForPreviewAsync(string id, Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/templates/evaluate",
                new { templateId = id, parameters }, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<TemplateEvaluationResult>(cancellationToken: cancellationToken);
                return result?.Blueprint;
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating template {Id} for preview", id);
            return null;
        }
    }

    public async Task<bool> ValidateParametersAsync(string id, Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"/api/templates/{id}/validate",
                new { parameters }, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating template parameters {Id}", id);
            return false;
        }
    }

    public async Task IncrementUsageAsync(string templateId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _httpClient.PostAsync($"/api/templates/{templateId}/increment-usage", null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error incrementing usage for template {Id}", templateId);
        }
    }

    private static TemplateListItemViewModel MapToViewModel(BlueprintTemplate template)
    {
        return new TemplateListItemViewModel
        {
            Id = template.Id,
            Title = template.Title,
            Description = template.Description,
            Category = template.Category ?? string.Empty,
            Version = template.Version,
            UsageCount = ExtractUsageCount(template.Metadata),
            Parameters = ExtractParameters(template.ParameterSchema, template.DefaultParameters)
        };
    }

    private static List<TemplateParameter> ExtractParameters(JsonDocument? parameterSchema, Dictionary<string, object>? defaultParameters)
    {
        if (parameterSchema is null)
            return [];

        var parameters = new List<TemplateParameter>();

        try
        {
            var root = parameterSchema.RootElement;

            if (!root.TryGetProperty("properties", out var properties))
                return [];

            var requiredSet = new HashSet<string>(StringComparer.Ordinal);
            if (root.TryGetProperty("required", out var requiredArray) && requiredArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in requiredArray.EnumerateArray())
                {
                    if (item.GetString() is { } name)
                        requiredSet.Add(name);
                }
            }

            foreach (var prop in properties.EnumerateObject())
            {
                var paramName = prop.Name;
                var paramDef = prop.Value;

                var type = paramDef.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "string" : "string";
                var description = paramDef.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? string.Empty : string.Empty;

                string? defaultValue = null;
                if (defaultParameters is not null && defaultParameters.TryGetValue(paramName, out var defVal))
                {
                    defaultValue = defVal switch
                    {
                        JsonElement je => je.ToString(),
                        _ => defVal?.ToString()
                    };
                }
                else if (paramDef.TryGetProperty("default", out var schemaDef))
                {
                    defaultValue = schemaDef.ToString();
                }

                parameters.Add(new TemplateParameter
                {
                    Name = paramName,
                    Description = description,
                    Type = type,
                    DefaultValue = defaultValue,
                    Required = requiredSet.Contains(paramName)
                });
            }
        }
        catch
        {
            // If schema parsing fails, return empty list
        }

        return parameters;
    }

    private static int ExtractUsageCount(Dictionary<string, string>? metadata)
    {
        if (metadata is not null && metadata.TryGetValue("usageCount", out var countStr) && int.TryParse(countStr, out var count))
            return count;

        return 0;
    }
}
