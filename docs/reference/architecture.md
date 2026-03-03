# Sorcha Architecture

## Overview

Sorcha is a modern .NET 10 platform for defining, designing, and executing multi-participant data flow orchestration workflows (called "Blueprints"). Built on .NET Aspire for cloud-native orchestration, Sorcha provides a flexible and scalable solution for workflow automation with selective data disclosure and conditional routing.

**Last Updated:** 2025-11-16
**Version:** 2.3.0
**Status:** Active Development
**Recent Changes:** Added Validator.Service for secured docket validation and chain integrity

## High-Level Architecture

```
┌───────────────────────────────────────────────────────────────────────────────┐
│                            Sorcha Platform                                     │
├───────────────────────────────────────────────────────────────────────────────┤
│                                                                                 │
│  ┌────────────────────────┐              ┌──────────────────────────┐         │
│  │  Blueprint Designer    │              │  Blueprint Engine        │         │
│  │  (Blazor Server)       │─────────────▶│  (REST API)              │         │
│  │  + Designer.Client     │   HTTP       │  Minimal APIs            │         │
│  │  (Blazor WASM)         │              │  (In Development)        │         │
│  └────────────────────────┘              └──────────────────────────┘         │
│           │                                         │                          │
│           │              ┌──────────────────────────┼─────────────────┐       │
│           │              │                          │                  │       │
│           ▼              ▼                          ▼                  ▼       │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │                   Common Libraries Layer                                 │ │
│  ├─────────────────────────────────────────────────────────────────────────┤ │
│  │  • Blueprint.Models (Data Models)                                       │ │
│  │  • Blueprint.Fluent (Fluent API Builders)                              │ │
│  │  • Blueprint.Schemas (Schema Management & Caching)                     │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│                              │                                                 │
│           ┌──────────────────┼──────────────────┐                            │
│           │                  │                  │                            │
│           ▼                  ▼                  ▼                            │
│  ┌────────────────┐  ┌────────────────┐  ┌────────────────────────────┐    │
│  │  Peer Service  │  │  Hosting Layer │  │  Blueprint Services        │    │
│  │  (P2P Network) │  │  - AppHost     │  │  - Engine                  │    │
│  │  - Discovery   │  │  - Service     │  │  - Designer                │    │
│  │  - Distribution│  │    Defaults    │  └────────────────────────────┘    │
│  │  - Gossip      │  └────────────────┘                                     │
│  │  (Planned)     │                                                          │
│  └────────────────┘                                                          │
│           │                                                                   │
│           └──────────────────────┐                                          │
│                                  │                                          │
└──────────────────────────────────┼──────────────────────────────────────────┘
                                   │
         ┌─────────────────────────┼─────────────────────────┐
         │                         │                         │
         ▼                         ▼                         ▼
┌─────────────────┐    ┌─────────────────┐    ┌───────────────────────┐
│  Storage        │    │  External       │    │  Peer Network         │
│  (Planned:      │    │  Schema Sources │    │  (Other Sorcha Nodes) │
│   EF Core)      │    │  (SchemaStore)  │    │  - gRPC/REST          │
└─────────────────┘    └─────────────────┘    │  - Transaction Sync   │
                                               └───────────────────────┘
```

## Solution Structure

Sorcha follows a clean 4-layer architecture pattern for maximum maintainability:

