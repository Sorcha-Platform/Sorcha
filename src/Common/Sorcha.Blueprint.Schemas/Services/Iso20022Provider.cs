// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Schemas.DTOs;

namespace Sorcha.Blueprint.Schemas.Services;

/// <summary>
/// Provider for ISO 20022 financial messaging schemas.
/// Serves pre-built JSON Schema representations of common payment and reporting messages.
/// </summary>
public class Iso20022Provider : IExternalSchemaProvider
{
    private readonly ILogger<Iso20022Provider> _logger;
    private List<ExternalSchemaResult>? _catalogCache;

    private const string ProviderDisplayName = "ISO 20022";

    /// <summary>
    /// Curated ISO 20022 messages most relevant for financial data exchange.
    /// </summary>
    private static readonly Dictionary<string, string> CuratedMessages = new()
    {
        ["pacs.008"] = "FI to FI Customer Credit Transfer — single customer credit transfer instruction",
        ["pacs.002"] = "FI to FI Payment Status Report — status of a payment instruction",
        ["pacs.004"] = "Payment Return — return of funds previously transferred",
        ["pain.001"] = "Customer Credit Transfer Initiation — batch payment initiation by debtor",
        ["pain.002"] = "Customer Payment Status Report — status report on payment initiation",
        ["pain.008"] = "Customer Direct Debit Initiation — batch direct debit instruction",
        ["camt.053"] = "Bank to Customer Statement — end-of-day account statement",
        ["camt.052"] = "Bank to Customer Account Report — intraday account report",
        ["camt.054"] = "Bank to Customer Debit/Credit Notification — real-time transaction notification",
        ["camt.056"] = "FI to FI Payment Cancellation Request — request to cancel a payment",
        ["head.001"] = "Business Application Header — routing and identification metadata for messages",
        ["acmt.001"] = "Account Opening Request — request to open a new bank account",
    };

    public Iso20022Provider(ILogger<Iso20022Provider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderName => ProviderDisplayName;
    public Models.ProviderType ProviderType => Models.ProviderType.StaticFile;
    public string[] DefaultSectorTags => ["finance"];

    public Task<ExternalSchemaSearchResponse> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult(new ExternalSchemaSearchResponse([], 0, ProviderName, query));

        var catalog = BuildCatalog();
        var matches = catalog
            .Where(r => r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        r.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult(new ExternalSchemaSearchResponse(matches, matches.Count, ProviderName, query));
    }

    public Task<ExternalSchemaResult?> GetSchemaAsync(string schemaUrl, CancellationToken cancellationToken = default)
    {
        var catalog = BuildCatalog();
        return Task.FromResult(catalog.FirstOrDefault(r =>
            string.Equals(r.Url, schemaUrl, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<IEnumerable<ExternalSchemaResult>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<ExternalSchemaResult>>(BuildCatalog());
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true); // Static schemas
    }

    private List<ExternalSchemaResult> BuildCatalog()
    {
        if (_catalogCache is not null) return _catalogCache;

        _catalogCache = CuratedMessages.Select(kvp =>
        {
            var (messageId, description) = kvp;
            return new ExternalSchemaResult(
                Name: $"ISO 20022 {messageId}",
                Description: $"ISO 20022 {messageId} — {description}",
                Url: $"urn:iso:std:iso:20022:{messageId}",
                Provider: ProviderDisplayName,
                Content: BuildMessageSchema(messageId, description));
        }).ToList();

        _logger.LogInformation("Built {Count} ISO 20022 message schemas", _catalogCache.Count);
        return _catalogCache;
    }

    internal static JsonDocument BuildMessageSchema(string messageId, string description)
    {
        var properties = new JsonObject
        {
            ["messageId"] = new JsonObject { ["type"] = "string", ["description"] = "Message identification" },
            ["creationDateTime"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
        };

        // Add domain-specific properties
        if (messageId.StartsWith("pacs") || messageId.StartsWith("pain"))
        {
            properties["debtorAccount"] = new JsonObject { ["type"] = "object" };
            properties["creditorAccount"] = new JsonObject { ["type"] = "object" };
            properties["amount"] = new JsonObject { ["type"] = "number" };
            properties["currency"] = new JsonObject { ["type"] = "string" };
        }

        if (messageId.StartsWith("camt"))
        {
            properties["account"] = new JsonObject { ["type"] = "object" };
            properties["balance"] = new JsonObject { ["type"] = "number" };
            properties["entries"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "object" } };
        }

        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = $"urn:iso:std:iso:20022:{messageId}",
            ["title"] = $"ISO 20022 {messageId}",
            ["description"] = $"ISO 20022 {messageId} — {description}",
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray("messageId", "creationDateTime"),
        };

        return JsonDocument.Parse(schema.ToJsonString());
    }
}
