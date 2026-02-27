# Sorcha API Gateway

**Version**: 1.0.0
**Status**: Production Ready (95% Complete - Minor Enhancements Pending)
**Framework**: .NET 10.0
**Architecture**: Reverse Proxy (YARP)

---

## Overview

The **API Gateway** is the unified entry point for all Sorcha platform services, providing a single external endpoint that routes requests to backend microservices. Built on Microsoft YARP (Yet Another Reverse Proxy), it implements the API Gateway pattern with intelligent routing, health aggregation, OpenAPI documentation consolidation, and client distribution.

This service acts as the platform front door for:
- **Request routing** to Blueprint, Wallet, Register, Tenant, Peer, and Validator services
- **Health aggregation** from all backend services
- **OpenAPI consolidation** for unified API documentation
- **Client distribution** packaging and serving the Blazor WebAssembly client
- **CORS management** for frontend applications
- **Service discovery** via .NET Aspire integration

### Key Features

- **YARP Reverse Proxy**: High-performance reverse proxy with flexible routing rules
- **Path Transformations**: Automatic path rewriting for backend services
- **Health Aggregation**: Real-time health status from all microservices
- **OpenAPI Aggregation**: Consolidated API documentation from all services
- **Client Download**: Package and serve Blazor WebAssembly client source code
- **Interactive Landing Page**: Beautiful HTML dashboard with system statistics
- **CORS Support**: Configured for secure cross-origin requests
- **Service Discovery**: Automatic service resolution via .NET Aspire
- **Load Balancing**: Built-in support for multiple service instances (future)
- **Circuit Breaker**: Automatic failure detection and recovery (future)

---

## Architecture

### Components

```
API Gateway (Single External Endpoint)
├── YARP Reverse Proxy
│   ├── Route Configuration (appsettings.json)
│   ├── Path Transformations
│   └── Cluster Management
├── Gateway APIs
│   ├── Health Aggregation (/api/health)
│   ├── System Statistics (/api/stats)
│   ├── Client Download (/api/client/download)
│   ├── Installation Instructions (/api/client/instructions)
│   └── OpenAPI Aggregation (/openapi/aggregated.json)
├── Services
│   ├── HealthAggregationService
│   ├── OpenApiAggregationService
│   └── ClientDownloadService
├── Landing Page (/)
│   └── Interactive HTML Dashboard
└── External Service References
    ├── Blueprint Service (http://blueprint-service)
    ├── Wallet Service (http://wallet-service)
    ├── Register Service (http://register-service)
    ├── Tenant Service (http://tenant-service)
    ├── Peer Service (http://peer-service)
    └── Validator Service (http://validator-service)
```

### Request Flow

```
External Client (Browser/Mobile/API Consumer)
      ↓
API Gateway (https://api.sorcha.io)
      ↓
YARP Routing Engine
      ↓
Match Request Path:
  - /api/blueprint/* → Blueprint Service
  - /api/wallets/*  → Wallet Service
  - /api/registers/* → Register Service (future)
  - /api/peer/*     → Peer Service
      ↓
Path Transformation
      ↓
Backend Service (internal network)
      ↓
Response → API Gateway → Client
```

### Routing Configuration

**Routing Rules:**

| External Path | Backend Service | Backend Path | Description |
|---------------|-----------------|--------------|-------------|
| `/api/blueprint/**` | Blueprint Service | `/api/**` | Blueprint CRUD, actions, templates |
| `/api/wallets/**` | Wallet Service | `/api/v1/wallets/**` | Wallet management, HD wallets |
| `/api/registers/**` | Register Service | `/api/**` | Register and transaction operations |
| `/api/tenant/**` | Tenant Service | `/api/**` | Tenant and organization management |
| `/api/peer/**` | Peer Service | `/api/**` | Peer discovery, gossip |
| `/api/validator/**` | Validator Service | `/api/v1/**` | Transaction validation, consensus |
| `/api/health` | API Gateway | - | Aggregated health from all services |
| `/api/stats` | API Gateway | - | System-wide statistics |
| `/api/client/download` | API Gateway | - | Blazor client package |
| `/` | API Gateway | - | Landing page dashboard |

