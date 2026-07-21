# Sorcha.Action.Service Specification

**Version:** 1.0
**Date:** 2025-11-15
**Status:** Proposed
**Related Constitution:** [constitution.md](../constitution.md)
**Related Specifications:**
- [sorcha-wallet-service.md](sorcha-wallet-service.md)
- [sorcha-register-service.md](sorcha-register-service.md)
- [sorcha-transaction-handler.md](sorcha-transaction-handler.md)

## Executive Summary

This specification defines the requirements for creating the **Sorcha.Action.Service**, a microservice responsible for managing participant interactions within Blueprint-controlled workflows. The Action Service retrieves available actions for participants, processes action submissions, validates data against schemas, encrypts payloads for selective disclosure, and coordinates transaction distribution across the network.

## Background

### Current State Analysis

The Sorcha platform currently has:
- ✅ Blueprint definition and modeling (Sorcha.Blueprint.Models)
- ✅ Blueprint fluent API (Sorcha.Blueprint.Fluent)
- ✅ Blueprint schema management (Sorcha.Blueprint.Schemas)
- ✅ Blueprint service for CRUD operations (Sorcha.Blueprint.Service)
- ✅ Cryptography library (Sorcha.Cryptography)
- ✅ Transaction handling library (Sorcha.TransactionHandler)
- ⏳ Wallet Service (in development)
- ⏳ Register Service (in development)
- ❌ **Action Service (not yet implemented)**

The Action Service is a critical component that bridges Blueprint definitions, participant wallets, and the transaction register to enable workflow execution.

### Legacy Reference

This specification is informed by the Action Service implementation in the legacy platform, which provided:
- Action retrieval for participants
- Action submission with schema validation
- Payload encryption for selective disclosure
- Transaction construction and signing
- File attachment handling
- Sustainability metric calculation
- Real-time notifications via SignalR

### Goals

1. **Workflow Execution** - Enable participants to execute Blueprint workflows
2. **Selective Disclosure** - Implement privacy-preserving data sharing
3. **Schema Validation** - Ensure data integrity and compliance
4. **Transaction Coordination** - Build and submit valid transactions
5. **Real-Time Notifications** - Provide immediate feedback on actions
6. **Cloud-Native Design** - Follow Sorcha architectural principles
7. **Comprehensive Testing** - >80% coverage with integration tests

## Scope

### In Scope

#### Core Action Management

1. **Action Retrieval**
   - Get starting actions for new Blueprint instances
   - Get pending actions awaiting participant attention
   - Get action details with full context and history
   - Filter actions by Blueprint, participant, status
   - Aggregate previous transaction data for context
   - Support pagination and search

2. **Action Submission**
   - Validate submission data against Blueprint schema
   - Apply JSON Schema validation (Draft 2020-12)
   - Execute Blueprint calculations (JSON Logic)
   - Evaluate routing conditions (JSON Logic)
   - Determine next action participants
   - Build transaction with encrypted payloads
   - Support file attachments

3. **Action Rejection**
   - Create rejection transactions
   - Reverse workflow to previous participant
   - Include rejection reason and metadata
   - Maintain audit trail

4. **Data Aggregation**
   - Aggregate historical transaction data
   - Filter based on required data fields
   - Apply participant-specific disclosures
   - Support JSON Pointer-based field selection
   - Decrypt payloads for authorized participants

5. **Payload Management**
   - Encrypt payloads for selective disclosure
   - Support multiple recipients per payload
   - Handle public vs. participant-specific data
   - Process tracking data separately
   - Manage file payloads independently

#### Blueprint Integration

1. **Blueprint Resolution**
   - Retrieve Blueprint definitions from Register Service
   - Cache Blueprint data for performance
   - Validate Blueprint structure
   - Extract action definitions
   - Parse disclosure rules
   - Evaluate conditions and calculations

2. **Participant Resolution**
   - Map Blueprint participant IDs to wallet addresses
   - Support dynamic participant assignment
   - Validate participant authorization
   - Support multi-participant actions

#### Transaction Integration

1. **Transaction Construction**
   - Build transaction requests with metadata
   - Set Blueprint ID, Action ID, Instance ID
   - Link to previous transaction hash
   - Include tracking data
   - Set transaction type (Action, Rejection, File)
   - Apply digital signatures

