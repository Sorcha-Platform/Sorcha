// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Schemas.DTOs;

namespace Sorcha.Blueprint.Schemas.Services;

/// <summary>
/// Provider for OASIS UBL 2.3 document schemas.
/// Serves pre-built schemas for common business documents (Invoice, Order, etc.).
/// </summary>
public class UblSchemaProvider : IExternalSchemaProvider
{
    private readonly ILogger<UblSchemaProvider> _logger;
    private List<ExternalSchemaResult>? _catalogCache;

    private const string ProviderDisplayName = "OASIS UBL";

    /// <summary>
    /// Curated UBL document types most relevant for commercial data exchange.
    /// </summary>
    private static readonly Dictionary<string, string> CuratedDocuments = new()
    {
        ["Invoice"] = "A commercial invoice requesting payment for goods or services",
        ["CreditNote"] = "A document notifying a credit to the buyer",
        ["DebitNote"] = "A document notifying a debit to the buyer",
        ["Order"] = "A purchase order for goods or services",
        ["OrderResponse"] = "A response to a purchase order",
        ["DespatchAdvice"] = "Notification of a shipment of goods",
        ["ReceiptAdvice"] = "Acknowledgement of receipt of goods",
        ["Quotation"] = "A quotation for goods or services",
        ["RequestForQuotation"] = "A request for a supplier quotation",
        ["Waybill"] = "A transport document for goods",
        ["BillOfLading"] = "A carrier's receipt for goods accepted for transport",
        ["PackingList"] = "A list of the contents of a package",
        ["Certificate"] = "A document certifying a fact or condition",
        ["Catalogue"] = "A document describing goods and services offered",
        ["Statement"] = "A financial statement of account",
        ["Reminder"] = "A reminder for payment due",
    };

    public UblSchemaProvider(ILogger<UblSchemaProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderName => ProviderDisplayName;
    public Models.ProviderType ProviderType => Models.ProviderType.StaticFile;
    public string[] DefaultSectorTags => ["finance", "commerce"];

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

        _catalogCache = CuratedDocuments.Select(kvp =>
        {
            var (docType, description) = kvp;
            return new ExternalSchemaResult(
                Name: $"UBL {docType}",
                Description: $"OASIS UBL 2.3 {docType} — {description}",
                Url: $"urn:oasis:names:specification:ubl:schema:xsd:{docType}-2",
                Provider: ProviderDisplayName,
                Content: BuildDocumentSchema(docType, description));
        }).ToList();

        _logger.LogInformation("Built {Count} UBL document schemas", _catalogCache.Count);
        return _catalogCache;
    }

    internal static JsonDocument BuildDocumentSchema(string docType, string description)
    {
        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = $"urn:oasis:names:specification:ubl:schema:xsd:{docType}-2",
            ["title"] = $"UBL {docType}",
            ["description"] = $"OASIS UBL 2.3 {docType} — {description}",
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["UBLVersionID"] = new JsonObject { ["type"] = "string", ["const"] = "2.3" },
                ["ID"] = new JsonObject { ["type"] = "string", ["description"] = "Document identifier" },
                ["IssueDate"] = new JsonObject { ["type"] = "string", ["format"] = "date" },
                ["IssueTime"] = new JsonObject { ["type"] = "string", ["format"] = "time" },
                ["Note"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                ["DocumentCurrencyCode"] = new JsonObject { ["type"] = "string", ["description"] = "ISO 4217 currency code" },
            },
            ["required"] = new JsonArray("ID", "IssueDate"),
        };

        return JsonDocument.Parse(schema.ToJsonString());
    }
}