---

## Quick Start

### Prerequisites

- **.NET 10 SDK** or later
- **Access to backend services** (Blueprint, Wallet, Register, Peer)
- **Git**

### 1. Clone and Navigate

```bash
git clone https://github.com/yourusername/Sorcha.git
cd Sorcha/src/Services/Sorcha.ApiGateway
```

### 2. Set Up Configuration

The service uses `appsettings.json` for routing configuration. For local development with .NET Aspire, service URLs are automatically resolved.

### 3. Run the Service

```bash
dotnet run
```

Service will start at:
- **HTTPS**: `https://localhost:7082`
- **HTTP**: `http://localhost:5082`
- **Landing Page**: `https://localhost:7082/`
- **Scalar API Docs**: `https://localhost:7082/scalar/v1`
- **Health Check**: `https://localhost:7082/api/health`

### 4. Test Routing

```bash
# Test Blueprint Service routing
curl https://localhost:7082/api/blueprint/status

# Test Wallet Service routing
curl https://localhost:7082/api/wallets/status

# Test health aggregation
curl https://localhost:7082/api/health

# Download Blazor client
curl -o sorcha-client.zip https://localhost:7082/api/client/download
```

---

## Configuration

### appsettings.json Structure

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Yarp": "Information"
    }
  },
  "AllowedHosts": "*",
  "Services": {
    "Blueprint": {
      "Url": "http://blueprint-api"
    },
    "Wallet": {
      "Url": "http://wallet-service"
    },
    "Register": {
      "Url": "http://register-service"
    },
    "Peer": {
      "Url": "http://peer-service"
    }
  },
  "ReverseProxy": {
    "Routes": {
      "blueprint-route": {
        "ClusterId": "blueprint-cluster",
        "Match": {
          "Path": "/api/blueprint/{**catch-all}"
        },
        "Transforms": [
          {
            "PathPattern": "/api/{**catch-all}"
          }
        ]
      },
      "wallet-route": {
        "ClusterId": "wallet-cluster",
        "Match": {
          "Path": "/api/wallets/{**catch-all}"
        },
        "Transforms": [
          {
            "PathPattern": "/api/v1/wallets/{**catch-all}"
          }
        ]
      }
    },
    "Clusters": {
      "blueprint-cluster": {
        "Destinations": {
          "destination1": {
            "Address": "http://blueprint-api"
          }
        }
      },
      "wallet-cluster": {
        "Destinations": {
          "destination1": {
            "Address": "http://wallet-service"
          }
        }
      }
    }
  }
}
```

### Environment Variables

For production deployment:

```bash
# Service URLs (Aspire auto-discovery in production)
SERVICES__BLUEPRINT__URL="https://blueprint.sorcha.io"
SERVICES__WALLET__URL="https://wallet.sorcha.io"
SERVICES__REGISTER__URL="https://register.sorcha.io"
SERVICES__PEER__URL="https://peer.sorcha.io"