```
Sorcha/
├── src/
│   ├── Apps/                            # Application layer
│   │   ├── Sorcha.AppHost              # .NET Aspire orchestration host
│   │   └── UI/
│   │       └── Sorcha.Blueprint.Designer.Client  # Blazor WASM UI
│   ├── Common/                          # Cross-cutting concerns
│   │   ├── Sorcha.Blueprint.Models     # Domain models & contracts
│   │   ├── Sorcha.Cryptography         # Cryptographic operations
│   │   ├── Sorcha.Validator.Core       # Enclave-safe validation library
│   │   └── Sorcha.ServiceDefaults      # Shared service configurations
│   ├── Core/                            # Business logic layer
│   │   ├── Sorcha.Blueprint.Engine     # Blueprint execution engine
│   │   ├── Sorcha.Blueprint.Fluent     # Fluent API builders
│   │   └── Sorcha.Blueprint.Schemas    # Schema management
│   └── Services/                        # Service layer
│       ├── Sorcha.ApiGateway           # YARP API Gateway
│       ├── Sorcha.Blueprint.Service    # Blueprint REST API
│       ├── Sorcha.Peer.Service         # P2P networking service
│       ├── Sorcha.Register.Service     # Register/blockchain storage service
│       └── Sorcha.Validator.Service    # Blockchain validation service (SECURED)
├── tests/                               # Test projects
│   ├── Sorcha.Blueprint.Models.Tests
│   ├── Sorcha.Blueprint.Fluent.Tests
│   ├── Sorcha.Blueprint.Schemas.Tests
│   ├── Sorcha.Blueprint.Engine.Tests
│   ├── Sorcha.Cryptography.Tests
│   ├── Sorcha.Peer.Service.Tests
│   ├── Sorcha.Validator.Core.Tests
│   ├── Sorcha.Validator.Service.Tests
│   ├── Sorcha.Integration.Tests
│   ├── Sorcha.Gateway.Integration.Tests
│   ├── Sorcha.UI.E2E.Tests
│   └── Sorcha.Performance.Tests
└── docs/                                # Documentation
```

For detailed information about the directory structure, see [Project Structure](project-structure.md).

## Core Components

### 1. Common Layer

#### Sorcha.Blueprint.Models
Core domain models representing the Blueprint data structure.

**Key Models:**
- `Blueprint` - Root workflow definition (title, description, version, participants, actions)
- `Action` - Workflow step (data schemas, routing, disclosures, calculations, form)
- `Participant` - Workflow party (ID, name, organization, wallet, DID)
- `Disclosure` - Data visibility rule (participant, JSON Pointers)
- `Condition` - Routing logic (participant, JSON Logic)
- `Control` - UI form definition (hierarchical layout)
- `Calculation` - Computed field (JSON Logic expressions)

**Features:**
- JSON serialization/deserialization
- Data validation with `DataAnnotations`
- JSON Schema generation support
- Equality comparison (`IEquatable<T>`)
- Audit timestamps (createdAt, updatedAt)

**Technology:**
- System.Text.Json for serialization
- DataAnnotations for validation
- JsonSchema.Net.Generation for schema generation

### 2. Core Layer

#### Sorcha.Blueprint.Fluent
Fluent API for programmatically building blueprints with compile-time safety.

**Key Builders:**
- `BlueprintBuilder` - Main workflow builder
- `ParticipantBuilder` - Participant configuration
- `ActionBuilder` - Action/step configuration
- `DisclosureBuilder` - Data visibility rules
- `ConditionBuilder` - Routing conditions
- `CalculationBuilder` - Computed fields
- `FormBuilder` / `ControlBuilder` - UI form layout
- `DataSchemaBuilder` - JSON Schema definitions

**Example Usage:**
```csharp
var blueprint = BlueprintBuilder.Create()
    .WithTitle("Purchase Order")
    .WithDescription("Two-party purchase workflow")
    .AddParticipant("buyer", p => p
        .Named("Buyer Organization")
        .FromOrganisation("ORG-123"))
    .AddParticipant("seller", p => p
        .Named("Seller Organization"))
    .AddAction(0, a => a
        .WithTitle("Submit Order")
        .SentBy("buyer")
        .RequiresData(d => d
            .WithTitle("Order Details")
            .AddProperty("itemName", "string")
            .AddProperty("quantity", "integer"))
        .Disclose("seller", d => d.Field("/itemName").Field("/quantity"))
        .RouteToNext("seller"))
    .Build();
```

**Features:**
- Type-safe blueprint construction
- Validation at build time
- Fluent method chaining
- Participant reference validation
- Draft mode for incomplete blueprints

**Technology:**
- Modern C# patterns (method chaining, delegates)
- Strong typing with generics
- Integrated validation

#### Sorcha.Blueprint.Schemas
Schema management with pluggable repositories and client-side caching.

**Key Components:**
- `SchemaLibraryService` - Unified schema access across repositories
- `ISchemaRepository` - Repository interface for schema sources
- `BuiltInSchemaRepository` - Embedded schemas (person, address, document, payment)
- `SchemaStoreRepository` - External schema.org integration
- `ISchemaCacheService` - Caching interface
- `LocalStorageSchemaCacheService` - Browser LocalStorage cache