2. **Transaction Submission**
   - Submit to Register Service
   - Validate transaction acceptance
   - Handle submission errors
   - Retry with exponential backoff
   - Emit transaction events

3. **Transaction Notification**
   - Receive transaction confirmation events
   - Notify participants via real-time channel
   - Update action status
   - Trigger downstream workflows

#### File Management

1. **File Attachment**
   - Accept file uploads with actions
   - Validate file size and type
   - Create separate file transactions
   - Link file transaction to action transaction
   - Support multiple files per action
   - Store file metadata

2. **File Retrieval**
   - Retrieve file transaction by ID
   - Download file binary content
   - Apply access control
   - Support streaming for large files

#### Calculation & Conditions

1. **Calculation Engine**
   - Execute JSON Logic calculations
   - Apply calculated fields to submission data
   - Support complex expressions
   - Validate calculation results
   - Cache calculation outputs

2. **Condition Evaluation**
   - Evaluate routing conditions
   - Determine next action based on data
   - Support conditional branching
   - Validate condition logic
   - Log evaluation results

#### Real-Time Features

1. **SignalR Hub**
   - Maintain persistent connections
   - Authenticate connections via JWT
   - Support connection groups by wallet/tenant
   - Broadcast action notifications
   - Handle reconnection logic

2. **Event Broadcasting**
   - Notify on new actions available
   - Notify on transaction confirmation
   - Notify on action completion
   - Support targeted notifications

### Out of Scope

1. **Blueprint Execution Engine** - Handled by separate Sorcha.Blueprint.Engine service
2. **Wallet Management** - Handled by Sorcha.WalletService
3. **Transaction Storage** - Handled by Sorcha.RegisterService
4. **Peer-to-Peer Distribution** - Handled by Sorcha.Peer.Service
5. **Sustainability Calculations** - Will be external service integration (future)
6. **Advanced Analytics** - Separate service (future)

## Architecture

### Solution Structure

```
src/
├── Services/
│   └── Sorcha.Action.Service/              # Main REST API service
│       ├── Program.cs                       # Entry point and service configuration
│       ├── Endpoints/                       # Minimal API endpoint definitions
│       │   ├── ActionsEndpoints.cs
│       │   ├── FilesEndpoints.cs
│       │   └── NotificationsEndpoints.cs
│       ├── Services/                        # Business logic services
│       │   ├── ActionService.cs
│       │   ├── ActionResolver.cs
│       │   ├── PayloadResolver.cs
│       │   ├── TransactionRequestBuilder.cs
│       │   └── CalculationService.cs
│       ├── Hubs/                           # SignalR hubs
│       │   └── ActionsHub.cs
│       ├── Models/                         # DTOs and request/response models
│       │   ├── ActionSubmission.cs
│       │   ├── ActionResponse.cs
│       │   └── ActionFilter.cs
│       ├── Validators/                     # FluentValidation validators
│       │   └── ActionSubmissionValidator.cs
│       ├── Exceptions/                     # Custom exceptions
│       │   ├── ActionNotFoundException.cs
│       │   └── ValidationException.cs
│       └── appsettings.json

tests/
├── Sorcha.Action.Service.Tests/            # Unit tests
│   ├── Services/
│   ├── Validators/
│   └── Endpoints/
└── Sorcha.Action.Service.Integration.Tests/ # Integration tests
    ├── ActionsEndpointsTests.cs
    ├── FilesEndpointsTests.cs
    └── NotificationsTests.cs
```

### Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    Sorcha.Action.Service                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌──────────────┐      ┌──────────────┐      ┌──────────────┐  │
│  │   Actions    │      │    Files     │      │ Notifications│  │
│  │  Endpoints   │      │  Endpoints   │      │  Endpoints   │  │
│  └──────┬───────┘      └──────┬───────┘      └──────┬───────┘  │
│         │                     │                      │           │
│         └──────────┬──────────┴──────────┬───────────┘          │
│                    ▼                     ▼                       │
│         ┌──────────────────────────────────────────┐            │
│         │         Business Services                 │            │
│         ├──────────────────────────────────────────┤            │
│         │  • ActionService                         │            │
│         │  • ActionResolver                        │            │
│         │  • PayloadResolver                       │            │
│         │  • TransactionRequestBuilder             │            │
│         │  • CalculationService                    │            │
│         └──────────┬───────────────────────────────┘            │
│                    │                                             │
│                    ▼                                             │
│         ┌──────────────────────────────────────────┐            │
│         │         SignalR Hub                       │            │
│         │  • ActionsHub                             │            │
│         │  • Authenticated connections              │            │
│         │  • Broadcast to groups                    │            │
│         └──────────────────────────────────────────┘            │
│                                                                   │
└───────────────────────────┬───────────────────────────────────┬─┘
                            │                                   │
                ┌───────────▼──────────┐          ┌────────────▼─────────┐
                │   External Services   │          │   Storage & Cache    │
                ├──────────────────────┤          ├─────────────────────┤
                │ • WalletService      │          │ • Redis (caching)   │
                │ • RegisterService    │          │ • Blueprint cache   │
                │ • Blueprint.Service  │          │ • Schema cache      │
                └──────────────────────┘          └─────────────────────┘
