// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Schemas.DTOs;

namespace Sorcha.Blueprint.Schemas.Services;

/// <summary>
/// Provider for W3C Verifiable Credentials data model schemas.
/// Serves pre-built VC and VP schemas (already draft-2020-12).
/// </summary>
public class W3cVcProvider : IExternalSchemaProvider
{
    private readonly ILogger<W3cVcProvider> _logger;
    private List<ExternalSchemaResult>? _catalogCache;

    private const string ProviderDisplayName = "W3C VC";

    public W3cVcProvider(ILogger<W3cVcProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderName => ProviderDisplayName;
    public Models.ProviderType ProviderType => Models.ProviderType.StaticFile;
    public string[] DefaultSectorTags => ["identity", "government"];

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
        return Task.FromResult(true); // Static schemas — always available
    }

    private List<ExternalSchemaResult> BuildCatalog()
    {
        if (_catalogCache is not null) return _catalogCache;

        _catalogCache =
        [
            new ExternalSchemaResult(
                Name: "Verifiable Credential",
                Description: "W3C Verifiable Credential (VC) data model — a tamper-evident credential with cryptographic proof",
                Url: "https://www.w3.org/ns/credentials/v2#VerifiableCredential",
                Provider: ProviderDisplayName,
                Content: BuildVcSchema()),
            new ExternalSchemaResult(
                Name: "Verifiable Presentation",
                Description: "W3C Verifiable Presentation (VP) — a collection of verifiable credentials presented to a verifier",
                Url: "https://www.w3.org/ns/credentials/v2#VerifiablePresentation",
                Provider: ProviderDisplayName,
                Content: BuildVpSchema()),
            new ExternalSchemaResult(
                Name: "Credential Subject",
                Description: "W3C Credential Subject — the entity about which claims are made",
                Url: "https://www.w3.org/ns/credentials/v2#CredentialSubject",
                Provider: ProviderDisplayName,
                Content: BuildCredentialSubjectSchema()),
        ];

        _logger.LogInformation("Built {Count} W3C VC schemas", _catalogCache.Count);
        return _catalogCache;
    }

    private static JsonDocument BuildVcSchema()
    {
        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = "https://www.w3.org/ns/credentials/v2#VerifiableCredential",
            ["title"] = "Verifiable Credential",
            ["description"] = "W3C Verifiable Credential data model v2.0",
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["@context"] = new JsonObject { ["type"] = "array" },
                ["id"] = new JsonObject { ["type"] = "string", ["format"] = "uri" },
                ["type"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                ["issuer"] = new JsonObject { ["type"] = "string", ["format"] = "uri" },
                ["issuanceDate"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
                ["expirationDate"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
                ["credentialSubject"] = new JsonObject { ["type"] = "object" },
                ["proof"] = new JsonObject { ["type"] = "object" },
            },
            ["required"] = new JsonArray("@context", "type", "issuer", "credentialSubject"),
        };
        return JsonDocument.Parse(schema.ToJsonString());
    }

    private static JsonDocument BuildVpSchema()
    {
        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = "https://www.w3.org/ns/credentials/v2#VerifiablePresentation",
            ["title"] = "Verifiable Presentation",
            ["description"] = "W3C Verifiable Presentation data model v2.0",
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["@context"] = new JsonObject { ["type"] = "array" },
                ["id"] = new JsonObject { ["type"] = "string", ["format"] = "uri" },
                ["type"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                ["holder"] = new JsonObject { ["type"] = "string", ["format"] = "uri" },
                ["verifiableCredential"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "object" },
                },
                ["proof"] = new JsonObject { ["type"] = "object" },
            },
            ["required"] = new JsonArray("@context", "type", "verifiableCredential"),
        };
        return JsonDocument.Parse(schema.ToJsonString());
    }

    private static JsonDocument BuildCredentialSubjectSchema()
    {
        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = "https://www.w3.org/ns/credentials/v2#CredentialSubject",
            ["title"] = "Credential Subject",
            ["description"] = "The entity about which claims are made in a verifiable credential",
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["id"] = new JsonObject { ["type"] = "string", ["format"] = "uri", ["description"] = "The DID or URI identifying the subject" },
            },
            ["additionalProperties"] = true,
        };
        return JsonDocument.Parse(schema.ToJsonString());
    }
}
