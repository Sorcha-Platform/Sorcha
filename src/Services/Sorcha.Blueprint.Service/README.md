# Sorcha Blueprint Service

**Version**: 1.0.0
**Status**: Production Ready (100% Complete)
**Framework**: .NET 10.0
**Architecture**: Microservice

---

## Overview

The **Blueprint Service** is the workflow orchestration engine of the Sorcha platform, managing the complete lifecycle of multi-participant data flow blueprints from design to execution. It coordinates selective data disclosure, conditional routing, and cryptographic transaction signing through integration with the Wallet and Register services.

This service acts as the central hub for:
- **Blueprint lifecycle management** (create, publish, version, execute)
- **Action orchestration** for multi-party workflows
- **Real-time notifications** for workflow state changes
- **Transaction coordination** with cryptographic signing and blockchain storage

### Key Features

- **Blueprint Management**: Full CRUD operations for blueprint definitions with JSON Schema validation
- **Publishing & Versioning**: Publish blueprints to specific registers with immutable version tracking
- **Action Workflows**: Submit, retrieve, validate, and reject actions with state management
- **Portable Execution Engine**: Client-side and server-side execution of JSON Logic calculations, routing rules, and disclosure policies
- **Real-time Notifications**: SignalR hub (`/hubs/blueprint`) for live action status updates with Redis backplane
- **Wallet Integration**: Automatic transaction signing and payload encryption/decryption
- **Register Integration**: Blockchain transaction storage with distributed ledger guarantees
- **File Attachments**: Upload and download support for action-related documents
- **Template System**: JSON-e based blueprint templates with parameter substitution
- **Execution Helpers**: Validation, calculation, routing, and disclosure endpoints for client applications
- **AI Chat Designer**: SignalR-based conversational blueprint builder with 13 AI tools, standardised schema library (26 schemas), and Verified Credential support

### AI Chat Designer Tools

The AI blueprint designer uses these tools through the Anthropic API:

| Tool | Purpose |
|------|---------|
| `search_schemas` | Query the standardised schema library (26 schemas across 7 categories) |
| `use_standard_schema` | Apply a schema's fields + form layout to a blueprint action |
| `search_templates` | Query the blueprint template catalogue |
| `create_blueprint` | Create a new blueprint with title and description |
| `add_participant` | Add a participant to the workflow |
| `remove_participant` | Remove a participant |
| `add_action` | Add a workflow step with data fields |
| `update_action` | Modify an existing action |
| `set_disclosure` | Configure data visibility rules |
| `add_routing` | Add conditional routing logic |
| `require_credential` | Require a Verified Credential to perform an action |
| `issue_credential` | Issue a Verified Credential on action completion |
| `validate_blueprint` | Check blueprint validity |

---

## Architecture

### Components

```
Blueprint Service
├── Controllers/Endpoints
│   ├── Blueprints API (CRUD, publish, versions)
│   ├── Actions API (submit, retrieve, reject)
│   ├── Templates API (template management)
│   ├── Schemas API (schema browsing)
│   ├── Execution API (helpers)
│   └── Files API (attachments)
├── SignalR Hubs
│   ├── BlueprintHub (/hubs/blueprint — thin-signal notifications, F118)
│   └── ChatHub (/hubs/chat — AI designer streaming)
├── Execution Engine
│   ├── Sorcha.Blueprint.Engine (portable library)
│   ├── JSON Schema validator
│   ├── JSON Logic evaluator
│   └── Disclosure processor
├── Stores (pluggable backend; persistent in Production)
│   ├── IBlueprintStore / IPublishedBlueprintStore
│   ├── IActionStore (F113-audited — fails fast on in-memory in Prod/Staging)
│   └── IInstanceStore (ledger-derived projections, F145)
└── External Integrations
    ├── Wallet Service (signing, encryption)
    └── Register Service (transaction storage)
```

### Data Flow

```
Client → Blueprint API → [Create/Publish Blueprint]
      ↓
Client → Action API → [Submit Action]  ──►  202 Accepted (single async path)
      ↓
Execution Engine → [Validate, Calculate, Route, Disclose]
      ↓
Wallet Service → [Sign Transaction]
      ↓
Register Service → [Seal on Ledger]
      ↓
InstanceProjector (every node) → [Fold sealed docket → advance instance] → SignalR notify
```