```

### Technology Stack

**Runtime & Framework**
- .NET 10 (target framework: net10.0)
- ASP.NET Core Minimal APIs
- C# 13 with modern language features

**API & Documentation**
- .NET 10 built-in OpenAPI support (Microsoft.AspNetCore.OpenApi)
- Scalar.AspNetCore 2.10.0 for interactive API documentation
- OpenAPI specification at `/openapi/v1.json`
- Interactive UI at `/scalar/v1`

**Real-Time Communication**
- SignalR for real-time notifications
- JWT authentication for SignalR connections
- Connection groups for targeted broadcasting

**Validation & Processing**
- FluentValidation for request validation
- JsonSchema.Net for Blueprint schema validation
- JsonLogic.Net for calculations and conditions
- JsonPath.Net for data extraction

**Service Integration**
- .NET Aspire for service discovery and orchestration
- HTTP clients for service-to-service communication
- Resilience policies (retry, circuit breaker, timeout)

**Caching & Performance**
- Redis output caching via Aspire.StackExchange.Redis
- Tag-based cache invalidation
- In-memory caching for frequently accessed data

**Observability**
- OpenTelemetry (logs, metrics, traces)
- Health checks (`/health`, `/alive`)
- Structured logging with Serilog
- Correlation IDs for distributed tracing

**Testing**
- xUnit 2.9.3 test framework
- FluentAssertions for readable assertions
- Moq for mocking
- WebApplicationFactory for integration tests
- Testcontainers for Redis integration tests

## API Design

### REST Endpoints

#### 1. Get Starting Actions

```
GET /api/actions/{walletAddress}/{registerId}/blueprints
```

**Purpose:** Retrieve first actions in Blueprints that the wallet can initiate.

**Authorization:** JWT Bearer token (wallet owner)

**Path Parameters:**
- `walletAddress` (string) - Wallet address of the participant
- `registerId` (string) - Register ID for multi-register support

**Response:** `200 OK`
```json
[
  {
    "blueprintId": "bp_12345",
    "blueprintTitle": "Purchase Order",
    "actionId": "action_1",
    "actionTitle": "Submit Order",
    "description": "Create a new purchase order",
    "schema": { /* JSON Schema */ },
    "form": { /* Form UI definition */ }
  }
]
```

**Caching:** 5 minutes, tag: `actions:{walletAddress}`

---

#### 2. Get Pending Actions

```
GET /api/actions/{walletAddress}/{registerId}
```

**Purpose:** Retrieve all pending actions awaiting the participant's attention.

**Authorization:** JWT Bearer token (wallet owner)

**Path Parameters:**
- `walletAddress` (string) - Wallet address
- `registerId` (string) - Register ID

**Query Parameters:**
- `blueprintId` (string, optional) - Filter by Blueprint ID
- `status` (string, optional) - Filter by status (pending, completed, rejected)
- `page` (int, optional, default: 1) - Page number
- `pageSize` (int, optional, default: 20) - Items per page

**Response:** `200 OK`
```json
{
  "items": [
    {
      "transactionId": "tx_67890",
      "blueprintId": "bp_12345",
      "blueprintTitle": "Purchase Order",
      "actionId": "action_2",
      "actionTitle": "Approve Order",
      "status": "pending",
      "receivedAt": "2025-11-15T10:30:00Z",
      "metadata": { /* Action metadata */ }
    }
  ],
  "totalCount": 42,
  "page": 1,
  "pageSize": 20
}
```

**Caching:** 1 minute, tag: `actions:{walletAddress}`

---

#### 3. Get Action by ID

```
GET /api/actions/{walletAddress}/{registerId}/{transactionId}
```

**Purpose:** Retrieve detailed action data including Blueprint context and decrypted payloads.

**Authorization:** JWT Bearer token (wallet owner)

**Path Parameters:**
- `walletAddress` (string) - Wallet address
- `registerId` (string) - Register ID
- `transactionId` (string) - Transaction ID

**Query Parameters:**
- `aggregatePreviousTransactionData` (bool, optional, default: true) - Include historical data

**Response:** `200 OK`
```json
{
  "transactionId": "tx_67890",
  "blueprintId": "bp_12345",
  "blueprintTitle": "Purchase Order",
  "actionId": "action_2",
  "actionTitle": "Approve Order",
  "description": "Review and approve the purchase order",
  "schema": { /* JSON Schema for this action */ },
  "form": { /* Form UI definition */ },
  "data": {
    /* Aggregated data from previous transactions */
    "itemName": "Widget",
    "quantity": 100,
    "price": 25.50
  },
  "calculations": [
    {
      "field": "total",
      "formula": { /* JSON Logic */ },
      "value": 2550.00
    }
  ],
  "conditions": [
    {
      "participantId": "approver",
      "condition": { /* JSON Logic */ }
    }
  ],
  "previousTransactionHash": "hash_56789",
  "createdAt": "2025-11-15T10:30:00Z"
}
```

**Response Codes:**
- `200 OK` - Action found and authorized
- `404 Not Found` - Action not found or not for this wallet
- `401 Unauthorized` - Invalid or missing JWT token
- `403 Forbidden` - Wallet not authorized for this action

**Caching:** 2 minutes, tag: `actions:{transactionId}`

---

#### 4. Submit Action

```
POST /api/actions
```

**Purpose:** Submit completed action data, validate against schema, encrypt payloads, and create transaction.

**Authorization:** JWT Bearer token (wallet owner)

**Request Body:** `ActionSubmission` (max 4MB)
```json
{
  "walletAddress": "wallet_abc123",
  "registerId": "register_1",
  "blueprintId": "bp_12345",
  "actionId": "action_2",
  "transactionId": "tx_67890",  // Optional for new instances
  "data": {
    /* User-provided data matching action schema */
    "approvalStatus": "approved",
    "approverComments": "Looks good, approved"
  },
  "files": [
    {
      "name": "invoice.pdf",
      "contentType": "application/pdf",
      "base64Data": "JVBERi0xLjQK..."
    }
  ]
}
```

**Response:** `202 Accepted`
```json
{
  "transactionId": "tx_new123",
  "status": "pending",
  "message": "Action submitted successfully",
  "submittedAt": "2025-11-15T11:00:00Z"
}
```

**Response Codes:**
- `202 Accepted` - Submission accepted and queued
- `400 Bad Request` - Validation failed
- `401 Unauthorized` - Invalid or missing JWT token
- `403 Forbidden` - Wallet not authorized
- `404 Not Found` - Blueprint or action not found
- `413 Payload Too Large` - Request exceeds 4MB

**Validation:**
- JWT token valid and matches walletAddress
- Blueprint exists and is published
- Action exists in Blueprint
- Data matches JSON Schema
- File sizes within limits
- Wallet has permission for this action

---

#### 5. Reject Action

```
POST /api/actions/reject
```

**Purpose:** Create rejection transaction, reversing action flow to previous participant.

**Authorization:** JWT Bearer token (wallet owner)

**Request Body:** `ActionRejection`
```json
{
  "walletAddress": "wallet_abc123",
  "registerId": "register_1",
  "transactionId": "tx_67890",
  "reason": "Incorrect pricing, please review",
  "data": {
    /* Optional additional context */
  }
}
```

**Response:** `202 Accepted`
```json
{
  "transactionId": "tx_reject456",
  "status": "rejected",
  "message": "Action rejected successfully",
  "submittedAt": "2025-11-15T11:05:00Z"
}
```

**Response Codes:**
- `202 Accepted` - Rejection accepted
- `400 Bad Request` - Validation failed
- `401 Unauthorized` - Invalid or missing JWT token
- `403 Forbidden` - Wallet not authorized
- `404 Not Found` - Transaction not found

---

#### 6. Get Action Files

```
GET /api/files/{walletAddress}/{registerId}/{transactionId}/{fileId}
```

**Purpose:** Retrieve file attached to an action.

**Authorization:** JWT Bearer token (wallet owner)

**Path Parameters:**
- `walletAddress` (string) - Wallet address
- `registerId` (string) - Register ID
- `transactionId` (string) - Action transaction ID
- `fileId` (string) - File transaction ID

**Response:** `200 OK`
- Content-Type: as per file type
- Body: Binary file data
- Headers: Content-Disposition with filename

**Response Codes:**
- `200 OK` - File found and authorized
- `404 Not Found` - File not found
- `401 Unauthorized` - Invalid token
- `403 Forbidden` - Not authorized for this file

---

#### 7. Internal Notification Endpoint

```
POST /api/actions/notify
```

**Purpose:** Internal endpoint receiving confirmed transactions from Register Service and broadcasting via SignalR.

**Authorization:** Internal service authentication (API key or Aspire service auth)

**Request Body:** `TransactionConfirmed`
```json
{
  "transactionId": "tx_new123",
  "walletAddresses": ["wallet_abc123", "wallet_xyz789"],
  "blueprintId": "bp_12345",
  "actionId": "action_3",
  "status": "confirmed",
  "confirmedAt": "2025-11-15T11:02:00Z"
}
```

**Response:** `202 Accepted`

**Response Codes:**
- `202 Accepted` - Notification queued
- `401 Unauthorized` - Invalid service auth
- `400 Bad Request` - Invalid payload

### SignalR Hub

#### ActionsHub

**Endpoint:** `/actionshub`

**Authentication:** JWT token via query parameter `?access_token={jwt}`

**Hub Methods (Server → Client):**

1. **ActionAvailable(actionNotification)**
   - Sent when a new action is available for the participant
   - Payload: `{ transactionId, blueprintId, actionId, actionTitle, receivedAt }`

2. **ActionConfirmed(confirmationNotification)**
   - Sent when a submitted action is confirmed
   - Payload: `{ transactionId, status, confirmedAt }`

3. **ActionRejected(rejectionNotification)**
   - Sent when an action is rejected
   - Payload: `{ transactionId, reason, rejectedAt }`

**Connection Groups:**
- `wallet:{walletAddress}` - Receive notifications for specific wallet
- `tenant:{tenantId}` - Receive notifications for entire tenant (future)

**Connection Lifecycle:**
- Authenticate on connect
- Add to wallet/tenant groups
- Remove from groups on disconnect
- Support reconnection with same groups

## Service Interfaces

### IActionService

```csharp
public interface IActionService
{
    // Action retrieval
    Task<IEnumerable<ActionResponse>> GetStartingActionsAsync(
        string walletAddress,
        string registerId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<ActionSummary>> GetPendingActionsAsync(
        string walletAddress,
        string registerId,
        ActionFilter filter,
        CancellationToken cancellationToken = default);

    Task<ActionResponse?> GetActionByIdAsync(
        string walletAddress,
        string registerId,
        string transactionId,
        bool aggregateData = true,
        CancellationToken cancellationToken = default);

    // Action submission
    Task<TransactionResult> SubmitActionAsync(
        ActionSubmission submission,
        CancellationToken cancellationToken = default);

    Task<TransactionResult> RejectActionAsync(
        ActionRejection rejection,
        CancellationToken cancellationToken = default);
}
```

### IActionResolver

```csharp
public interface IActionResolver
{
    Task<Blueprint> GetBlueprintAsync(
        string blueprintId,
        string registerId,
        CancellationToken cancellationToken = default);