# Gateway configuration
ASPNETCORE_URLS="https://+:443;http://+:80"
ASPNETCORE_HTTPS_PORT=443
```

### Adding a New Service Route

To add a new service to the gateway, update `appsettings.json`:

```json
{
  "Services": {
    "NewService": {
      "Url": "http://new-service"
    }
  },
  "ReverseProxy": {
    "Routes": {
      "newservice-route": {
        "ClusterId": "newservice-cluster",
        "Match": {
          "Path": "/api/newservice/{**catch-all}"
        },
        "Transforms": [
          {
            "PathPattern": "/api/{**catch-all}"
          }
        ]
      }
    },
    "Clusters": {
      "newservice-cluster": {
        "Destinations": {
          "destination1": {
            "Address": "http://new-service"
          }
        }
      }
    }
  }
}
```

Then update `HealthAggregationService.cs` to include the new service:

```csharp
_serviceEndpoints = new Dictionary<string, string>
{
    // ... existing services ...
    { "newservice", configuration["Services:NewService:Url"] ?? "http://new-service" },
};
```

---

## API Endpoints

### Gateway Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/` | Landing page with system dashboard |
| GET | `/api/health` | Aggregated health status from all services |
| GET | `/api/stats` | System-wide statistics |
| GET | `/api/dashboard` | Dashboard statistics (blueprints, wallets, registers, tenants, peers) |
| GET | `/api/client/info` | Blazor client information |
| GET | `/api/client/download` | Download Blazor client source code (ZIP) |
| GET | `/api/client/instructions` | Installation instructions (Markdown) |
| GET | `/openapi/aggregated.json` | Consolidated OpenAPI from all services |
| GET | `/scalar/v1` | Interactive API documentation (Scalar UI) |

### Proxied Service Endpoints

| External Endpoint | Backend Service |
|-------------------|-----------------|
| `/api/blueprint/*` | Blueprint Service |
| `/api/wallets/*` | Wallet Service |
| `/api/registers/*` | Register Service |
| `/api/tenant/*` | Tenant Service |
| `/api/peer/*` | Peer Service |
| `/api/validator/*` | Validator Service |

For detailed service endpoint documentation, visit `/scalar/v1`.

---

## Health Aggregation

### Health Check Response

**Endpoint**: `GET /api/health`

**Response Example:**
```json
{
  "status": "healthy",
  "timestamp": "2025-11-23T10:30:00Z",
  "services": {
    "blueprint": {
      "status": "healthy",
      "endpoint": "http://blueprint-api"
    },
    "wallet": {
      "status": "healthy",
      "endpoint": "http://wallet-service"
    },
    "register": {
      "status": "unhealthy",
      "endpoint": "http://register-service",
      "error": "Connection refused"
    },
    "peer": {
      "status": "healthy",
      "endpoint": "http://peer-service"
    }
  }
}
```

**Status Values:**
- **healthy**: All services are operational
- **degraded**: Some services are unhealthy, but platform is partially functional
- **unhealthy**: Majority of services are down

### System Statistics

**Endpoint**: `GET /api/stats`

**Response Example:**
```json
{
  "timestamp": "2025-11-23T10:30:00Z",
  "totalServices": 4,
  "healthyServices": 3,
  "unhealthyServices": 1,
  "serviceMetrics": {
    "blueprint": {
      "uptime": "5d 12h 34m",
      "requestCount": 15234,
      "errorRate": 0.02
    },
    "wallet": {
      "uptime": "5d 12h 34m",
      "requestCount": 45123,
      "errorRate": 0.01
    }
  }
}
```

### Dashboard Statistics

**Endpoint**: `GET /api/dashboard`

**Response Example:**
```json
{
  "timestamp": "2025-12-16T10:30:00Z",
  "totalBlueprints": 42,
  "totalBlueprintInstances": 156,
  "activeBlueprintInstances": 23,
  "totalWallets": 89,
  "totalRegisters": 12,
  "totalTransactions": 3421,
  "totalTenants": 5,
  "connectedPeers": 8
}
```

This endpoint aggregates platform-wide statistics from all backend services, including:
- **Blueprints**: Total blueprints and instances (active and total)
- **Wallets**: Total wallet count
- **Registers**: Total registers and transactions
- **Tenants**: Total tenant count
- **Peers**: Connected peer count from the Peer Service

---

## Client Download Service

### Download Blazor Client

**Endpoint**: `GET /api/client/download`

Downloads a ZIP package containing the Blazor WebAssembly client source code.

**Response**: `sorcha-client-20251123.zip`

**Contents:**
```
sorcha-client-20251123.zip
├── wwwroot/
├── Pages/
├── Shared/
├── Program.cs
├── appsettings.json
└── README.md
```

### Installation Instructions

**Endpoint**: `GET /api/client/instructions`

Returns Markdown-formatted installation instructions.