**Built-in Schemas:**
- `person.json` - Contact information, dates, social media
- `address.json` - Physical location data
- `document.json` - Document metadata
- `payment.json` - Payment information

**Features:**
- Multi-source schema aggregation
- Search and filtering (by category, source, keywords)
- Favorites management
- Usage tracking
- Client-side caching with statistics
- Extensible repository pattern

**Technology:**
- JsonSchema.Net for validation (Draft 2020-12)
- Blazored.LocalStorage for caching
- Async/await patterns throughout

#### Sorcha.Blueprint.Engine
**⭐ NEW: Portable execution engine library that runs client-side and server-side**

**Current Status:** Ready for implementation (design approved)

**Architecture:** Standalone class library (`net10.0`) with zero ASP.NET dependencies

**Responsibilities:**
- **Schema Validation** - JSON Schema Draft 2020-12 validation
- **JSON Logic Evaluation** - Calculations and conditional routing
- **Selective Disclosure** - Privacy-preserving data filtering using JSON Pointers
- **Routing Determination** - Next participant resolution based on conditions
- **Action Processing** - Complete action execution orchestration

**Key Interfaces:**
- `IExecutionEngine` - Main stateless execution engine
- `IActionProcessor` - Action validation and processing
- `ISchemaValidator` - JSON Schema validation
- `IJsonLogicEvaluator` - JSON Logic expression evaluation
- `IDisclosureProcessor` - Selective disclosure processing
- `IRoutingEngine` - Routing logic

**Key Features:**
- **Stateless** - No internal state, all context passed as parameters
- **Portable** - Runs in Blazor WASM (client-side) and ASP.NET Core (server-side)
- **Pure Functions** - Deterministic results for same inputs
- **Async Throughout** - Non-blocking operations
- **Zero External Dependencies** - Only JSON processing libraries
- **Highly Testable** - Easy to unit test in isolation

**Technology:**
- .NET 10 Class Library (net10.0)
- JsonSchema.Net 7.2.4 for validation
- JsonLogic.Net 2.0.0 for expression evaluation
- JsonPath.Net 1.1.3 for JSON Pointers

### 3. Apps Layer

#### Sorcha.Blueprint.Designer (Blazor Server)
Web-based application host for the Blueprint designer.

**Responsibilities:**
- Host Blazor Server application
- HTTP client for Engine API
- Service discovery integration
- Health checks

**Features:**
- Interactive server-side rendering
- SignalR for real-time updates
- Output caching
- Anti-forgery protection

**Technology:**
- Blazor Server
- MudBlazor components
- SignalR

#### Sorcha.Blueprint.Designer.Client (Blazor WASM)
Client-side Blazor components and logic.

**Responsibilities:**
- Visual blueprint editor UI
- Blueprint validation
- Schema browser
- Form designer
- Client-side state management

**Features:**
- Visual workflow designer (Z.Blazor.Diagrams)
- MudBlazor component library
- LocalStorage caching for schemas
- JSON Schema validation

**Technology:**
- Blazor WebAssembly
- MudBlazor 8.0.0
- Z.Blazor.Diagrams 3.0.3
- Blazored.LocalStorage 4.5.0

### 4. Apps Layer

#### Sorcha.AppHost
.NET Aspire orchestration host managing service lifecycle.

**Location:** `src/Apps/Sorcha.AppHost/`

**Responsibilities:**
- Service orchestration and discovery
- Configuration management
- Resource allocation
- Developer dashboard
- Health check aggregation

**Configuration:**
```csharp
var blueprintService = builder.AddProject<Projects.Sorcha_Blueprint_Service>("blueprint-service")
    .WithHttpHealthCheck("/health");

var apiGateway = builder.AddProject<Projects.Sorcha_ApiGateway>("api-gateway")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(blueprintService)
    .WaitFor(blueprintService);

var peerService = builder.AddProject<Projects.Sorcha_Peer_Service>("peer-service")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.Sorcha_Blueprint_Designer_Client>("blueprint-designer")
    .WithExternalHttpEndpoints()
    .WithReference(apiGateway);
```

**Technology:**
- .NET Aspire 13.0.0
- Service discovery
- Health checks