> **Feature 145 — ledger-derived instances.** A workflow instance is a deterministic
> projection of the sealed register. Action submission always returns **`202 Accepted`**
> (`isAsync`, empty `nextActions`); the submitter never advances instance state. The
> single `InstanceProjector` folds each sealed action transaction on **every** node, so
> all nodes derive identical state, and it fires the `action-available` / `workflow-completed`
> notifications post-fold. There is no origin/mirror split and no synchronous-advance response.
> See `specs/145-ledger-derived-instances/contracts/submission-response.md`.

---

## Quick Start

### Prerequisites

- **.NET 10 SDK** or later
- **Docker Desktop** (for Redis)
- **Git**

### 1. Clone and Navigate

```bash
git clone https://github.com/Sorcha-Platform/Sorcha.git
cd Sorcha/src/Services/Sorcha.Blueprint.Service
```

### 2. Set Up Configuration

The service uses `appsettings.json` for configuration. For local development, defaults are pre-configured.

### 3. Start Dependencies

Start Redis for caching and SignalR backplane:

```bash
docker run -d -p 6379:6379 --name redis redis:latest
```

### 4. Run the Service

```bash
dotnet run
```

Service will start at:
- **HTTPS (Aspire)**: `https://localhost:7000`
- **HTTP (Docker)**: `http://localhost:5000`
- **Scalar API Docs**: `https://localhost:7000/scalar`
- **SignalR Hub**: `/hubs/blueprint` (reached via the gateway / service host)

---

## Configuration

### appsettings.json Structure

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  },
  "ServiceUrls": {
    "WalletService": "https://localhost:7084",
    "RegisterService": "https://localhost:7085"
  },
  "OpenTelemetry": {
    "ServiceName": "Sorcha.Blueprint.Service",
    "ZipkinEndpoint": "http://localhost:9411"
  }
}
```

### Environment Variables

For production deployment:

```bash
# Redis connection
CONNECTIONSTRINGS__REDIS="your-redis-connection-string"

# External service URLs
SERVICEURLS__WALLETSERVICE="https://wallet.sorcha.io"
SERVICEURLS__REGISTERSERVICE="https://register.sorcha.io"

# Observability
OPENTELEMETRY__ZIPKINENDPOINT="https://zipkin.yourcompany.com"
```

---

## API Endpoints

### Blueprint Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/blueprints/` | Get all blueprints (paginated) |
| GET | `/api/blueprints/{id}` | Get blueprint by ID |
| POST | `/api/blueprints/` | Create new blueprint |
| PUT | `/api/blueprints/{id}` | Update existing blueprint |
| DELETE | `/api/blueprints/{id}` | Delete blueprint (soft delete) |
| POST | `/api/blueprints/{id}/publish` | Publish blueprint to register |
| GET | `/api/blueprints/{id}/versions` | Get all published versions |
| GET | `/api/blueprints/{id}/versions/{version}` | Get specific version |

### Action Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/actions/{wallet}/{register}/blueprints` | Get available blueprints |
| GET | `/api/actions/{wallet}/{register}` | Get actions (paginated) |
| GET | `/api/actions/{wallet}/{register}/{tx}` | Get action details |
| POST | `/api/actions/` | Submit an action |
| POST | `/api/actions/reject` | Reject a pending action |

### Template System

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/templates/` | Get all published templates |
| GET | `/api/templates/{id}` | Get template by ID |
| POST | `/api/templates/` | Create or update template |
| DELETE | `/api/templates/{id}` | Delete template |
| POST | `/api/templates/evaluate` | Evaluate template with parameters |
| POST | `/api/templates/{id}/validate` | Validate template parameters |
| GET | `/api/templates/{id}/examples/{exampleName}` | Evaluate template example |

### Execution Helpers

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/execution/validate` | Validate action data against schema |
| POST | `/api/execution/calculate` | Apply JSON Logic calculations |
| POST | `/api/execution/route` | Determine routing destinations |
| POST | `/api/execution/disclose` | Apply disclosure rules |

### File Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/files/{wallet}/{register}/{tx}/{fileId}` | Download file attachment |

### Schema Browsing

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/schemas/` | Get available schemas |

### SignalR Hub

| Hub | Endpoint | Events |
|-----|----------|--------|
| BlueprintHub | `/hubs/blueprint` | Thin-signal (Feature 118): opaque IDs + timestamps only (e.g. action lifecycle, instance advanced) — fetch detail via REST; see `IBlueprintHubClient` |
| ChatHub | `/hubs/chat` | AI designer streaming (exempt from thin-signal) |

### Pending Action Notifications (Feature 062)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/actions/pending` | Get pending actions (paginated, urgency filter, blueprintId filter) |
| GET | `/api/actions/pending/count` | Get pending action count (for badge display) |