**Example:**
```bash
curl https://localhost:7082/api/client/instructions
```

**Response** (Markdown):
```markdown
# Sorcha Blazor Client Installation

## Prerequisites
- .NET 10 SDK
- Visual Studio 2025 or VS Code

## Installation Steps
1. Download the client package
2. Extract to your projects directory
3. Run `dotnet restore`
4. Run `dotnet run`
5. Open browser to `https://localhost:7083`

## Configuration
Update `appsettings.json` with your API Gateway URL:
...
```

---

## OpenAPI Aggregation

### Aggregated Documentation

**Endpoint**: `GET /openapi/aggregated.json`

Returns consolidated OpenAPI specification from all backend services.

**Features:**
- Combines API documentation from Blueprint, Wallet, Register, Peer services
- Maintains service-specific tags for organization
- Includes all request/response schemas
- Preserves authentication requirements

**View with Scalar UI**: `https://localhost:7082/scalar/v1`

---

## Development

### Project Structure

```
Sorcha.ApiGateway/
├── Program.cs                      # Service entry point, YARP configuration
├── Services/
│   ├── HealthAggregationService.cs # Health checks from all services
│   ├── OpenApiAggregationService.cs # OpenAPI consolidation
│   └── ClientDownloadService.cs    # Blazor client packaging
├── Models/
│   ├── AggregatedHealthResponse.cs # Health response model
│   └── SystemStatistics.cs         # Statistics model
└── appsettings.json                # YARP routing configuration
```

### Running Tests