#### Sorcha.ServiceDefaults
Shared service configurations and cross-cutting concerns.

**Location:** `src/Common/Sorcha.ServiceDefaults/`

**Extension Methods:**
- `AddServiceDefaults<TBuilder>()` - Complete service setup
- `ConfigureOpenTelemetry<TBuilder>()` - Observability setup
- `AddDefaultHealthChecks<TBuilder>()` - Health/liveness checks
- `MapDefaultEndpoints(WebApplication)` - Health endpoints

**Features:**
- OpenTelemetry integration (logs, metrics, traces)
- Health check endpoints (`/health`, `/alive`)
- Service discovery configuration
- Resilience patterns (retry, circuit breaker, timeout)
- HTTP client defaults with resilience

**Technology:**
- OpenTelemetry 1.12.0
- Microsoft.Extensions.Http.Resilience 9.9.0
- Microsoft.Extensions.ServiceDiscovery 13.0.0

### 5. Services Layer

#### Sorcha.ApiGateway
YARP-based API Gateway for routing and aggregation.

**Location:** `src/Services/Sorcha.ApiGateway/`

**Responsibilities:**
- API routing and reverse proxy
- Health check aggregation across services
- OpenAPI document aggregation
- Client download service
- Load balancing and failover

**Features:**
- YARP reverse proxy configuration
- Dynamic route configuration
- Health check monitoring and reporting
- OpenAPI document aggregation from downstream services
- Scalar API documentation UI

**Technology:**
- YARP 2.2.0
- ASP.NET Core 10.0
- Scalar.AspNetCore 2.10.0

#### Sorcha.Blueprint.Service
**⭐ UPDATED: Unified Blueprint & Action Service**

**Location:** `src/Services/Sorcha.Blueprint.Service/`

**Responsibilities:**
- **Blueprint Management** - CRUD operations, publishing, versioning
- **Action Operations** - Action retrieval, submission, rejection
- **Execution Coordination** - Uses `Sorcha.Blueprint.Engine` for processing
- **Payload Management** - Encryption/decryption via Wallet Service
- **Transaction Building** - Coordinates with Register Service
- **Real-Time Notifications** - SignalR hub for action updates
- **File Handling** - Upload and download file attachments

**API Endpoints:**

*Blueprint Management:*
- `GET/POST/PUT/DELETE /api/blueprints` - Blueprint CRUD
- `POST /api/blueprints/{id}/publish` - Publish blueprint
- `GET /api/blueprints/{id}/versions` - Version history
- `POST /api/blueprints/validate` - Validate blueprint

*Action Operations:*
- `GET /api/actions/{wallet}/{register}/blueprints` - Get starting actions
- `GET /api/actions/{wallet}/{register}` - Get pending actions
- `GET /api/actions/{wallet}/{register}/{tx}` - Get action details
- `POST /api/actions` - Submit action
- `POST /api/actions/reject` - Reject action

*Execution Helpers (for client-side validation):*
- `POST /api/execution/validate` - Validate action data
- `POST /api/execution/calculate` - Apply calculations
- `POST /api/execution/route` - Determine routing
- `POST /api/execution/disclose` - Apply disclosure rules

*File Operations:*
- `GET /api/files/{wallet}/{register}/{tx}/{fileId}` - Download file

*SignalR Hub:*
- `/actionshub` - Real-time notifications (ActionAvailable, ActionConfirmed, ActionRejected)

**Features:**
- RESTful API with Minimal APIs pattern
- .NET 10 built-in OpenAPI documentation with Scalar UI
- SignalR for real-time notifications (Redis backplane for scale-out)
- JSON-LD middleware for semantic web support
- Output caching with Redis
- FluentValidation for request validation
- Integration with Wallet Service (encryption/decryption)
- Integration with Register Service (transaction submission)
- JWT Bearer authentication
- Rate limiting and audit logging

**Technology:**
- ASP.NET Core 10.0 with Minimal APIs
- Microsoft.AspNetCore.SignalR 1.0.0
- FluentValidation.AspNetCore 11.3.0
- Aspire.StackExchange.Redis 13.0.0 (caching + SignalR backplane)
- Scalar.AspNetCore 2.10.0
- References: Sorcha.Blueprint.Engine, Sorcha.Cryptography, Sorcha.TransactionHandler

