# Sorcha Platform - Integration Guide

**Version:** 1.0.0
**Last Updated:** 2025-11-17
**Sprint:** 7

---

## Table of Contents

1. [Introduction](#introduction)
2. [Integration Patterns](#integration-patterns)
3. [Quick Start Integration](#quick-start-integration)
4. [Blueprint-Based Workflows](#blueprint-based-workflows)
5. [Wallet Integration](#wallet-integration)
6. [Register Integration](#register-integration)
7. [Real-time Notifications](#real-time-notifications)
8. [Best Practices](#best-practices)
9. [Troubleshooting](#troubleshooting)
10. [Advanced Topics](#advanced-topics)

---

## Introduction

This guide provides step-by-step instructions for integrating applications with the Sorcha Platform. Whether you're building a frontend application, backend service, or integration middleware, this guide covers the essential patterns and practices.

### What You'll Learn

- How to create and manage blueprints
- How to integrate wallet services for cryptographic operations
- How to submit and track transactions on the register
- How to handle real-time notifications
- Best practices for production deployments

### Prerequisites

- Basic understanding of REST APIs
- Familiarity with JSON and HTTP
- Development environment with HTTP client capabilities
- .NET 10 SDK (for running the platform)

---

## Integration Patterns

### Pattern 1: Direct API Integration

**Use Case:** Simple applications with direct HTTP access

```
┌─────────────┐
│   Your App  │
└──────┬──────┘
       │ HTTP/REST
       ▼
┌─────────────┐
│ API Gateway │
└─────────────┘
```

**Advantages:**
- Simple and straightforward
- No additional dependencies
- Full control over requests

**Example Technologies:**
- JavaScript (fetch, axios)
- C# (HttpClient)
- Python (requests)
- Any HTTP client

### Pattern 2: SDK Integration (Future)

**Use Case:** Applications needing type-safe, language-specific integration

```
┌─────────────┐
│   Your App  │
│             │
│  Sorcha SDK │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ API Gateway │
└─────────────┘
```

**Status:** Coming in future releases

### Pattern 3: Event-Driven Integration

**Use Case:** Real-time applications requiring instant updates

```
┌─────────────┐
│   Your App  │
│             │
│  SignalR    │◄─── Real-time Events
└──────┬──────┘
       │ REST API
       ▼
┌─────────────┐
│ API Gateway │
└─────────────┘
```

---

## Quick Start Integration

### Step 1: Set Up Your Development Environment

```bash
# Clone the repository
git clone https://github.com/Sorcha-Platform/Sorcha.git
cd Sorcha

# Run with .NET Aspire
dotnet run --project src/Apps/Sorcha.AppHost
```

The platform is reached through the **API Gateway at `http://localhost`** (port 80; under Aspire the
gateway is `https://localhost:7082`). Scalar API docs are served per service via the gateway.

> **Every request requires a JWT** — `Authorization: Bearer <token>`. Obtain one via
> `POST /api/auth/login` (user) or `POST /api/service-auth/token` (service). See
> [Authentication Setup](AUTHENTICATION-SETUP.md). The examples below assume `$TOKEN` is set.

### Step 2: Create Your First Wallet

```bash
curl -X POST http://localhost/api/v1/wallets \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
  -d '{
    "name": "Integration Test Wallet",
    "algorithm": "ED25519",
    "wordCount": 24
  }'
```

**Response:**
```json
{
  "id": "…",
  "address": "ws1q…",
  "name": "Integration Test Wallet",
  "algorithm": "ED25519"
}
```

**Save the wallet `address` — you'll need it for subsequent operations.**

### Step 3: Create a Register

```bash
curl -X POST http://localhost/api/registers \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
  -d '{ "name": "Integration Test Register" }'
```

**Response:**
```json
{
  "id": "…",
  "name": "Integration Test Register"
}
```

### Step 4: Create and Publish a Blueprint

```bash
# Create blueprint
curl -X POST http://localhost/api/blueprints \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Hello World Workflow",
    "participants": [
      { "id": "sender", "name": "Message Sender" },
      { "id": "receiver", "name": "Message Receiver" }
    ],
    "actions": [
      {
        "id": "0",
        "title": "Send Message",
        "sender": "sender"
      }
    ]
  }'

# Publish it
curl -X POST http://localhost/api/blueprints/{blueprint-id}/publish
```

### Step 5: Submit Your First Action

```bash
curl -X POST http://localhost/api/actions \
  -H "Content-Type: application/json" \
  -d '{
    "blueprintId": "{blueprint-id}",
    "actionId": "0",
    "senderWallet": "wallet-abc123",
    "registerAddress": "register-xyz789",
    "payloadData": {
      "message": "Hello, Sorcha!"
    }
  }'
```

**Congratulations!** You've successfully submitted your first action. 🎉

---

## Blueprint-Based Workflows

### Understanding Blueprints

Blueprints define the structure and rules of your workflows:

```
Blueprint
├── Participants (who's involved)
├── Actions (what can happen)
│   ├── Data Schema (what data is required)
│   ├── Disclosures (who sees what)
│   ├── Calculations (computed fields)
│   └── Routing (what happens next)
└── Metadata
```

### Creating Complex Blueprints

#### Example: Multi-Step Approval Workflow

```json
{
  "title": "Invoice Approval Workflow",
  "description": "Three-tier invoice approval process",
  "participants": [
    { "id": "submitter", "name": "Invoice Submitter" },
    { "id": "manager", "name": "Department Manager" },
    { "id": "director", "name": "Finance Director" },
    { "id": "finance", "name": "Finance Department" }
  ],
  "actions": [
    {
      "id": "0",
      "title": "Submit Invoice",
      "sender": "submitter",
      "data": {
        "type": "object",
        "properties": {
          "invoiceNumber": { "type": "string" },
          "amount": { "type": "number" },
          "vendor": { "type": "string" }
        },
        "required": ["invoiceNumber", "amount", "vendor"]
      },
      "routing": {
        "next": "manager"
      }
    },
    {
      "id": "1",
      "title": "Manager Approval",
      "sender": "manager",
      "condition": {
        "if": { ">": [{ "var": "amount" }, 10000] },
        "then": { "route": "director" },
        "else": { "route": "finance" }
      }
    },
    {
      "id": "2",
      "title": "Director Approval",
      "sender": "director",
      "routing": {
        "next": "finance"
      }
    },
    {
      "id": "3",
      "title": "Finance Processing",
      "sender": "finance"
    }
  ]
}
```

### Using the Fluent API (C#)

For .NET applications, use the Fluent API for type-safe blueprint creation:

```csharp
using Sorcha.Blueprint.Fluent;

var blueprint = BlueprintBuilder.Create()
    .WithTitle("Invoice Approval Workflow")
    .WithDescription("Three-tier invoice approval process")
    .AddParticipant("submitter", p => p.Named("Invoice Submitter"))
    .AddParticipant("manager", p => p.Named("Department Manager"))
    .AddParticipant("director", p => p.Named("Finance Director"))
    .AddParticipant("finance", p => p.Named("Finance Department"))
    .AddAction(0, a => a
        .WithTitle("Submit Invoice")
        .SentBy("submitter")
        .RequiresData(d => d
            .AddString("invoiceNumber", f => f.IsRequired())
            .AddNumber("amount", f => f.IsRequired())
            .AddString("vendor", f => f.IsRequired()))
        .RouteConditionally(r => r
            .When(w => w.GreaterThan("amount", 10000))
            .ThenRoute("director")
            .ElseRoute("manager")))
    .Build();
```

---

## Wallet Integration

### Cryptographic Operations

#### 1. Signing Transactions

```javascript
async function signTransaction(walletId, data) {
  const response = await fetch(`http://localhost/api/wallets/${walletId}/sign`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      data: btoa(data), // Base64 encode
      algorithm: 'ED25519'
    })
  });

  const result = await response.json();
  return result.signature;
}
```

#### 2. Encrypting Sensitive Data

```javascript
async function encryptPayload(walletId, data, recipientWalletId) {
  const response = await fetch(`http://localhost/api/wallets/${walletId}/encrypt`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      data: JSON.stringify(data),
      recipientWalletId: recipientWalletId
    })
  });

  const result = await response.json();
  return result.encryptedData;
}
```

#### 3. Decrypting Payloads

```javascript
async function decryptPayload(walletId, encryptedData) {
  const response = await fetch(`http://localhost/api/wallets/${walletId}/decrypt`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ encryptedData })
  });

  const result = await response.json();
  return JSON.parse(result.data);
}
```

### Key Management Best Practices

1. **Never expose private keys** - they never leave the wallet service
2. **Use appropriate algorithms:**
   - **ED25519**: Fast, small signatures, recommended for most use cases
   - **NISTP256**: FIPS-compliant, good for regulated industries
   - **RSA**: Large signatures, good for legacy compatibility

3. **Implement key rotation** - regularly rotate encryption keys
4. **Use delegation** - for temporary access, use wallet delegation instead of sharing keys

---

## Register Integration

### Submitting Transactions

#### Basic Transaction Submission

```python
import requests
import base64
import json