    Task<Models.Action> GetActionDefinitionAsync(
        string blueprintId,
        string actionId,
        string registerId,
        CancellationToken cancellationToken = default);

    Task<Dictionary<string, string>> ResolveParticipantWalletsAsync(
        Blueprint blueprint,
        string instanceId,
        string registerId,
        CancellationToken cancellationToken = default);
}
```

### IPayloadResolver

```csharp
public interface IPayloadResolver
{
    Task<IEnumerable<Payload>> CreatePayloadsAsync(
        ActionSubmission submission,
        Blueprint blueprint,
        Models.Action action,
        Dictionary<string, string> participantWallets,
        CancellationToken cancellationToken = default);

    Task<Dictionary<string, object>> AggregateHistoricalDataAsync(
        string transactionId,
        string walletAddress,
        Blueprint blueprint,
        Models.Action action,
        string registerId,
        CancellationToken cancellationToken = default);

    Task<byte[]> EncryptPayloadAsync(
        object data,
        IEnumerable<string> recipientWallets,
        CancellationToken cancellationToken = default);

    Task<object> DecryptPayloadAsync(
        byte[] encryptedData,
        string walletAddress,
        CancellationToken cancellationToken = default);
}
```

### ITransactionRequestBuilder

```csharp
public interface ITransactionRequestBuilder
{
    Task<TransactionRequest> BuildNewInstanceTransactionAsync(
        ActionSubmission submission,
        Blueprint blueprint,
        CancellationToken cancellationToken = default);