#### Sorcha.Peer.Service
Peer-to-peer networking service for decentralized transaction distribution.

**Location:** `src/Services/Sorcha.Peer.Service/`

**Status:** Active Development - See [Peer Service Design](peer-service-design.md) and [Implementation Plan](peer-service-implementation-plan.md)

**Purpose:**
Enable decentralized, peer-to-peer communication and transaction distribution across a network of Sorcha nodes without reliance on centralized infrastructure.

**Key Components:**
- **Peer Discovery Service** - Bootstrap connection, recursive discovery, health monitoring
- **Communication Manager** - Protocol negotiation (gRPC Stream → gRPC → REST), connection pooling
- **Transaction Distributor** - Gossip protocol for efficient distribution, bandwidth optimization
- **Network Address Discovery** - STUN/TURN for NAT traversal, external address detection
- **Offline/Online Mode** - Transaction queuing, automatic flush when connectivity restored

**Features:**
- Multi-protocol support (gRPC streaming preferred, REST fallback)
- Gossip-based transaction distribution (O(log N) complexity)
- Automatic peer discovery and health monitoring
- NAT traversal with STUN/TURN
- Offline transaction queuing
- Bloom filter for duplicate detection
- Streaming support for large transactions
- Bandwidth optimization with compression
- Real-time metrics and monitoring dashboard

**Communication Flow:**
```
New Transaction → Local Queue → Gossip Protocol → Select Peers (fanout) →
Notify Peers (hash only) → Peers Request Full Transaction (if unknown) →
Peers Repeat Gossip → 90% Network Coverage in < 1 minute
```

**Planned Configuration:**
```json
{
  "PeerService": {
    "Enabled": true,
    "BootstrapNodes": ["https://peer.sorcha.dev:5001"],
    "RefreshIntervalMinutes": 15,
    "GossipProtocol": {
      "FanoutFactor": 3,
      "GossipRounds": 3
    }
  }
}
```

**Technology:**
- gRPC with bidirectional streaming
- Protocol Buffers for efficient serialization
- SQLite for peer list persistence
- STUN protocol for NAT traversal
- Bloom filter for duplicate detection
- Circuit breaker pattern for resilience

**Implementation Timeline:** 20 weeks (10 sprints) - See [Implementation Plan](peer-service-implementation-plan.md)

**Benefits:**
- Decentralization - No single point of failure
- Scalability - Distributed load across all peers
- Resilience - Automatic failover and recovery
- Bandwidth Efficiency - Only transmit to subset of peers
- Privacy - Direct peer-to-peer communication

**Use Cases:**
- Distribute new blueprint transactions across the network
- Synchronize transaction history between nodes
- Enable offline-first operation with automatic sync
- Build decentralized blueprint registry
- Support multi-region deployment without centralized coordination

#### Sorcha.Validator.Service
**🔒 SECURED SERVICE** - Blockchain validation and consensus service

**Location:** `src/Services/Sorcha.Validator.Service/`

**Status:** Active Development - See [Validator Service Design](validator-service-design.md)

**Purpose:**
Provide blockchain consensus and validation in a secured environment with access to encryption keys for cryptographic operations (SHA256 hashing, chain integrity validation).

**Key Components:**
- **DocketManager** - Manages docket operations (block creation, sealing, chain integrity)
  - Creates new dockets from pending transactions
  - Proposes dockets for consensus
  - Seals dockets after approval
  - Calculates and verifies SHA256 docket hashes
  - **Security**: Requires secured environment for cryptographic operations

- **ChainValidator** - Validates blockchain chain integrity for registers
  - Validates entire docket chains
  - Verifies docket hash integrity
  - Checks sequential ID linking
  - Validates previous hash linkage
  - Confirms register height consistency
  - **Security**: Critical security component requiring isolated execution

**Security Architecture:**
- Runs in secured environment with access to encryption keys
- Performs cryptographic operations (SHA256) for docket hashing
- Validates chain integrity to prevent tampering
- Integrates with Wallet Service for signature verification
- Supports enclave execution (Intel SGX/AMD SEV) for production environments

**Integration:**
- **Register Service**: Storage and retrieval of dockets and transactions
- **Event Publisher**: Publishes docket confirmation and register update events
- **Wallet Service** (planned): Signature verification and key management
- **Peer Service** (planned): Docket broadcasting and consensus coordination