def submit_transaction(register_id, wallet_address, payload_data):
    payload_json = json.dumps(payload_data)
    payload_base64 = base64.b64encode(payload_json.encode()).decode()

    response = requests.post(
        f"http://localhost/api/registers/{register_id}/transactions",
        json={
            "transactionType": "Action",
            "senderAddress": wallet_address,
            "payload": payload_base64,
            "metadata": {
                "blueprintId": "bp-123",
                "actionId": "0"
            }
        }
    )

    return response.json()
```

### Querying Transactions

#### Time-Based Queries

```javascript
async function getTransactionsByTimeRange(registerId, startTime, endTime) {
  const url = new URL(`http://localhost/api/registers/${registerId}/transactions`);
  url.searchParams.set('startTime', startTime.toISOString());
  url.searchParams.set('endTime', endTime.toISOString());

  const response = await fetch(url);
  return await response.json();
}

// Example usage
const transactions = await getTransactionsByTimeRange(
  'register-xyz789',
  new Date('2025-11-17T00:00:00Z'),
  new Date('2025-11-17T23:59:59Z')
);
```

#### OData Queries

```javascript
// Filter by sender and transaction type
const url = `http://localhost/api/registers/${registerId}/transactions?$filter=senderAddress eq 'wallet-abc123' and transactionType eq 'Action'&$top=10`;

