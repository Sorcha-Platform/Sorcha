// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Common;
using Sorcha.UI.Core.Models.Explorer;
using Sorcha.UI.Core.Models.Registers;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Implementation of <see cref="IODataQueryService"/> calling the Register Service OData endpoints.
/// </summary>
public class ODataQueryService : IODataQueryService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ODataQueryService> _logger;

    public ODataQueryService(HttpClient httpClient, ILogger<ODataQueryService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PaginatedList<TransactionViewModel>> ExecuteTransactionQueryAsync(ODataQueryModel query, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = BuildODataUrl("/odata/Transactions", query);
            var result = await _httpClient.GetFromJsonAsync<PaginatedList<TransactionViewModel>>(url, cancellationToken);
            return result ?? new PaginatedList<TransactionViewModel>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing transaction query");
            return new PaginatedList<TransactionViewModel>();
        }
    }

    public async Task<PaginatedList<RegisterViewModel>> ExecuteRegisterQueryAsync(ODataQueryModel query, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = BuildODataUrl("/odata/Registers", query);
            var result = await _httpClient.GetFromJsonAsync<PaginatedList<RegisterViewModel>>(url, cancellationToken);
            return result ?? new PaginatedList<RegisterViewModel>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing register query");
            return new PaginatedList<RegisterViewModel>();
        }
    }

    public string BuildFilterString(ODataQueryModel model)
    {
        if (model.Filters.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        for (var i = 0; i < model.Filters.Count; i++)
        {
            var filter = model.Filters[i];

            if (!IsValidFieldName(filter.Field))
            {
                _logger.LogWarning("Rejected invalid OData field name: {FieldName}", filter.Field);
                continue;
            }

            if (i > 0 && sb.Length > 0)
            {
                sb.Append($" {filter.LogicalOperator} ");
            }

            sb.Append(filter.Operator switch
            {
                "contains" => $"contains({filter.Field}, '{EscapeODataValue(filter.Value)}')",
                "startswith" => $"startswith({filter.Field}, '{EscapeODataValue(filter.Value)}')",
                _ => $"{filter.Field} {filter.Operator} '{EscapeODataValue(filter.Value)}'"
            });
        }

        return sb.ToString();
    }

    private string BuildODataUrl(string baseUrl, ODataQueryModel query)
    {
        var parts = new List<string>();

        var filter = BuildFilterString(query);
        if (!string.IsNullOrEmpty(filter))
            parts.Add($"$filter={Uri.EscapeDataString(filter)}");

        if (!string.IsNullOrEmpty(query.OrderBy) && IsValidFieldName(query.OrderBy))
            parts.Add($"$orderby={Uri.EscapeDataString(query.OrderBy)} {query.OrderDirection}");

        parts.Add($"$top={query.Top}");
        parts.Add($"$skip={query.Skip}");
        parts.Add("$count=true");

        return $"{baseUrl}?{string.Join("&", parts)}";
    }

    private static readonly Regex SafeFieldNamePattern = new(@"^[A-Za-z_][A-Za-z0-9_./]*$", RegexOptions.Compiled);

    /// <summary>
    /// Validates that an OData field name contains only safe characters to prevent injection.
    /// </summary>
    internal static bool IsValidFieldName(string fieldName) =>
        !string.IsNullOrWhiteSpace(fieldName) && SafeFieldNamePattern.IsMatch(fieldName);

    private static string EscapeODataValue(string value) =>
        value.Replace("'", "''");
}