**Features:**
- SHA256-based docket hashing
- Chain integrity validation (PreviousHash linkage)
- Transaction-to-docket association
- Register height management
- Event-driven architecture
- Async/await patterns throughout

**Technology:**
- ASP.NET Core 10.0 with Minimal APIs
- System.Security.Cryptography for SHA256 hashing
- Aspire.StackExchange.Redis for distributed state
- Event-driven with IEventPublisher

**Related Documentation:**
- [Validator Service Design](validator-service-design.md)
- [Validator Service Implementation Plan](validator-service-implementation-plan.md)
- [Validator Service Quick Reference](VALIDATOR-SERVICE-QUICK-REFERENCE.md)

**Architectural Note (Updated 2025-11-16):**
DocketManager and ChainValidator were permanently moved from `Sorcha.Register.Core` to `Sorcha.Validator.Service` to ensure they run in a secured environment with proper access to encryption keys and cryptographic operations.

This architectural decision is now permanently captured in:
- Functional Requirements: Components requiring cryptographic operations must run in secured environments
- Non-Functional Requirements: Security isolation for cryptographic validation components
- Component Placement Guidelines: See LEARNINGS.md for decision criteria

The separation ensures:
1. **Security Isolation**: Cryptographic operations (SHA256 hashing, chain validation) run in secured environment with key access
2. **Separation of Concerns**: Storage (Register.Service) separate from validation/consensus (Validator.Service)
3. **Enclave Support**: Pure validation logic can be deployed in Intel SGX/AMD SEV enclaves
4. **Zero-Trust Architecture**: Security-sensitive components have explicit trust boundaries
5. **Compliance**: Meets regulatory requirements for cryptographic operation isolation

## Testing Architecture

Sorcha follows a comprehensive testing strategy with multiple levels of testing:

### Test Projects Structure

```
tests/
├── Sorcha.Blueprint.Models.Tests      # Unit tests for domain models
├── Sorcha.Blueprint.Fluent.Tests      # Unit tests for fluent builders
├── Sorcha.Blueprint.Schemas.Tests     # Unit tests for schema management
├── Sorcha.Blueprint.Engine.Tests      # Unit tests for API endpoints
├── Sorcha.Blueprint.Designer.Tests    # Component tests for Blazor UI
└── Sorcha.Integration.Tests           # End-to-end integration tests
```

### Testing Layers

**1. Unit Tests**
- Test individual classes and methods in isolation
- Mock external dependencies
- Fast execution (< 100ms per test)
- High code coverage target (80%+)
- Technologies: xUnit, Moq, FluentAssertions

**2. Component Tests**
- Test Blazor components in isolation
- Verify UI rendering and interactions
- Test client-side logic
- Technologies: bUnit, xUnit

**3. Integration Tests**
- Test service-to-service communication
- Test Aspire orchestration
- Test database operations (when implemented)
- Test API endpoints end-to-end
- Technologies: xUnit, WebApplicationFactory, Testcontainers

**4. Contract Tests**
- Verify API contracts match OpenAPI specs
- Ensure backward compatibility
- Validate request/response schemas

### Test Naming Convention

```csharp
// Pattern: MethodName_Scenario_ExpectedBehavior
[Fact]
public void Build_WithoutTitle_ThrowsInvalidOperationException() { }

[Fact]
public void Build_WithValidData_ReturnsBlueprint() { }

[Theory]
[InlineData("", "short")]
[InlineData("a", "short")]
public void WithTitle_TooShort_ThrowsException(string title, string reason) { }
```

### CI/CD Testing Phases

```
Build Pipeline:
1. Restore dependencies
2. Build solution
3. Run unit tests (parallel)
4. Run component tests
5. Run integration tests
6. Collect code coverage
7. Generate coverage reports
8. Upload to Codecov
```

## Design Principles

### 1. Cloud-Native

- Containerized deployments
- Horizontal scalability
- Service discovery
- Resilience patterns (retry, circuit breaker, timeout)
- Configuration as code
- Stateless service design

### 2. Observability-First

- Distributed tracing with OpenTelemetry
- Structured logging with correlation IDs
- Health checks (`/health`, `/alive`)
- Metrics collection (request rate, duration, errors)
- Real-time monitoring via Aspire dashboard