**NotificationConfig on Action Model:** The `Action` class in `Sorcha.Blueprint.Models` now includes a `NotificationConfig` property that defines per-action notification behavior (summary template, urgency rules, deadline).

**Utilities:**
- **SummaryTemplateRenderer** — Renders human-readable notification summaries from action metadata using configurable templates
- **UrgencyCalculator** — Computes notification urgency (Low/Medium/High/Critical) based on deadline proximity and action configuration

**EventsHubNotificationBridge Enhancements:** The bridge now enriches inbound action notifications with summary text, urgency level, and deadline information before delivery. It also persists `ActivityEvent` records for notification history tracking.

### Disclosed Prior-Action Data Query (Feature 176)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/workflows/{instanceId}/actions/{actionId}/disclosures` | Prior-action data disclosed to the calling participant, for the action being decided |
| GET | `/api/workflows/{instanceId}/disclosures` | Instance-wide form (anchored on the instance's current action) |

The read-side of the DAD disclosure model. Reconstructs each required prior action's caller-decryptable
view from the instance's sealed transactions (identical for encrypted and dev-mode registers) and clamps
it to exactly the caller participant's entitlement — no undisclosed field is ever returned. Authenticated;
the caller's wallet(s) are resolved from the `wallet_address` claim or the Wallet Service fallback (the
same path `/api/actions/pending` uses), and `X-Delegation-Token` (when supplied) unwraps disclosure-group
keys on encrypted registers. Returns `recipientResolved: false` with an empty view when the caller is not a
recipient. Backed by the shared `IActionDisclosureResolver` (also used by `ActionExecutionService`), so the
execution and query paths share one disclosure implementation. Consumed by the autonomous `Sorcha.Agent`
(it sets each pending action's previous payload from `disclosedFields` before running its checks, and holds
fail-closed when the fetch is unavailable) and by the MCP `sorcha_disclosed_data` participant tool.

### File Chunk Submission (Feature 085)

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/file-chunks` | Submit an encrypted file chunk (staged upload) |

**Purpose:** Supports large encrypted file attachments by accepting chunks individually before they are assembled and committed as part of a blueprint action transaction.

**Request Body:**
```json
{
  "uploadId": "string",
  "chunkIndex": 0,
  "totalChunks": 5,
  "encryptedData": "base64-encoded-chunk",
  "checksum": "sha256-hex"
}
```

**Response:** `202 Accepted` — chunk acknowledged and staged. Final assembly occurs when all `totalChunks` chunks are received.

**Rate Limiting:** `RateLimitPolicies.Strict` (wallet operations policy).

**Auth:** JWT Bearer required.

For full API documentation with request/response schemas, open **Scalar UI** at `https://localhost:7081/scalar`.

---

## Verified Citizen v2 Subsystems (Feature 103)

Three Blueprint Service components ship with Feature 103. They cooperate
to support open citizen-facing actions, schema reuse via JSON Schema
`$ref`, and credential issuance from nested primitive payloads.

### CoreSchemaSeedService

Hosted background service that seeds the Sorcha core identity primitives
(`PersonName/v1`, `DateOfBirth/v1`, `EmailAddress/v1`, `PostalAddress/v1`)
into the schema store on startup if they are not already present. Seed
documents live under
`src/Services/Sorcha.Blueprint.Service/CoreSchemas/v1/*.schema.json` and
are embedded resources, so the service runs without any external file
mount. Future versions add new files alongside existing ones — the
seeder is idempotent and never overwrites a stored schema.

### SchemaRefResolver

Publish-time JSON Schema `$ref` flattener. When `PublishService`
processes an action's `dataSchemas`, the resolver walks the schema tree
and inlines any `$ref` that points to a `https://schemas.sorcha.dev/core/*`
primitive, producing a single self-contained schema document for the
runtime. Inlining at publish time means the runtime never has to chase
external `$ref` URLs — `JsonSchema.Net` evaluation runs against a fully
materialised tree. The `BlueprintRefResolutionContext` tracks visited
URIs to defend against accidental cycles and missing primitives are
reported as publish-time validation errors with the same shape as
schema-validation errors.

### InstanceBindingCache

