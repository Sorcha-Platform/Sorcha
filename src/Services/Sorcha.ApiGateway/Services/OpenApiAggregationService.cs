// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

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
                ["version"] = "1.0.0",
                ["description"] = """
                    # Sorcha Distributed Ledger Platform

                    ## Overview

                    Sorcha is a **distributed ledger platform** for building secure, auditable, multi-party workflows with cryptographic guarantees. It combines blockchain-inspired immutability with practical enterprise features like multi-tenancy, selective disclosure, and workflow orchestration.

                    ## Platform Architecture

                    The Sorcha platform consists of five core microservices:

                    ### 🏢 Tenant Service
                    **Multi-tenant organization management and authentication**
                    - Manages organizations, users, and service principals
                    - JWT-based authentication for all platform services
                    - Role-based access control (RBAC)
                    - Organization isolation and data boundaries

                    **Endpoints:** `/api/tenant/*`

                    ### 💰 Wallet Service
                    **Cryptographic wallet management and transaction signing**
                    - HD wallet generation (BIP39/BIP44)
                    - Multi-algorithm support (ED25519, NIST P-256, RSA-4096)
                    - Secure key storage with HSM integration
                    - Transaction signing for ledger submissions

                    **Endpoints:** `/api/wallet/*`

                    ### 📚 Register Service
                    **Distributed ledger for immutable transaction storage**
                    - Append-only transaction ledger with chain integrity
                    - Cryptographic signature verification
                    - Wallet-based payload encryption
                    - Real-time notifications via SignalR
                    - OData v4 querying capabilities

                    **Endpoints:** `/api/register/*`

                    ### 🔄 Blueprint Service
                    **Workflow orchestration and execution**
                    - JSON-based workflow definitions
                    - Action routing and state management
                    - Selective disclosure rules
                    - Template-based workflow creation
                    - Integration with Wallet and Register services

                    **Endpoints:** `/api/blueprint/*`

                    ### 🌐 Peer Service
                    **P2P network monitoring and coordination**
                    - Peer discovery and health monitoring
                    - Network statistics and quality metrics
                    - Circuit breaker pattern for resilience
                    - gRPC-based peer communication

                    **Endpoints:** `/api/peer/*`

                    ## Getting Started

                    ### 1. Authentication
                    All API calls require authentication via the Tenant Service:

                    ```http
                    POST /api/tenant/api/service-auth/token
                    Content-Type: application/json

                    {
                      "clientId": "your-client-id",
                      "clientSecret": "your-client-secret"
                    }
                    ```

                    **Response:**
                    ```json
                    {
                      "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
                      "expiresAt": "2025-12-11T11:30:00Z"
                    }
                    ```

                    ### 2. Create Organization
                    ```http
                    POST /api/tenant/api/organizations
                    Authorization: Bearer {token}
                    Content-Type: application/json

                    {
                      "name": "Acme Corporation",
                      "displayName": "Acme Corp"
                    }
                    ```

                    ### 3. Create Wallet
                    ```http
                    POST /api/wallet/api/wallets
                    Authorization: Bearer {token}
                    Content-Type: application/json

                    {
                      "name": "Primary Wallet",
                      "algorithm": "ED25519",
                      "wordCount": 12
                    }
                    ```

                    ⚠️ **CRITICAL**: Save the returned mnemonic phrase securely!

                    ### 4. Create Register
                    ```http
                    POST /api/register/api/registers
                    Authorization: Bearer {token}
                    Content-Type: application/json

                    {
                      "registerId": "my-ledger-001",
                      "organizationId": "org-123"
                    }
                    ```

                    ### 5. Submit Transaction
                    ```http
                    POST /api/register/api/registers/{registerId}/transactions
                    Authorization: Bearer {token}
                    Content-Type: application/json

                    {
                      "registerId": "my-ledger-001",
                      "senderWallet": "wallet-id",
                      "payloads": [...],
                      "signature": "...",
                      "metadata": {...}
                    }
                    ```

                    ## Common Workflows

                    ### Document Timestamping
                    1. Create organization and wallet
                    2. Create register for document management
                    3. Hash document and submit as transaction
                    4. Transaction provides cryptographic proof of existence

                    ### Multi-Party Workflow
                    1. Create blueprint defining workflow steps
                    2. Each participant creates a wallet
                    3. Actions execute in sequence, creating transactions
                    4. Selective disclosure controls data visibility
                    5. Immutable audit trail in register

                    ### Audit Trail Creation
                    1. System events logged as transactions
                    2. Each event signed by system wallet
                    3. Transactions chained for integrity
                    4. OData queries for compliance reporting

                    ## Key Features

                    ### 🔒 Security
                    - JWT-based authentication
                    - Cryptographic signature verification
                    - AES-256-GCM encryption at rest
                    - HSM integration (Azure Key Vault, AWS KMS)
                    - OWASP security headers
                    - Rate limiting and DDoS protection

                    ### 🔐 Privacy
                    - Selective disclosure per participant
                    - Wallet-based payload encryption
                    - Organization data isolation
                    - No PII in transaction metadata

                    ### ✅ Integrity
                    - Immutable transaction chains
                    - Merkle chain verification
                    - Cryptographic signatures required
                    - Tamper-evident design

                    ### 📊 Auditability
                    - Complete transaction history
                    - Timestamped events
                    - OData v4 querying
                    - Real-time notifications

                    ## API Standards

                    ### Authentication
                    All endpoints (except `/api/tenant/api/service-auth/token`) require:
                    ```
                    Authorization: Bearer {jwt-token}
                    ```

                    ### Error Responses
                    Standard HTTP status codes with JSON error details:
                    ```json
                    {
                      "error": "Error description",
                      "details": "Additional context"
                    }
                    ```

                    ### Pagination
                    OData endpoints support pagination:
                    ```
                    GET /odata/Transactions?$top=50&$skip=100
                    ```

                    ### Filtering
                    OData v4 query syntax:
                    ```
                    GET /odata/Transactions?$filter=SenderWallet eq 'wallet-123' and TimeStamp gt 2025-01-01
                    ```

                    ## Client Libraries

                    ### .NET CLI
                    ```bash
                    sorcha auth login --profile docker
                    sorcha org create --name "Acme Corp"
                    sorcha wallet create --name "My Wallet" --algorithm ED25519
                    ```

                    ### SDK Generation
                    OpenAPI specs can be used to generate client SDKs:
                    - C# (recommended)
                    - TypeScript/JavaScript
                    - Python
                    - Go

                    ## Support & Resources

                    - **API Gateway**: `http://localhost:8080` (Docker)
                    - **Scalar Documentation**: `http://localhost:8080/scalar`
                    - **Health Check**: `http://localhost:8080/api/health`
                    - **Dashboard**: `http://localhost:8080/api/dashboard`
                    - **GitHub**: https://github.com/sorcha-platform/sorcha

                    ## Version Information

                    - **Platform Version**: 1.0.0
                    - **OpenAPI Version**: 3.0.1
                    - **.NET Version**: 10.0
                    - **License**: MIT

                    ---

                    **⚠️ Note**: This is aggregated documentation from all Sorcha platform services. Each service also has its own detailed documentation available at its individual OpenAPI endpoint.
                    """,
                ["contact"] = new JsonObject
                {
                    ["name"] = "Sorcha Contributors",
                    ["url"] = "https://github.com/sorcha-platform/sorcha"
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