const response = await fetch(url);
const results = await response.json();
```

### Chain Validation

```javascript
async function validateChain(registerId) {
  const response = await fetch(`http://localhost/api/registers/${registerId}/chain`);
  const validation = await response.json();

  if (!validation.isValid) {
    console.error('Chain integrity compromised!');
    console.error('Errors:', validation.errors);
  }

  return validation.isValid;
}
```

---

## Real-time Notifications

### SignalR Integration (JavaScript)

#### Complete Example

```javascript
import * as signalR from '@microsoft/signalr';

class SorchaNotificationClient {
  constructor(hubUrl) {
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          // Exponential backoff
          return Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000);
        }
      })
      .configureLogging(signalR.LogLevel.Information)
      .build();

    this.setupHandlers();
  }

  setupHandlers() {
    this.connection.onreconnecting((error) => {
      console.log('Connection lost. Reconnecting...', error);
    });

    this.connection.onreconnected((connectionId) => {
      console.log('Reconnected with ID:', connectionId);
    });

    this.connection.onclose((error) => {
      console.log('Connection closed:', error);
    });
  }

  async start() {
    try {
      await this.connection.start();
      console.log('SignalR Connected');
    } catch (err) {
      console.error('SignalR Connection Error:', err);
      setTimeout(() => this.start(), 5000);
    }
  }

  // Thin-signal events (Feature 118): handlers receive opaque IDs + timestamps;
  // group membership is server-side, so there is no client-side "subscribe" call.
  on(event, callback) {
    this.connection.on(event, callback);
  }

  async stop() {
    await this.connection.stop();
  }
}

// Usage — connect to a hub from the API docs (here, BlueprintHub). JWT goes in the query string.
const client = new SorchaNotificationClient(`http://localhost/hubs/blueprint?access_token=${token}`);