    Task<TransactionRequest> BuildContinuationTransactionAsync(
        ActionSubmission submission,
        string previousTransactionHash,
        Blueprint blueprint,
        CancellationToken cancellationToken = default);

    Task<TransactionRequest> BuildRejectionTransactionAsync(
        ActionRejection rejection,
        string previousTransactionHash,
        Blueprint blueprint,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<TransactionRequest>> BuildFileTransactionsAsync(
        IEnumerable<FileAttachment> files,
        string walletAddress,
        string registerId,
        CancellationToken cancellationToken = default);
}
```

### ICalculationService

```csharp
public interface ICalculationService
{
    Task<Dictionary<string, object>> ApplyCalculationsAsync(
        Dictionary<string, object> data,
        IEnumerable<Calculation> calculations,
        CancellationToken cancellationToken = default);

    Task<string?> EvaluateRoutingConditionsAsync(
        Dictionary<string, object> data,
        IEnumerable<Condition> conditions,
        CancellationToken cancellationToken = default);

    object EvaluateJsonLogic(
        JsonNode logic,
        Dictionary<string, object> data);
}
```

## Data Models

### ActionSubmission

```csharp
public class ActionSubmission
{
    public required string WalletAddress { get; set; }
    public required string RegisterId { get; set; }
    public required string BlueprintId { get; set; }
    public required string ActionId { get; set; }
    public string? TransactionId { get; set; }  // Null for new instances
    public required Dictionary<string, object> Data { get; set; }
    public List<FileAttachment>? Files { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}
```

### ActionResponse

```csharp
public class ActionResponse
{
    public required string TransactionId { get; set; }
    public required string BlueprintId { get; set; }
    public required string BlueprintTitle { get; set; }
    public required string ActionId { get; set; }
    public required string ActionTitle { get; set; }
    public string? Description { get; set; }
    public JsonNode? Schema { get; set; }
    public Control? Form { get; set; }
    public Dictionary<string, object>? Data { get; set; }
    public List<Calculation>? Calculations { get; set; }
    public List<Condition>? Conditions { get; set; }
    public string? PreviousTransactionHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public ActionStatus Status { get; set; }
}
```

### FileAttachment

```csharp
public class FileAttachment
{
    public required string Name { get; set; }
    public required string ContentType { get; set; }
    public required string Base64Data { get; set; }  // Or Stream for large files
    public long SizeBytes { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}
```

## Configuration

### appsettings.json

```json
{
  "ActionService": {
    "MaxRequestSizeBytes": 4194304,  // 4MB
    "MaxFileCountPerAction": 10,
    "MaxFileSizeBytes": 10485760,  // 10MB per file
    "AllowedFileTypes": [
      "application/pdf",
      "image/jpeg",
      "image/png",
      "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    ],
    "DefaultPageSize": 20,
    "MaxPageSize": 100,
    "CacheDuration": {
      "StartingActions": "00:05:00",
      "PendingActions": "00:01:00",
      "ActionDetails": "00:02:00",
      "Blueprints": "00:10:00"
    }
  },
  "ServiceEndpoints": {
    "WalletService": "https+http://wallet-service",
    "RegisterService": "https+http://register-service",
    "BlueprintService": "https+http://blueprint-service"
  },
  "SignalR": {
    "EnableDetailedErrors": false,
    "KeepAliveInterval": "00:00:15",
    "HandshakeTimeout": "00:00:15",
    "MaximumReceiveMessageSize": 32768
  },
  "Authentication": {
    "Authority": "https://identity.sorcha.dev",
    "Audience": "action-service",
    "RequireHttpsMetadata": true
  }
}
```

## Security

### Authentication & Authorization

1. **JWT Bearer Authentication**
   - All endpoints require valid JWT token
   - Token must contain wallet address claim
   - Token issuer must match configured authority
   - Token audience must include "action-service"

2. **Wallet Address Validation**
   - Request wallet address must match JWT claim
   - Prevent impersonation attacks
   - Log suspicious access attempts

3. **Action Authorization**
   - Verify wallet is authorized for requested action
   - Check Blueprint participant definitions
   - Validate transaction ownership
   - Apply delegation rules (future)

4. **SignalR Authentication**
   - JWT token via query parameter
   - Validate token on connect
   - Assign to appropriate groups
   - Prevent cross-wallet notifications

### Data Protection

1. **Payload Encryption**
   - Encrypt payloads at rest and in transit
   - Use recipient public keys for encryption
   - Support multiple recipients per payload
   - Implement selective disclosure via Disclosure rules

2. **Input Validation**
   - Validate all inputs against schemas
   - Sanitize file uploads
   - Prevent injection attacks
   - Limit request sizes

3. **Rate Limiting**
   - Limit requests per wallet per minute
   - Prevent DoS attacks
   - Return 429 Too Many Requests when exceeded

### Audit Logging

1. **Logged Events**
   - Action retrieval requests
   - Action submissions
   - Action rejections
   - File uploads
   - Authorization failures
   - Validation failures

2. **Log Data**
   - Timestamp
   - Wallet address
   - Transaction ID
   - Action taken
   - Result (success/failure)
   - Client IP address (if available)
   - Correlation ID for tracing

## Testing Strategy

### Unit Tests (>80% coverage)

1. **Service Tests**
   - ActionService methods
   - ActionResolver logic
   - PayloadResolver encryption/decryption
   - TransactionRequestBuilder construction
   - CalculationService JSON Logic evaluation

2. **Validator Tests**
   - ActionSubmissionValidator rules
   - Schema validation logic
   - File validation rules

3. **Endpoint Tests**
   - Request handling
   - Response formatting
   - Error handling
   - Authorization checks

### Integration Tests

1. **API Endpoint Tests**
   - GET /api/actions/{wallet}/{register}/blueprints
   - GET /api/actions/{wallet}/{register}
   - GET /api/actions/{wallet}/{register}/{tx}
   - POST /api/actions
   - POST /api/actions/reject
   - GET /api/files/{wallet}/{register}/{tx}/{fileId}

2. **Service Integration Tests**
   - WalletService integration
   - RegisterService integration
   - BlueprintService integration
   - Redis caching integration

3. **SignalR Tests**
   - Connection authentication
   - Group assignment
   - Notification broadcasting
   - Reconnection handling

### End-to-End Tests

1. **Complete Workflow**
   - Get starting actions
   - Submit first action
   - Receive notification
   - Get next action
   - Submit continuation
   - Complete workflow

2. **Rejection Workflow**
   - Submit action
   - Reject action
   - Verify reversal
   - Re-submit corrected action

3. **File Attachment Workflow**
   - Submit action with files
   - Retrieve action with file references
   - Download files
   - Verify file integrity

## Performance Requirements

1. **Response Times**
   - GET endpoints: < 200ms (p95)
   - POST endpoints: < 500ms (p95)
   - SignalR notifications: < 100ms
   - File downloads: Depends on size, support streaming

2. **Throughput**
   - Support 1000 requests/second per instance
   - Support 10,000 concurrent SignalR connections
   - Horizontal scaling for higher load

3. **Caching**
   - Cache Blueprints for 10 minutes
   - Cache pending actions for 1 minute
   - Cache action details for 2 minutes
   - Tag-based invalidation on updates

## Migration & Deployment

### Deployment Strategy

1. **Container Deployment**
   - Docker image: `sorcha/action-service:latest`
   - Linux container
   - Health checks configured
   - Resource limits: 512MB RAM, 0.5 CPU

2. **Aspire Orchestration**
   - Service discovery via Aspire
   - Configuration via Aspire
   - Health check integration
   - Automatic restart on failure

3. **Scaling**
   - Horizontal scaling: 1-10 instances
   - Load balancing via Aspire/K8s
   - Sticky sessions for SignalR
   - Redis backplane for SignalR scale-out

### Migration from Legacy

Not applicable - new service implementation.

## Dependencies

### NuGet Packages

```xml
<ItemGroup>
  <!-- ASP.NET Core -->
  <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.0" />
  <PackageReference Include="Microsoft.AspNetCore.SignalR" Version="1.0.0" />

  <!-- API Documentation -->
  <PackageReference Include="Scalar.AspNetCore" Version="2.10.0" />

  <!-- Validation -->
  <PackageReference Include="FluentValidation.AspNetCore" Version="11.3.0" />
  <PackageReference Include="JsonSchema.Net" Version="7.2.4" />

  <!-- JSON Processing -->
  <PackageReference Include="JsonLogic.Net" Version="2.0.0" />
  <PackageReference Include="JsonPath.Net" Version="1.1.3" />

  <!-- Caching -->
  <PackageReference Include="Aspire.StackExchange.Redis" Version="13.0.0" />

  <!-- Aspire -->
  <PackageReference Include="Aspire.Hosting.AppHost" Version="13.0.0" />

  <!-- Testing -->
  <PackageReference Include="xunit" Version="2.9.3" />
  <PackageReference Include="FluentAssertions" Version="7.0.1" />
  <PackageReference Include="Moq" Version="4.20.72" />
  <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
  <PackageReference Include="Testcontainers.Redis" Version="4.0.0" />
</ItemGroup>
```

### Project References

```xml
<ItemGroup>
  <ProjectReference Include="..\..\Common\Sorcha.Blueprint.Models\Sorcha.Blueprint.Models.csproj" />
  <ProjectReference Include="..\..\Common\Sorcha.Cryptography\Sorcha.Cryptography.csproj" />
  <ProjectReference Include="..\..\Common\Sorcha.TransactionHandler\Sorcha.TransactionHandler.csproj" />
  <ProjectReference Include="..\..\Common\Sorcha.ServiceDefaults\Sorcha.ServiceDefaults.csproj" />
</ItemGroup>
```

### Service Dependencies

- **Sorcha.WalletService** - Wallet operations, encryption/decryption
- **Sorcha.RegisterService** - Transaction storage and retrieval
- **Sorcha.Blueprint.Service** - Blueprint definitions
- **Redis** - Output caching, SignalR backplane

## Success Criteria

1. ✅ All REST endpoints implemented with OpenAPI docs
2. ✅ SignalR hub with real-time notifications
3. ✅ Schema validation for action submissions
4. ✅ Payload encryption with selective disclosure
5. ✅ File attachment support
6. ✅ JSON Logic calculations and conditions
7. ✅ Integration with WalletService, RegisterService, BlueprintService
8. ✅ >80% unit test coverage
9. ✅ Integration tests for all endpoints
10. ✅ Performance targets met (response times, throughput)
11. ✅ Aspire orchestration integration
12. ✅ Health checks and observability
13. ✅ Comprehensive API documentation via Scalar

## Open Questions

1. **Sustainability Metrics** - Should we implement sustainability calculations in this service or integrate with external service?
   - **Recommendation:** External service integration (future phase)

2. **Multi-Tenancy** - How should tenant isolation be implemented?
   - **Recommendation:** Tenant ID in JWT, filter all queries by tenant

3. **File Storage** - Where should file binaries be stored?
   - **Recommendation:** Store in transaction payloads initially, move to blob storage in future

4. **Notification Retention** - How long should SignalR notifications be retained for offline clients?
   - **Recommendation:** Use persistent queue (Azure Service Bus) for offline delivery

5. **Action History** - Should we maintain separate action history or rely on transaction history?
   - **Recommendation:** Rely on transaction history from RegisterService

## References

1. [Sorcha Architecture](../../docs/architecture.md)
2. [Blueprint Schema](../../docs/blueprint-schema.md)
3. [Transaction Handler Specification](sorcha-transaction-handler.md)
4. [Wallet Service Specification](sorcha-wallet-service.md)
5. [Register Service Specification](sorcha-register-service.md)
6. [JSON Schema Specification](https://json-schema.org/draft/2020-12/json-schema-core)
7. [JSON Logic Specification](https://jsonlogic.com/)
8. [SignalR Documentation](https://learn.microsoft.com/en-us/aspnet/core/signalr/)

---

**Document Control**
- **Created:** 2025-11-15
- **Author:** Sorcha Architecture Team
- **Review Frequency:** Monthly
- **Next Review:** 2025-12-15