Redis-backed cache that pins the late-bound applicant wallet to an open
starting action's `Sender` participant. The first qualifying submitter
wins and is recorded for the lifetime of the instance; subsequent
submissions from a different wallet are rejected with the standard
`wallet not authorized` validation error. The cache uses a dedicated
`sorcha:binding:{instanceId}:{actionId}` keyspace with a 24-hour TTL
and emits OpenTelemetry metrics under `sorcha.binding_cache.*` for
hit-rate and read latency observability. A publish-time guardrail
(`VAL_BP_010` in the Validator Service) rejects blueprints where the
participant referenced by an open starting action has a non-null
`WalletAddress` — the foot-gun this whole feature exists to prevent.

### Late-binding claim mapping

`ActionExecutionService.BuildClaimsFromMappings` walks `ClaimMapping.SourceField`
JSON Pointer paths (with RFC 6901 `~1`/`~0` escape decoding) so a
credential with claims like `givenName`, `familyName`, `dateOfBirth` can
source from a nested submission payload like `{ "name": { "givenName": "..." } }`.
The same helper feeds both the internal Sorcha issuance path and the
HAIP external-wallet path, so the two stay in sync. Missing claim
sources are logged at Warning level — silently dropped claims would
otherwise produce credentials with fewer attributes than the action
promised.

---

## Development

### Project Structure

```
Sorcha.Blueprint.Service/
├── Program.cs                      # Service entry point, DI configuration
├── Endpoints/
│   ├── BlueprintEndpoints.cs       # Blueprint CRUD and publishing
│   ├── ActionEndpoints.cs          # Action workflows
│   ├── TemplateEndpoints.cs        # Template management
│   ├── ExecutionEndpoints.cs       # Execution helpers
│   └── FileEndpoints.cs            # File attachments
├── Hubs/
│   └── BlueprintHub.cs               # SignalR real-time notifications
├── Services/
│   ├── BlueprintService.cs         # Business logic
│   ├── ActionService.cs            # Action orchestration
│   ├── TemplateService.cs          # Template processing
│   └── ExecutionService.cs         # Execution engine integration
├── Repositories/
│   ├── IBlueprintRepository.cs     # Repository interfaces
│   ├── BlueprintRepository.cs      # In-memory implementation
│   ├── IActionRepository.cs
│   └── ActionRepository.cs
├── Models/
│   ├── Blueprint.cs                # Domain models
│   ├── Action.cs
│   └── Template.cs
└── appsettings.json                # Configuration

External Libraries:
├── Sorcha.Blueprint.Models/        # Shared models
├── Sorcha.Blueprint.Engine/        # Portable execution engine
└── Sorcha.Blueprint.Fluent/        # Fluent API (optional)
```

### Running Tests

```bash
# Run all Blueprint Service tests
dotnet test tests/Sorcha.Blueprint.Service.Tests

# Run with coverage
dotnet test tests/Sorcha.Blueprint.Service.Tests --collect:"XPlat Code Coverage"

# Watch mode (auto-rerun on changes)
dotnet watch test --project tests/Sorcha.Blueprint.Service.Tests
```

### Code Coverage

**Current Coverage**: ~85%
**Tests**: 37 integration tests
**Lines of Code**: ~1,600 LOC

```bash
# Generate coverage report
dotnet test tests/Sorcha.Blueprint.Service.Tests --collect:"XPlat Code Coverage"
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage -reporttypes:Html
```

Open `coverage/index.html` in your browser.

---

## Integration with Other Services

### Wallet Service Integration

The Blueprint Service integrates with the Wallet Service for:
- **Transaction Signing**: Automatically sign transactions before blockchain submission
- **Payload Encryption**: Encrypt sensitive action payloads
- **Payload Decryption**: Decrypt received action data

**Communication**: HTTP REST API
**Endpoints Used**: `/api/v1/wallets/{address}/sign`, `/api/v1/wallets/{address}/encrypt`, `/api/v1/wallets/{address}/decrypt`

### Register Service Integration

The Blueprint Service integrates with the Register Service for:
- **Transaction Storage**: Submit signed transactions to the blockchain
- **Transaction Retrieval**: Query transaction history
- **Blueprint Publishing**: Associate blueprints with specific registers

**Communication**: HTTP REST API
**Endpoints Used**: `/api/registers/{registerId}/transactions`

### SignalR Client Example

```typescript
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("https://localhost:7081/hubs/blueprint")
    .build();

connection.on("TransactionConfirmed", (transactionId, status) => {
    console.log(`Transaction ${transactionId} confirmed with status: ${status}`);
});

await connection.start();
```

---

## Security Considerations

### Authentication

- **Current**: Development mode (no authentication required)
- **Production**: JWT bearer token authentication required (issued by Tenant Service)

### Authorization