```bash
# Run gateway tests (when available)
dotnet test tests/Sorcha.ApiGateway.Tests

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Code Coverage

**Current Coverage**: ~60%
**Tests**: Pending (integration tests planned)
**Lines of Code**: ~800 LOC

---

## Landing Page

The API Gateway includes a beautiful, responsive landing page at `/`.

### Features

- **Real-time Health Status**: Live service health indicators
- **System Statistics**: Total services, healthy count, uptime
- **Quick Actions**: Download client, view docs, check health
- **Service List**: Visual status for each backend service
- **Gradient Design**: Modern purple gradient theme

### Screenshot

```
┌──────────────────────────────────────────────────┐
│      🚀 Sorcha API Gateway                       │
│      Unified API endpoint for Sorcha services    │
├──────────────────────────────────────────────────┤
│  Total Services: 4    Healthy: 3    Status: ✓   │
├──────────────────────────────────────────────────┤
│  Platform Statistics:                            │
│    📋 Blueprints: 42  (156 instances, 23 active)│
│    💰 Wallets: 89                                │
│    📚 Registers: 12  (3,421 transactions)       │
│    🏢 Tenants: 5                                │
│    🔗 Connected Peers: 8                        │
├──────────────────────────────────────────────────┤
│  Service Status:                                 │
│    ✓ blueprint   [healthy]                       │
│    ✓ wallet      [healthy]                       │
│    ✗ register    [unhealthy]                     │
│    ✓ peer        [healthy]                       │
├──────────────────────────────────────────────────┤
│  [💾 Download Client]  [📚 API Docs]            │
│  [🏥 Health Check]     [📊 System Stats]        │
└──────────────────────────────────────────────────┘
```

---

## Integration with Services

### Blueprint Service Integration

**Routes:**
- `/api/blueprint/**` → `http://blueprint-api/api/**`

**Use Cases:**
- Blueprint CRUD operations
- Action submission
- Template management
- Execution helpers

### Wallet Service Integration

**Routes:**
- `/api/wallets/**` → `http://wallet-service/api/v1/wallets/**`

**Use Cases:**
- Wallet creation and management
- Address derivation
- Transaction signing
- Key management

### Register Service Integration

**Routes:**
- `/api/registers/**` → `http://register-service/api/**` (future)

**Use Cases:**
- Transaction storage
- Docket retrieval
- OData queries
- Chain validation

### Peer Service Integration

**Routes:**
- `/api/peer/**` → `http://peer-service/api/**`

**Use Cases:**
- Peer discovery
- Transaction distribution
- Network health

### Validator Service Integration

**Routes:**
- `/api/validator/**` → `http://validator-service:8080/api/v1/**`
- `/api/v1/validators/**` → `http://validator-service:8080/api/v1/validators/**`

**Use Cases:**
- Transaction validation
- Consensus participation
- Mempool management
- Validator node registration

**gRPC Support:**
- gRPC endpoint available on port 8081 for inter-service communication
- REST API on port 8080 for external access

---

## Security Considerations

### Authentication (Production)

- **Current**: Development mode (no authentication required)
- **Production**: JWT bearer token authentication at gateway level
- **Token Validation**: Centralized at API Gateway
- **Service-to-Service**: Internal services trust gateway authentication

### Authorization

- **Role-Based Access**: Enforced at API Gateway
- **Tenant Isolation**: Multi-tenant filtering at gateway
- **Service Access**: Backend services trust gateway user context

### CORS Configuration

**Development:**
```csharp
policy.AllowAnyOrigin()
      .AllowAnyMethod()
      .AllowAnyHeader();
```

**Production:**
```csharp
policy.WithOrigins("https://app.sorcha.io", "https://sorcha.io")
      .AllowAnyMethod()
      .AllowAnyHeader()
      .AllowCredentials();
```

### Rate Limiting (Future)

```json
{
  "RateLimiting": {
    "Enabled": true,
    "PermitLimit": 100,
    "Window": "00:01:00"
  }
}
```

---

## Deployment

### .NET Aspire (Development)

The API Gateway is registered in the Aspire AppHost:

```csharp
var apiGateway = builder.AddProject<Projects.Sorcha_ApiGateway>("api-gateway")
    .WithReference(tenantService)
    .WithReference(blueprintService)
    .WithReference(walletService)
    .WithReference(registerService)
    .WithReference(peerService)
    .WithReference(redis)
    .WithExternalHttpEndpoints(); // Only the gateway is exposed externally
```

Start the entire platform:

```bash
dotnet run --project src/Apps/Sorcha.AppHost
```

Access Aspire Dashboard: `http://localhost:15888`

### Docker Compose (Recommended)

The API Gateway is part of the full Sorcha Docker Compose stack:

```bash
# Start all services (including API Gateway)
docker-compose up -d

# API Gateway will be accessible at:
# HTTP: http://localhost/api/...
# HTTPS: https://localhost/api/...
```

**Network Architecture:**
- Bridge network (`sorcha-network`) for all services
- Services communicate via Docker DNS (e.g., `http://wallet-service:8080`)
- Published ports: 80 (HTTP), 443 (HTTPS)
- See [docs/DOCKER-BRIDGE-NETWORKING.md](../../../docs/DOCKER-BRIDGE-NETWORKING.md) for complete details

### Docker (Standalone)

For standalone deployment:

```bash
# Build Docker image
docker build -t sorcha-api-gateway:latest -f src/Services/Sorcha.ApiGateway/Dockerfile .

# Run container
docker run -d \
  -p 443:8443 \
  -p 80:8080 \
  -e Services__Blueprint__Url="http://blueprint-service:8080" \
  -e Services__Wallet__Url="http://wallet-service:8080" \
  -e Services__Register__Url="http://register-service:8080" \
  -e Services__Peer__Url="http://peer-service:8080" \
  -e Services__Validator__Url="http://validator-service:8080" \
  --name api-gateway \
  sorcha-api-gateway:latest
```

### Azure Deployment

Deploy to Azure Container Apps with:
- **Azure Front Door**: Global load balancing
- **Azure Application Gateway**: Regional load balancing (alternative)
- **Azure Key Vault**: TLS certificates and secrets
- **Application Insights**: Observability and monitoring

**Azure Front Door Configuration:**
```bash
# Create Front Door
az afd profile create --profile-name sorcha-afd --resource-group sorcha-rg

# Create endpoint
az afd endpoint create --endpoint-name api --profile-name sorcha-afd \
  --resource-group sorcha-rg

# Create origin group
az afd origin-group create --origin-group-name backend-services \
  --profile-name sorcha-afd --resource-group sorcha-rg

# Add Container Apps as origin
az afd origin create --origin-name api-gateway --origin-group-name backend-services \
  --profile-name sorcha-afd --resource-group sorcha-rg \
  --host-name api-gateway.azurecontainerapps.io --priority 1
```

---

## Observability

### Logging (Serilog + Seq)

Structured logging with Serilog:

```csharp
Log.Information("Request routed to {Service}: {Path}", serviceName, path);
Log.Warning("Health check failed for service {Service}: {Error}", serviceName, error);
```

**Log Sinks**:
- Console (structured output via Serilog)
- OTLP → Aspire Dashboard (centralized log aggregation)

### Tracing (OpenTelemetry + Zipkin)

Distributed tracing with OpenTelemetry:

```bash
# View traces in Zipkin
open http://localhost:9411
```

**Traced Operations**:
- HTTP requests to gateway
- YARP proxy operations
- Backend service calls
- Health check aggregation

### Metrics (Prometheus)

Metrics exposed at `/metrics`:
- Request count per service
- Request latency per service
- Health check success rate
- Error rate by service
- Active connections

---

## Troubleshooting

### Common Issues

**Issue**: Backend service routing fails (404 Not Found)
**Solution**: Verify service URLs in `appsettings.json` and ensure backend services are running.

```bash
# Test service connectivity
curl http://blueprint-api/api/health
curl http://wallet-service/api/health
```

**Issue**: Health aggregation shows all services as unhealthy
**Solution**: Check service discovery configuration. Ensure Aspire service names match configuration.

**Issue**: CORS errors in browser console
**Solution**: Update CORS policy in `Program.cs` to allow your frontend origin.

```csharp
policy.WithOrigins("https://your-frontend.com")
      .AllowAnyMethod()
      .AllowAnyHeader()
      .AllowCredentials();
```

**Issue**: OpenAPI aggregation returns empty specification
**Solution**: Verify backend services expose OpenAPI at `/openapi/v1.json`.

### Debug Mode

Enable detailed logging:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Yarp": "Debug",
      "Sorcha.ApiGateway": "Trace"
    }
  }
}
```

---

## Contributing

### Development Workflow

1. **Create a feature branch**: `git checkout -b feature/your-feature`
2. **Make changes**: Follow C# coding conventions
3. **Update routing**: Add routes to `appsettings.json`
4. **Update health checks**: Add new services to `HealthAggregationService`
5. **Run tests**: `dotnet test`
6. **Format code**: `dotnet format`
7. **Commit**: `git commit -m "feat: your feature description"`
8. **Push**: `git push origin feature/your-feature`
9. **Create PR**: Reference issue number

---

## Resources

- **Architecture**: [docs/architecture.md](../../docs/architecture.md)
- **Development Status**: [docs/development-status.md](../../docs/development-status.md)
- **YARP Documentation**: https://microsoft.github.io/reverse-proxy/
- **.NET Aspire**: https://learn.microsoft.com/en-us/dotnet/aspire/

---

## Technology Stack

**Runtime:**
- .NET 10.0 (10.0.100)
- C# 13
- ASP.NET Core 10

**Frameworks:**
- YARP (Yet Another Reverse Proxy) for routing
- Minimal APIs for gateway endpoints
- .NET Aspire 13.0+ for orchestration

**Observability:**
- OpenTelemetry for distributed tracing
- Serilog for structured logging
- Prometheus metrics

**Testing:**
- xUnit for test framework
- FluentAssertions for assertions

---

## License

Apache License 2.0 - See [LICENSE](../../LICENSE) for details.

---

**Last Updated**: 2025-12-24
**Maintained By**: Sorcha Contributors
**Status**: ✅ Production Ready (95% Complete - Minor Enhancements Pending)
