// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Models.Blueprints;
using Sorcha.UI.Core.Models.Templates;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for backend-driven template library.
/// </summary>
public interface ITemplateApiService
{
    Task<List<TemplateListItemViewModel>> GetTemplatesAsync(string? category = null, CancellationToken cancellationToken = default);
    Task<TemplateListItemViewModel?> GetTemplateAsync(string id, CancellationToken cancellationToken = default);
    Task<BlueprintListItemViewModel?> EvaluateTemplateAsync(string id, Dictionary<string, object> parameters, CancellationToken cancellationToken = default);
    Task<Sorcha.Blueprint.Models.Blueprint?> EvaluateTemplateForPreviewAsync(string id, Dictionary<string, object> parameters, CancellationToken cancellationToken = default);
    Task<bool> ValidateParametersAsync(string id, Dictionary<string, object> parameters, CancellationToken cancellationToken = default);
    Task IncrementUsageAsync(string templateId, CancellationToken cancellationToken = default);
}
