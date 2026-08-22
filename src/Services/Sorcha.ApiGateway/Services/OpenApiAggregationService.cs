// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sorcha.ApiGateway.Services;

/// <summary>
/// Service for aggregating OpenAPI documentation from all backend services
/// </summary>
public class OpenApiAggregationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenApiAggregationService> _logger;
    private readonly Dictionary<string, ServiceOpenApiConfig> _serviceConfigs;

    public OpenApiAggregationService(
        IHttpClientFactory httpClientFactory,
        ILogger<OpenApiAggregationService> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        // Load service configurations
        _serviceConfigs = new Dictionary<string, ServiceOpenApiConfig>
        {
            {
                "blueprint",
                new ServiceOpenApiConfig
                {
                    Name = "Blueprint API",
                    BaseUrl = configuration["Services:Blueprint:Url"] ?? "http://blueprint-api",
                    OpenApiPath = "/openapi/v1.json",
                    PathPrefix = "/api/blueprint"
                }
            },
            {
                "tenant",
                new ServiceOpenApiConfig
                {
                    Name = "Tenant Service",
                    BaseUrl = configuration["Services:Tenant:Url"] ?? "http://tenant-service",
                    OpenApiPath = "/openapi/v1.json",
                    PathPrefix = "/api/tenant"
                }
            },
            {
                "wallet",
                new ServiceOpenApiConfig
                {
                    Name = "Wallet Service",
                    BaseUrl = configuration["Services:Wallet:Url"] ?? "http://wallet-service",
                    OpenApiPath = "/openapi/v1.json",
                    PathPrefix = "/api/wallet"
                }
            },
            {
                "register",
                new ServiceOpenApiConfig
                {
                    Name = "Register Service",
                    BaseUrl = configuration["Services:Register:Url"] ?? "http://register-service",
                    OpenApiPath = "/openapi/v1.json",
                    PathPrefix = "/api/register"
                }
            },
            {
                "peer",
                new ServiceOpenApiConfig
                {
                    Name = "Peer Service",
                    BaseUrl = configuration["Services:Peer:Url"] ?? "http://peer-service",
                    OpenApiPath = "/openapi/v1.json",
                    PathPrefix = "/api/peer"
                }
            }
        };
    }

    /// <summary>
    /// Gets aggregated OpenAPI documentation from all services
    /// </summary>
    public async Task<JsonObject> GetAggregatedOpenApiAsync(CancellationToken cancellationToken = default)
    {
        // Create base OpenAPI document
        var aggregated = new JsonObject
        {
            ["openapi"] = "3.0.1",
            ["info"] = new JsonObject
            {
                ["title"] = "Sorcha Platform API",
                ["version"] = ResolveAssemblyInformationalVersion(),
                ["description"] = "Sorcha is programmable proof infrastructure for multi-party workflows — every action is wallet-signed, every record is Merkle-chained on an immutable register, every disclosure is cryptographically bounded rather than policy-enforced. AI agents and integrators can use this aggregated OpenAPI surface to discover the full platform: tenant authentication, HD wallets and signing (BIP32/39/44, ML-DSA FIPS 204), distributed register read/write, blueprint-driven multi-party workflow orchestration, peer-to-peer replication, and HAIP 1.0 OpenID4VCI / OpenID4VP credential issuance and verification. See `docs/quickstart.md` for setup, `STANDARDS.md` for the full standards posture, and `/.well-known/mcp.json` for the MCP server manifest.",
                ["contact"] = new JsonObject
                {
                    ["name"] = "Sorcha Contributors",
                    ["url"] = "https://github.com/Sorcha-Platform/Sorcha"
                },
                ["license"] = new JsonObject
                {
                    ["name"] = "MIT License",
                    ["url"] = "https://opensource.org/licenses/MIT"
                }
            },
            ["servers"] = new JsonArray
            {
                new JsonObject
                {
                    ["url"] = "http://localhost:8080",
                    ["description"] = "API Gateway (Docker)"
                },
                new JsonObject
                {
                    ["url"] = "/",
                    ["description"] = "Relative URL"
                }
            },
            ["paths"] = new JsonObject(),
            ["components"] = new JsonObject
            {
                ["schemas"] = new JsonObject()
            },
            ["tags"] = new JsonArray()
        };

        var paths = aggregated["paths"]!.AsObject();
        var schemas = aggregated["components"]!["schemas"]!.AsObject();
        var tags = aggregated["tags"]!.AsArray();

        // Add gateway-specific endpoints
        AddGatewayEndpoints(paths, tags);

        // Fetch and merge OpenAPI from each service
        foreach (var (serviceName, config) in _serviceConfigs)
        {
            try
            {
                var serviceOpenApi = await FetchServiceOpenApiAsync(config, cancellationToken);
                if (serviceOpenApi != null)
                {
                    MergeServiceOpenApi(serviceOpenApi, paths, schemas, tags, config);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch OpenAPI from service {Service}", serviceName);
            }
        }

        return aggregated;
    }

    private async Task<JsonObject?> FetchServiceOpenApiAsync(
        ServiceOpenApiConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var url = $"{config.BaseUrl}{config.OpenApiPath}";
            var response = await client.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch OpenAPI from {Url}: {StatusCode}", url, response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonNode.Parse(content)?.AsObject();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching OpenAPI from {Service}", config.Name);
            return null;
        }
    }

    private void MergeServiceOpenApi(
        JsonObject serviceOpenApi,
        JsonObject targetPaths,
        JsonObject targetSchemas,
        JsonArray targetTags,
        ServiceOpenApiConfig config)
    {
        // Merge paths with prefix
        if (serviceOpenApi["paths"] is JsonObject servicePaths)
        {
            foreach (var (path, pathItem) in servicePaths)
            {
                var prefixedPath = $"{config.PathPrefix}{path}";
                targetPaths[prefixedPath] = pathItem?.DeepClone();

                // Update path descriptions to include service name
                if (pathItem is JsonObject pathObj)
                {
                    foreach (var method in new[] { "get", "post", "put", "delete", "patch" })
                    {
                        if (pathObj[method] is JsonObject operation)
                        {
                            var summary = operation["summary"]?.GetValue<string>() ?? "";
                            operation["summary"] = $"[{config.Name}] {summary}";

                            // Update tags to include service name
                            if (operation["tags"] is JsonArray opTags)
                            {
                                for (int i = 0; i < opTags.Count; i++)
                                {
                                    var tag = opTags[i]?.GetValue<string>() ?? "";
                                    opTags[i] = $"{config.Name}/{tag}";
                                }
                            }
                        }
                    }
                }
            }
        }

        // Merge schemas
        if (serviceOpenApi["components"]?["schemas"] is JsonObject serviceSchemas)
        {
            foreach (var (schemaName, schema) in serviceSchemas)
            {
                var prefixedName = $"{config.Name.Replace(" ", "")}_{schemaName}";
                targetSchemas[prefixedName] = schema?.DeepClone();
            }
        }

        // Merge tags
        if (serviceOpenApi["tags"] is JsonArray serviceTags)
        {
            foreach (var tag in serviceTags)
            {
                if (tag is JsonObject tagObj)
                {
                    var tagName = tagObj["name"]?.GetValue<string>() ?? "";
                    tagObj["name"] = $"{config.Name}/{tagName}";
                    targetTags.Add(tagObj.DeepClone());
                }
            }
        }
    }

    private void AddGatewayEndpoints(JsonObject paths, JsonArray tags)
    {
        // Add Gateway tag
        tags.Add(new JsonObject
        {
            ["name"] = "Gateway",
            ["description"] = "API Gateway endpoints for health, stats, and client management"
        });

        // These are documented via the gateway's own OpenAPI
        // The gateway endpoints will be included automatically
    }

    /// <summary>
    /// Returns the entry assembly's informational version, stripped of any "+commitHash" suffix.
    /// Single source of truth for FR-046 — both the gateway's own OpenAPI document and this aggregated
    /// document expose the same version, sourced from the same underlying assembly metadata.
    /// </summary>
    private static string ResolveAssemblyInformationalVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly();
        var raw = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
        var plusIdx = raw.IndexOf('+');
        return plusIdx > 0 ? raw[..plusIdx] : raw;
    }
}

/// <summary>
/// Configuration for a service's OpenAPI endpoint
/// </summary>
public class ServiceOpenApiConfig
{
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string OpenApiPath { get; set; } = string.Empty;
    public string PathPrefix { get; set; } = string.Empty;
}