### 3. API-First

- RESTful APIs using minimal API pattern
- **OpenAPI documentation REQUIRED for all REST endpoints**
- Use .NET 10's built-in OpenAPI support (Microsoft.AspNetCore.OpenApi)
- Auto-generated OpenAPI specifications from code annotations
- Interactive API documentation via Scalar.AspNetCore (NOT Swagger)
- OpenAPI spec available at `/openapi/v1.json`
- Interactive UI available at `/scalar/v1`
- Versioned APIs (planned)
- Standard HTTP status codes
- JSON request/response
- Consistent error responses (ProblemDetails)
- All endpoints must document authentication/authorization requirements

### 4. Modular Architecture

- Loose coupling between services
- Clear separation of concerns (Common/Core/Apps)
- Extensible plugin system (planned)
- Shared abstractions via interfaces
- Dependency injection throughout

### 5. Domain-Driven Design

- Rich domain models with behavior
- Ubiquitous language (Blueprint, Action, Participant, Disclosure)
- Value objects for immutable data
- Fluent builders for complex construction
- Validation at domain boundaries

### 6. Security by Design

- Input validation at all entry points
- JSON Schema validation for blueprints
- DataAnnotations for model validation
- Planned: Authentication and authorization
- Planned: Encryption at rest and in transit
- Audit logging for compliance

## Data Flow

### Blueprint Execution Flow

```
1. User creates/edits blueprint in Designer
2. Designer validates blueprint schema
3. User triggers execution via API
4. Engine validates blueprint
5. Engine creates execution context
6. Engine executes actions in order
7. Engine reports status updates
8. Designer displays real-time progress
9. Engine completes and returns results
```

### Monitoring Flow

```
1. Service emits telemetry
2. OpenTelemetry collector receives data
3. Data exported to monitoring backend
4. Dashboards visualize metrics/traces
5. Alerts triggered on anomalies
```

## Technology Stack

### Runtime
- .NET 10 (10.0.100)
- C# 13
- ASP.NET Core

### Frameworks
- .NET Aspire for orchestration
- Minimal APIs for REST endpoints
- Blazor Server for UI
- Entity Framework Core (planned)

### Observability
- OpenTelemetry
- Application Insights (optional)
- Prometheus (optional)
- Grafana (optional)

### Storage
- SQL database for metadata (planned)
- Blob storage for blueprints (planned)
- Cache for performance (planned)

### Message Queue
- Azure Service Bus (optional)
- RabbitMQ (optional)
- For async processing and event-driven patterns

## Deployment Models

### Development
- Local development with Aspire
- In-process services
- File-based storage

### Production - Single Instance
- Docker containers
- Reverse proxy (nginx/Kestrel)
- External database
- Centralized logging

### Production - Distributed
- Kubernetes cluster
- Service mesh (optional)
- Managed databases
- External monitoring
- Load balancing
- Auto-scaling

## Security Architecture

### Authentication & Authorization
- JWT token-based auth (planned)
- Role-based access control (planned)
- API key support (planned)

### Data Protection
- Encryption at rest (planned)
- Encryption in transit (TLS)
- Secrets management
- Audit logging (planned)

### Network Security
- HTTPS enforcement
- CORS configuration
- Rate limiting (planned)
- IP filtering (optional)

## Scalability Considerations

### Horizontal Scaling
- Stateless service design
- External state management
- Load balancing
- Service discovery

### Performance
- Async/await throughout
- Connection pooling
- Response caching
- Background processing

### Reliability
- Health checks
- Graceful degradation
- Circuit breakers
- Retry policies
- Bulkhead isolation

## Future Architecture Plans

- [ ] Distributed execution across multiple nodes
- [ ] Plugin system for custom actions
- [ ] Multi-tenancy support
- [ ] Advanced scheduling and cron support
- [ ] Workflow versioning and rollback
- [ ] GraphQL API (in addition to REST)
- [ ] Real-time collaboration in Designer
- [ ] Blueprint marketplace

## Related Documentation

- [Execution Model](execution-model.md)
- [Blueprint Schema](blueprint-schema.md)
- [API Reference](api-reference.md)
- [Deployment Guide](deployment.md)