client.on('InstanceAdvanced', async (instanceId) => {
  // Pull full detail via the authenticated REST endpoint.
  const res = await fetch(`http://localhost/api/instances/${instanceId}`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  updateUI(await res.json());
});

await client.start();
```

### SignalR Integration (C#)

```csharp
using Microsoft.AspNetCore.SignalR.Client;

public class SorchaNotificationClient : IAsyncDisposable
{
    private readonly HubConnection _connection;

    public SorchaNotificationClient(string hubUrl)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        SetupHandlers();
    }

    private void SetupHandlers()
    {
        // Thin-signal (Feature 118): the payload is an id; fetch full detail via REST.
        _connection.On<string>("InstanceAdvanced", instanceId =>
        {
            Console.WriteLine($"Instance advanced: {instanceId}");
            OnInstanceAdvanced?.Invoke(instanceId);
        });

        _connection.Reconnecting += error =>
        {
            Console.WriteLine($"Connection lost. Reconnecting... {error}");
            return Task.CompletedTask;
        };
    }

    public event Action<string>? OnInstanceAdvanced;

    public async Task StartAsync()
    {
        await _connection.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
```

---

## Best Practices

### 1. Error Handling

```javascript
async function robustApiCall(url, options) {
  const maxRetries = 3;
  const retryDelay = 1000; // 1 second

  for (let attempt = 1; attempt <= maxRetries; attempt++) {
    try {
      const response = await fetch(url, options);

      if (!response.ok) {
        if (response.status >= 500) {
          // Server error - retry
          if (attempt < maxRetries) {
            await new Promise(resolve => setTimeout(resolve, retryDelay * attempt));
            continue;
          }
        }

        const error = await response.json();
        throw new Error(`API Error: ${error.error || response.statusText}`);
      }

      return await response.json();
    } catch (error) {
      if (attempt === maxRetries) {
        throw error;
      }

      await new Promise(resolve => setTimeout(resolve, retryDelay * attempt));
    }
  }
}
```

### 2. Connection Pooling (C#)

```csharp
// Use a single HttpClient instance (singleton)
public class SorchaApiClient
{
    private static readonly HttpClient _httpClient = new HttpClient
    {
        BaseAddress = new Uri("http://localhost"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    public static HttpClient Client => _httpClient;
}
```

### 3. Caching

```javascript
class BlueprintCache {
  constructor(ttl = 300000) { // 5 minutes
    this.cache = new Map();
    this.ttl = ttl;
  }

  async get(blueprintId) {
    const cached = this.cache.get(blueprintId);

    if (cached && Date.now() - cached.timestamp < this.ttl) {
      return cached.data;
    }

    const response = await fetch(`http://localhost/api/blueprints/${blueprintId}`);
    const blueprint = await response.json();

    this.cache.set(blueprintId, {
      data: blueprint,
      timestamp: Date.now()
    });

    return blueprint;
  }
}
```

### 4. Idempotency

```javascript
// Use instance IDs for idempotent action submission
async function submitActionIdempotent(actionData) {
  // Generate a deterministic instance ID
  const instanceId = generateInstanceId(actionData);

  const response = await fetch('http://localhost/api/actions', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      ...actionData,
      instanceId // Ensures resubmissions are idempotent
    })
  });

  return await response.json();
}
```

---

## Troubleshooting

### Common Issues

#### 1. "Blueprint not found" Error

**Cause:** Blueprint hasn't been published or ID is incorrect

**Solution:**
```bash
# List all blueprints
curl http://localhost/api/blueprints

# Publish blueprint
curl -X POST http://localhost/api/blueprints/{id}/publish
```

#### 2. Connection Refused

**Cause:** Services not running

**Solution:**
```bash
# Start services
dotnet run --project src/Apps/Sorcha.AppHost

# Check health
curl http://localhost/api/health
```

#### 3. SignalR Connection Drops

**Cause:** Network instability or server restart

**Solution:** Use automatic reconnection (shown in examples above)

---

## Advanced Topics

### Custom Integrations

#### Webhook Integration (Future)

```javascript
// Register webhook for transaction confirmations
await fetch('http://localhost/api/webhooks', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    url: 'https://your-app.com/webhooks/sorcha',
    events: ['transaction.confirmed', 'action.pending'],
    secret: 'your-webhook-secret'
  })
});
```

### Monitoring and Observability

```bash
# Health check with metrics
curl http://localhost/api/health

# Service-specific health
curl http://localhost/api/blueprint/health
curl http://localhost/api/wallet/health
curl http://localhost/api/register/health
```

---

## Next Steps

1. **Explore the API:** Visit http://localhost/scalar/v1
2. **Read API Documentation:** See [API-DOCUMENTATION.md](../reference/API-DOCUMENTATION.md)
3. **Review Examples:** Check `/examples` directory
4. **Join Community:** GitHub Discussions

---

**Need Help?**

- GitHub Issues: https://github.com/Sorcha-Platform/Sorcha/issues
- Documentation: https://docs.sorcha.io
- Email: support@sorcha.io

---

**Last Updated:** 2025-11-17
**Document Version:** 1.0.0
**Sprint:** 7