- Action submission requires proof of wallet ownership (signature verification)
- Blueprint publishing restricted to wallet owners
- File downloads restricted to action participants

### Data Protection

- Sensitive payloads encrypted using Wallet Service
- Selective disclosure enforced through disclosure rules
- Transaction signatures prevent tampering

### Secrets Management

- Wallet Service connection requires service principal credentials (stored in Azure Key Vault or environment variables)
- Redis connection string should use TLS in production

---

## Deployment

### .NET Aspire (Development)

The Blueprint Service is registered in the Aspire AppHost:

```csharp
var blueprintService = builder.AddProject<Projects.Sorcha_Blueprint_Service>("blueprint-service")
    .WithReference(redis);
```

Start the entire platform:

```bash
dotnet run --project src/Apps/Sorcha.AppHost
```

Access Aspire Dashboard: `http://localhost:15888`

### Docker

```bash
# Build Docker image
docker build -t sorcha-blueprint-service:latest -f src/Services/Sorcha.Blueprint.Service/Dockerfile .

# Run container
docker run -d \
  -p 7081:8080 \
  -e ConnectionStrings__Redis="redis:6379" \
  -e ServiceUrls__WalletService="http://wallet-service:8080" \
  -e ServiceUrls__RegisterService="http://register-service:8080" \
  --name blueprint-service \
  sorcha-blueprint-service:latest
```

### Azure Deployment

Deploy to Azure Container Apps with:
- **Redis Cache**: Azure Cache for Redis
- **Secrets**: Azure Key Vault for service credentials
- **Observability**: Application Insights integration

---

## Observability

### Logging (Serilog + Seq)

Structured logging with Serilog:

```csharp
Log.Information("Blueprint {BlueprintId} published to register {RegisterId}", blueprintId, registerId);
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
- HTTP requests
- Wallet Service calls
- Register Service calls
- SignalR connections

### Metrics (Prometheus)

Metrics exposed at `/metrics`:
- Request count and latency
- Action submission rate
- Blueprint publish rate
- SignalR connection count

---

## Troubleshooting

### Common Issues

**Issue**: SignalR hub connection fails
**Solution**: Ensure Redis is running and accessible. Check `ConnectionStrings:Redis` in appsettings.json.

```bash
# Test Redis connectivity
docker exec -it redis redis-cli ping
```

**Issue**: Wallet Service integration error
**Solution**: Verify Wallet Service is running and `ServiceUrls:WalletService` is correct.

```bash
# Test Wallet Service health
curl https://localhost:7084/api/health
```

**Issue**: Blueprint validation fails
**Solution**: Ensure blueprint JSON matches the JSON Schema definition. Use `/api/schemas/` to browse available schemas.

**Issue**: File upload fails
**Solution**: Check file size limits (default: 10 MB). Increase in `appsettings.json`:

```json
{
  "Kestrel": {
    "Limits": {
      "MaxRequestBodySize": 52428800
    }
  }
}
```

### Debug Mode

Enable detailed logging:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Sorcha.Blueprint.Service": "Trace"
    }
  }
}
```

---

## Contributing

### Development Workflow

1. **Create a feature branch**: `git checkout -b feature/your-feature`
2. **Make changes**: Follow C# coding conventions
3. **Write tests**: Maintain >85% coverage
4. **Run tests**: `dotnet test`
5. **Format code**: `dotnet format`
6. **Commit**: `git commit -m "feat: your feature description"`
7. **Push**: `git push origin feature/your-feature`
8. **Create PR**: Reference issue number

### Code Standards

- Follow [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use async/await for I/O operations
- Add XML documentation for public APIs
- Include unit tests for all business logic
- Use dependency injection for testability

---

## Resources

- **Specification**: [.specify/specs/](https://github.com/Sorcha-Platform/Sorcha/tree/master/.specify/specs)
- **API Reference**: [Scalar UI](https://localhost:7081/scalar)
- **Architecture**: [docs/architecture.md](../../../docs/reference/architecture.md)
- **Development Status**: [docs/development-status.md](../../../docs/reference/development-status.md)
- **Portable Engine**: [src/Core/Sorcha.Blueprint.Engine](../../Core/Sorcha.Blueprint.Engine/)
- **OpenAPI Spec**: `https://localhost:7081/openapi/v1.json`

---

## License

Apache License 2.0 - See [LICENSE](https://github.com/Sorcha-Platform/Sorcha/blob/master/LICENSE) for details.

---

**Last Updated**: 2025-11-23
**Maintained By**: Sorcha Contributors
**Status**: ✅ Production Ready (100% Complete)
