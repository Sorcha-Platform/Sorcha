# Sorcha Blueprint Architecture: Integration with JSON-LD, JSON-e, and JSON Logic

## Table of Contents

1. [Overview](#overview)
2. [Current Blueprint Architecture](#current-blueprint-architecture)
3. [JSON-LD: Semantic Web Integration](#json-ld-semantic-web-integration)
4. [JSON-e: Dynamic Template Processing](#json-e-dynamic-template-processing)
5. [JSON Logic: Runtime Evaluation](#json-logic-runtime-evaluation)
6. [Simple Example: Loan Application](#simple-example-loan-application)
7. [Complex Example: Multi-Party Supply Chain](#complex-example-multi-party-supply-chain)
8. [Benefits and Restrictions](#benefits-and-restrictions)
9. [Implementation Recommendations](#implementation-recommendations)

---

## Overview

The Sorcha Blueprint system provides a declarative, schema-driven approach to defining multi-party workflows. Blueprints describe:

- **Participants**: Parties involved in the workflow (with DID/wallet support)
- **Actions**: Sequential steps in the workflow
- **Data Schemas**: JSON Schema definitions for data validation
- **Routing Logic**: Conditional participant routing using JSON Logic
- **Disclosures**: Fine-grained data access control using JSON Pointers
- **UI Forms**: Dynamic form generation from schema definitions

This document explores how blueprints currently work and how they can be enhanced with:

- **JSON-LD**: Linked data for semantic interoperability
- **JSON-e**: Template processing for dynamic blueprint generation
- **JSON Logic**: Runtime conditional logic (already implemented)

---

## Current Blueprint Architecture

### Core Blueprint Structure

A blueprint consists of:

```csharp
public class Blueprint
{
    public string Id { get; set; }                       // Unique identifier
    public string Title { get; set; }                    // Blueprint name
    public string Description { get; set; }              // Purpose description
    public int Version { get; set; }                     // Version number
    public List<JsonDocument>? DataSchemas { get; set; } // Embedded schemas
    public List<Participant> Participants { get; set; }  // Workflow parties (min 2)
    public List<Action> Actions { get; set; }            // Workflow steps (min 1)
    public Dictionary<string, string>? Metadata { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

### Workflow Actions

Actions represent steps in the workflow:

```csharp
public class Action
{
    public int Id { get; set; }                              // Sequence number (0-based)
    public string PreviousTxId { get; set; }                 // Blockchain tx reference
    public string BlueprintId { get; set; }                  // Parent blueprint
    public string Title { get; set; }                        // Action name
    public string Sender { get; set; }                       // Participant ID
    public IEnumerable<Condition>? Participants { get; set; } // Conditional routing
    public IEnumerable<string> RequiredActionData { get; set; }
    public IEnumerable<Disclosure> Disclosures { get; set; } // Data access control
    public JsonDocument? PreviousData { get; set; }          // Data from previous step
    public IEnumerable<JsonDocument>? DataSchemas { get; set; } // JSON Schemas
    public JsonNode? Condition { get; set; }                 // JSON Logic routing
    public Dictionary<string, JsonNode>? Calculations { get; set; } // JSON Logic calculations
    public Control? Form { get; set; }                       // UI form definition
}
```

### Transaction Chaining

Blueprints support blockchain-backed transaction chaining:

1. Each action corresponds to a blockchain transaction
2. `Action.PreviousTxId` links to the previous transaction
3. Data from previous actions flows forward via `PreviousData`
4. JSON Logic conditions determine the next participant based on transaction data

**Transaction Flow:**

```
[Action 0: Applicant Submits]
    ↓ (Transaction recorded, TxId: 0xabc123)
    ↓ (JSON Logic evaluates routing condition)
[Action 1: Reviewer Evaluates] (PreviousTxId: 0xabc123)
    ↓ (Transaction recorded, TxId: 0xdef456)
    ↓ (Conditional routing based on data)
[Action 2: Approver Decides] (PreviousTxId: 0xdef456)
```

### Current JSON Logic Usage

JSON Logic is **already actively used** in Sorcha blueprints for:

1. **Conditional Routing** (`Action.Condition`):
   ```json
   {
     "if": [
       {">": [{"var": "amount"}, 10000]},
       "senior-approver",
       {">": [{"var": "amount"}, 5000]},
       "approver",
       "auto-approve"
     ]
   }
   ```

2. **Calculations** (`Action.Calculations`):
   ```json
   {
     "totalPrice": {
       "*": [{"var": "quantity"}, {"var": "unitPrice"}]
     }
   }
   ```

3. **Conditional Display** (`Control.Conditions`):
   ```json
   {
     "==": [{"var": "status"}, "pending"]
   }
   ```

### Fluent Builder API

Blueprints are constructed using a fluent API:

```csharp
var blueprint = BlueprintBuilder.Create()
    .WithTitle("Purchase Order")
    .WithDescription("Two-party purchase workflow")
    .AddParticipant("buyer", p => p
        .Named("Buyer Corp")
        .WithWallet("0x1234..."))
    .AddParticipant("seller", p => p
        .Named("Seller LLC")
        .WithWallet("0x5678..."))
    .AddAction(0, a => a
        .WithTitle("Submit Order")
        .SentBy("buyer")
        .RequiresData(d => d
            .AddString("itemName", f => f.IsRequired())
            .AddInteger("quantity", f => f.WithMinimum(1).IsRequired()))
        .RouteToNext("seller"))
    .Build();
```

**Key Files:**
- Models: [Blueprint.cs](../src/Common/Sorcha.Blueprint.Models/Blueprint.cs)
- Builders: [BlueprintBuilder.cs](../src/Core/Sorcha.Blueprint.Fluent/BlueprintBuilder.cs)
- API: [Program.cs](../src/Apps/Services/Sorcha.Blueprint.Api/Program.cs)

---

## Blueprint Execution Engine Implementation

### The 6-Step Execution Pipeline

When a participant submits action data, the blueprint engine processes it through a comprehensive pipeline:

```
┌──────────────────────────────────────────────────────────────┐
│ 1. SUBMISSION: Participant submits action data              │
└─────────────────────┬────────────────────────────────────────┘
                      ↓
┌──────────────────────────────────────────────────────────────┐
│ 2. VALIDATION: Data validated against JSON Schema           │
│    Location: SchemaValidator.cs:712-745                     │
│    • Check required fields                                   │
│    • Validate data types                                     │
│    • Enforce constraints (min/max, patterns, etc.)          │
└─────────────────────┬────────────────────────────────────────┘
                      ↓
┌──────────────────────────────────────────────────────────────┐
│ 3. CALCULATIONS: JSON Logic derives new fields              │
│    Location: JsonLogicEvaluator.cs:89-132                   │
│    • totalAmount = qty × price                               │
│    • approvalLevel = if amount >= 5000...                    │
│    • tax = amount × 0.10                                     │
└─────────────────────┬────────────────────────────────────────┘
                      ↓
┌──────────────────────────────────────────────────────────────┐
│ 4. ROUTING: Conditions determine next participant           │
│    Location: RoutingEngine.cs:234-289                       │
│    • Evaluate routing conditions                             │
│    • Select next action                                      │
│    • Identify participant                                    │
└─────────────────────┬────────────────────────────────────────┘
                      ↓
┌──────────────────────────────────────────────────────────────┐
│ 5. DISCLOSURE: Data filtered for each participant          │
│    Location: DisclosureProcessor.cs:145-198                 │
│    • Filter fields per participant                           │
│    • Apply JSON Pointer rules                                │
│    • Create privacy-preserving views                         │
└─────────────────────┬────────────────────────────────────────┘
                      ↓
┌──────────────────────────────────────────────────────────────┐
│ 6. TRANSACTION: Signed and stored on distributed ledger    │
│    • Create cryptographic signature                          │
│    • Chain to previous transaction                           │
│    • Store on distributed ledger                             │
└──────────────────────────────────────────────────────────────┘
```

### Execution Context

The `ExecutionContext` is the data container that flows through the pipeline:

**File:** `src/Core/Sorcha.Blueprint.Engine/Models/ExecutionContext.cs`

```csharp
public class ExecutionContext
{
    public required Blueprint Blueprint { get; init; }        // Workflow definition
    public required Action Action { get; init; }              // Current step
    public required Dictionary<string, object> ActionData { get; init; }  // Submitted data

    public Dictionary<string, object>? PreviousData { get; init; }      // Prior action data
    public string? PreviousTransactionHash { get; init; }     // Transaction chain
    public string? InstanceId { get; init; }                  // Workflow instance ID

    public required string ParticipantId { get; init; }       // Actor ID
    public required string WalletAddress { get; init; }       // Signing address

    public ExecutionMode Mode { get; init; } = ExecutionMode.Full;
}
```

### Action Execution Orchestration

**ActionProcessor** coordinates the entire execution pipeline:

**File:** `src/Core/Sorcha.Blueprint.Engine/Implementation/ActionProcessor.cs` (Lines 89-178)

```csharp
public class ActionProcessor : IActionProcessor
{
    public async Task<ActionExecutionResult> ProcessAsync(
        ExecutionContext context,
        CancellationToken ct = default)
    {
        var result = new ActionExecutionResult();

        try
        {
            // Step 1: Validate
            if (context.Action.Form?.Schema != null)
            {
                result.Validation = await _schemaValidator.ValidateAsync(
                    context.ActionData,
                    context.Action.Form.Schema,
                    ct);

                if (!result.Validation.IsValid)
                {
                    result.Success = false;
                    result.Errors.Add("Validation failed");
                    return result;
                }
            }

            // Step 2: Calculate
            var processedData = new Dictionary<string, object>(context.ActionData);

            if (context.Action.Calculations?.Any() == true)
            {
                var calculations = context.Action.Calculations
                    .Select(kvp => Calculation.Create(kvp.Key, kvp.Value))
                    .ToList();

                processedData = await _jsonLogicEvaluator.ApplyCalculationsAsync(
                    processedData,
                    calculations,
                    ct);

                // Track calculated values
                foreach (var kvp in processedData)
                {
                    if (!context.ActionData.ContainsKey(kvp.Key))
                    {
                        result.CalculatedValues[kvp.Key] = kvp.Value;
                    }
                }
            }

            result.ProcessedData = processedData;

            // Step 3: Route
            result.Routing = await _routingEngine.DetermineNextAsync(
                context.Blueprint,
                context.Action,
                processedData,
                ct);

            // Step 4: Disclose
            if (context.Action.Disclosures?.Any() == true)
            {
                result.Disclosures = _disclosureProcessor.CreateDisclosures(
                    processedData,
                    context.Action.Disclosures);
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Error: {ex.Message}");
        }

        return result;
    }
}
```

### ActionExecutionResult

**File:** `src/Core/Sorcha.Blueprint.Engine/Models/ActionExecutionResult.cs`

```csharp
public class ActionExecutionResult
{
    public bool Success { get; set; }                          // Execution successful?
    public ValidationResult Validation { get; set; }           // Validation results
    public Dictionary<string, object> ProcessedData { get; set; }  // Original + calculated
    public Dictionary<string, object> CalculatedValues { get; set; }  // New fields only
    public RoutingResult Routing { get; set; }                 // Next step
    public List<DisclosureResult> Disclosures { get; set; }    // Filtered data per participant
    public List<string> Errors { get; set; }                   // Problems
    public List<string> Warnings { get; set; }                 // Warnings
}
```

### Schema Validation Implementation

**File:** `src/Core/Sorcha.Blueprint.Engine/Implementation/SchemaValidator.cs` (Lines 712-745)

```csharp
public class SchemaValidator : ISchemaValidator
{
    public async Task<ValidationResult> ValidateAsync(
        Dictionary<string, object> data,
        JsonNode schema,
        CancellationToken ct = default)
    {
        // Convert data to JsonNode
        var dataJson = ConvertToJsonNode(data);

        // Parse JSON Schema
        var jsonSchema = JsonSchema.FromText(schema.ToJsonString());

        // Convert to JsonElement for evaluation
        var jsonString = dataJson.ToJsonString();
        using var jsonDocument = JsonDocument.Parse(jsonString);
        var dataElement = jsonDocument.RootElement;

        // Validate with JSON Schema Draft 2020-12
        var validationResults = jsonSchema.Evaluate(dataElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true
        });

        return validationResults.IsValid
            ? ValidationResult.Valid()
            : ValidationResult.Invalid(ConvertErrors(validationResults));
    }
}
```

### JSON Logic Evaluator Implementation

**File:** `src/Core/Sorcha.Blueprint.Engine/Implementation/JsonLogicEvaluator.cs` (Lines 89-132)

```csharp
public class JsonLogicEvaluator : IJsonLogicEvaluator
{
    public async Task<Dictionary<string, object>> ApplyCalculationsAsync(
        Dictionary<string, object> data,
        IEnumerable<Calculation> calculations,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, object>(data);

        foreach (var calculation in calculations)
        {
            // Evaluate calculation expression against current data
            var value = Evaluate(calculation.Expression, result);

            // Add or update the output field
            result[calculation.OutputField] = value;
        }

        return await Task.FromResult(result);
    }
}
```

### Routing Engine Implementation

**File:** `src/Core/Sorcha.Blueprint.Engine/Implementation/RoutingEngine.cs` (Lines 234-289)

```csharp
public class RoutingEngine : IRoutingEngine
{
    public async Task<RoutingResult> DetermineNextAsync(
        Blueprint blueprint,
        Action currentAction,
        Dictionary<string, object> data,
        CancellationToken ct = default)
    {
        // Get routing conditions from action's Participants
        var conditions = currentAction.Participants?.ToList() ?? new List<Condition>();

        if (!conditions.Any())
        {
            return RoutingResult.Complete();  // No more participants
        }

        // Evaluate conditions to find next participant
        var nextParticipantId = await _evaluator.EvaluateConditionsAsync(data, conditions, ct);

        if (nextParticipantId == null)
        {
            return RoutingResult.Complete();  // Workflow complete
        }

        // Find next action for this participant
        var nextAction = FindNextActionForParticipant(blueprint, currentAction, nextParticipantId);

        return nextAction == null
            ? RoutingResult.Complete()
            : RoutingResult.Next(nextAction.Id.ToString(), nextParticipantId);
    }
}
```

### Disclosure Processing (Privacy-Preserving Data Filtering)

**File:** `src/Core/Sorcha.Blueprint.Engine/Implementation/DisclosureProcessor.cs` (Lines 145-198)

```csharp
public class DisclosureProcessor : IDisclosureProcessor
{
    public Dictionary<string, object> ApplyDisclosure(
        Dictionary<string, object> data,
        Disclosure disclosure)
    {
        var result = new Dictionary<string, object>();

        foreach (var pointer in disclosure.DataPointers)
        {
            if (pointer == "/*")  // All fields
            {
                foreach (var kvp in data)
                {
                    result[kvp.Key] = kvp.Value;
                }
                continue;
            }

            // Process JSON Pointer (e.g., "/fieldName")
            var fields = ExtractFieldsFromPointer(pointer, data);
            foreach (var field in fields)
            {
                result[field.Key] = field.Value;
            }
        }

        return result;
    }

    public List<DisclosureResult> CreateDisclosures(
        Dictionary<string, object> data,
        IEnumerable<Disclosure> disclosures)
    {
        var results = new List<DisclosureResult>();

        foreach (var disclosure in disclosures)
        {
            var disclosedData = ApplyDisclosure(data, disclosure);
            results.Add(DisclosureResult.Create(
                disclosure.ParticipantAddress,
                disclosedData
            ));
        }

        return results;
    }
}
```

### Concrete Example: Invoice Approval with Disclosure

**Blueprint Action 0: Submit Invoice**

```json
{
  "id": 0,
  "title": "Submit Invoice",
  "sender": "vendor",
  "disclosures": [
    {
      "participantAddress": "accounts-payable",
      "dataPointers": ["/*"]  // AP sees all fields
    }
  ]
}
```

**Vendor Submits:**
```json
{
  "invoiceNumber": "INV-001",
  "amount": 1500,
  "currency": "USD",
  "taxAmount": 150,
  "totalAmount": 1650
}
```

**Accounts Payable Sees (all fields via `/*`):**
```json
{
  "invoiceNumber": "INV-001",
  "amount": 1500,
  "currency": "USD",
  "taxAmount": 150,
  "totalAmount": 1650
}
```

**Blueprint Action 1: Approve Payment**

```json
{
  "id": 1,
  "title": "Approve Payment",
  "sender": "accounts-payable",
  "disclosures": [
    {
      "participantAddress": "vendor",
      "dataPointers": ["/approved", "/paymentDate", "/paymentMethod", "/notes"]
    }
  ]
}
```

**AP Approves and Submits:**
```json
{
  "approved": true,
  "paymentDate": "2025-02-15",
  "paymentMethod": "ACH",
  "notes": "Approved for payment",
  "internalNotes": "Vendor has good payment history",
  "budgetCode": "OP-2025-Q1"
}
```

**Vendor Sees (filtered to allowed fields only):**
```json
{
  "approved": true,
  "paymentDate": "2025-02-15",
  "paymentMethod": "ACH",
  "notes": "Approved for payment"
}
```

**Vendor Does NOT See:**
- `internalNotes` (internal AP comments)
- `budgetCode` (internal accounting)

This demonstrates **selective disclosure** - each participant sees only the data they're authorized to view, enabling privacy-preserving multi-party workflows.

### Data Accumulation: RequiredPriorActions

When a later action needs to reference data from earlier steps, use `requiredPriorActions`:

```json
{
  "id": 2,
  "title": "Review Complete Package",
  "requiredPriorActions": [0, 1],
  "description": "Review accumulated state from actions 0 and 1"
}
```

**Execution Context with Accumulated Data:**

```csharp
var context = new ExecutionContext
{
    Blueprint = blueprint,
    Action = action2,
    ActionData = currentActionSubmission,  // New data for action 2
    PreviousData = new Dictionary<string, object>
    {
        // Accumulated from action 0
        ["requisitionNumber"] = "REQ-2025-001",
        ["totalAmount"] = 6000,
        // Accumulated from action 1
        ["approved"] = true,
        ["approverComments"] = "Budget confirmed"
    },
    InstanceId = "workflow-instance-123",
    ParticipantId = "reviewer",
    WalletAddress = "0x..."
};
```

The `PreviousData` dictionary contains the accumulated state from all prior actions (0 and 1), allowing action 2 to access historical workflow data.

### Complete Workflow Execution Summary

```
Participant submits action data
    ↓
ExecutionContext created with:
  - Blueprint definition
  - Current action
  - Submitted data
  - Previous accumulated data
  - Participant identity
  - Wallet address
    ↓
ActionProcessor.ProcessAsync() called
    ↓
Step 1: SchemaValidator.ValidateAsync()
  - Validates data against JSON Schema
  - Returns ValidationResult
  - If fails: Short circuit, return error
    ↓
Step 2: JsonLogicEvaluator.ApplyCalculationsAsync()
  - Evaluates JSON Logic expressions
  - Derives new fields (totalAmount, approvalLevel, etc.)
  - Returns ProcessedData (original + calculated)
    ↓
Step 3: RoutingEngine.DetermineNextAsync()
  - Evaluates routing conditions
  - Determines next participant
  - Returns RoutingResult
    ↓
Step 4: DisclosureProcessor.CreateDisclosures()
  - Creates filtered data view for each participant
  - Uses JSON Pointers to extract allowed fields
  - Returns DisclosureResult list
    ↓
ActionExecutionResult returned with:
  - Success status
  - Validation results
  - ProcessedData (for storage)
  - CalculatedValues (for audit)
  - Routing decision
  - Disclosures (for each participant)
  - Errors/Warnings
    ↓
Blueprint Service creates transaction
  - Packages processed data + disclosures
  - Signs with participant wallet
  - Stores on distributed ledger
  - References previous transaction
    ↓
Workflow continues to next participant
OR
Workflow completes (if IsWorkflowComplete)
```

### Key Implementation Files

| Component | File Location | Lines | Purpose |
|-----------|---------------|-------|---------|
| **Blueprint Model** | `src/Common/Sorcha.Blueprint.Models/Blueprint.cs` | Full | Root workflow definition |
| **Action Model** | `src/Common/Sorcha.Blueprint.Models/Action.cs` | Full | Workflow step |
| **Execution Context** | `src/Core/Sorcha.Blueprint.Engine/Models/ExecutionContext.cs` | 23-47 | Data container for execution |
| **Execution Engine** | `src/Core/Sorcha.Blueprint.Engine/Implementation/ExecutionEngine.cs` | Full | Main entry point |
| **Action Processor** | `src/Core/Sorcha.Blueprint.Engine/Implementation/ActionProcessor.cs` | 89-178 | Orchestrates execution |
| **Schema Validator** | `src/Core/Sorcha.Blueprint.Engine/Implementation/SchemaValidator.cs` | 712-745 | JSON Schema validation |
| **JSON Logic Evaluator** | `src/Core/Sorcha.Blueprint.Engine/Implementation/JsonLogicEvaluator.cs` | 89-132 | Calculations & conditions |
| **JSON-e Evaluator** | `src/Core/Sorcha.Blueprint.Engine/Implementation/JsonEEvaluator.cs` | 67-89 | Template variable replacement |
| **Routing Engine** | `src/Core/Sorcha.Blueprint.Engine/Implementation/RoutingEngine.cs` | 234-289 | Next participant determination |
| **Disclosure Processor** | `src/Core/Sorcha.Blueprint.Engine/Implementation/DisclosureProcessor.cs` | 145-198 | Privacy-preserving data filtering |
| **Calculation Model** | `src/Core/Sorcha.Blueprint.Engine/Models/Calculation.cs` | Full | Field derivation definition |
| **RoutingResult** | `src/Core/Sorcha.Blueprint.Engine/Models/RoutingResult.cs` | Full | Next action/participant |
| **Disclosure Model** | `src/Common/Sorcha.Blueprint.Models/Disclosure.cs` | Full | Privacy rules |
| **Control Model** | `src/Common/Sorcha.Blueprint.Models/Control.cs` | Full | UI form definition |

---

## JSON-LD: Semantic Web Integration

### What is JSON-LD?

JSON-LD (JSON for Linked Data) adds semantic meaning to JSON documents by:

- Linking data to shared vocabularies (e.g., schema.org, W3C)
- Enabling data interoperability across systems
- Supporting decentralized identity (DIDs)
- Providing machine-readable context

### How Blueprints Can Use JSON-LD

#### 1. Blueprint Context

Add `@context` to blueprints for semantic interoperability:

```json
{
  "@context": {
    "@vocab": "https://sorcha.dev/blueprint/v1#",
    "schema": "https://schema.org/",
    "did": "https://www.w3.org/ns/did#",
    "xsd": "http://www.w3.org/2001/XMLSchema#",

    "id": "@id",
    "type": "@type",
    "Blueprint": "schema:WebApplication",
    "Participant": "schema:Person",
    "Action": "schema:Action",
    "title": "schema:name",
    "description": "schema:description",
    "createdAt": {
      "@id": "schema:dateCreated",
      "@type": "xsd:dateTime"
    },
    "didUri": {
      "@id": "did:Document",
      "@type": "@id"
    },
    "walletAddress": "did:walletAddress"
  },
  "id": "blueprint:abc-123",
  "type": "Blueprint",
  "title": "Loan Application",
  "version": 1,
  "participants": [...]
}
```

#### 2. Participant Identity with DIDs

Link participants to W3C Decentralized Identifiers:

```json
{
  "@context": "https://www.w3.org/ns/did/v1",
  "id": "did:example:123456789abcdefghi",
  "type": "Participant",
  "name": "Alice Smith",
  "organization": "Acme Corp",
  "walletAddress": "0x742d35Cc6634C0532925a3b844Bc9e7595f0bEb",
  "verifiableCredential": {
    "@context": "https://www.w3.org/2018/credentials/v1",
    "type": ["VerifiableCredential", "BusinessCredential"],
    "issuer": "did:example:issuer",
    "credentialSubject": {
      "id": "did:example:123456789abcdefghi",
      "businessRole": "Procurement Officer"
    }
  }
}
```

#### 3. Action as ActivityStreams

Model actions using ActivityStreams vocabulary:

```json
{
  "@context": "https://www.w3.org/ns/activitystreams",
  "type": "Action",
  "id": "action:submit-application",
  "summary": "Submit loan application",
  "actor": {
    "type": "Person",
    "id": "did:example:applicant",
    "name": "John Doe"
  },
  "object": {
    "type": "Document",
    "content": {
      "loanAmount": 50000,
      "purpose": "Business expansion"
    }
  },
  "target": {
    "type": "Person",
    "id": "did:example:loan-officer"
  },
  "published": "2025-01-15T10:00:00Z"
}
```

#### 4. Data Schema with Schema.org

Link data fields to schema.org types:

```json
{
  "@context": "https://schema.org/",
  "type": "LoanApplication",
  "applicant": {
    "type": "Person",
    "givenName": "John",
    "familyName": "Doe",
    "email": "john@example.com"
  },
  "loanAmount": {
    "type": "MonetaryAmount",
    "currency": "USD",
    "value": 50000
  },
  "purpose": {
    "type": "Text",
    "value": "Business expansion"
  }
}
```

### Benefits of JSON-LD in Blueprints

1. **Semantic Interoperability**: Different systems understand blueprint meaning
2. **Identity Federation**: DIDs enable cross-system participant recognition
3. **Data Discovery**: Linked data enables graph-based queries
4. **Standards Compliance**: W3C vocabularies for verifiable credentials
5. **API Evolution**: Add fields without breaking consumers (open world)

### Restrictions

1. **Complexity**: Additional overhead for context management
2. **Performance**: Larger payloads and processing time
3. **Learning Curve**: Teams need to understand linked data concepts
4. **Tooling**: Limited tooling compared to plain JSON
5. **Not Always Needed**: Simple internal workflows may not benefit

---

## JSON-e: Dynamic Template Processing

### What is JSON-e?

JSON-e is a templating language for JSON that supports:

- Variable substitution
- Conditional rendering
- Iteration over collections
- Expression evaluation
- Nested template composition

### How Blueprints Can Use JSON-e

#### 1. Parameterized Blueprint Templates

Create reusable blueprint templates:

```json
{
  "$eval": "blueprintTemplate",
  "context": {
    "blueprintTemplate": {
      "id": {"$eval": "blueprintId"},
      "title": {"$eval": "workflowName"},
      "version": 1,
      "participants": {
        "$map": {"$eval": "participantRoles"},
        "each(role)": {
          "id": {"$eval": "role.id"},
          "name": {"$eval": "role.name"},
          "organisation": {"$eval": "role.org"}
        }
      },
      "actions": {
        "$map": {"$eval": "workflowSteps"},
        "each(step, index)": {
          "id": {"$eval": "index"},
          "title": {"$eval": "step.title"},
          "sender": {"$eval": "step.senderId"},
          "dataSchemas": {"$eval": "step.schemas"}
        }
      }
    },
    "blueprintId": "loan-app-001",
    "workflowName": "Loan Application Process",
    "participantRoles": [
      {"id": "applicant", "name": "Loan Applicant", "org": "Self"},
      {"id": "officer", "name": "Loan Officer", "org": "Bank Corp"}
    ],
    "workflowSteps": [
      {"title": "Submit Application", "senderId": "applicant", "schemas": [...]},
      {"title": "Review Application", "senderId": "officer", "schemas": [...]}
    ]
  }
}
```

**Evaluates to:**

```json
{
  "id": "loan-app-001",
  "title": "Loan Application Process",
  "version": 1,
  "participants": [
    {"id": "applicant", "name": "Loan Applicant", "organisation": "Self"},
    {"id": "officer", "name": "Loan Officer", "organisation": "Bank Corp"}
  ],
  "actions": [
    {"id": 0, "title": "Submit Application", "sender": "applicant", "dataSchemas": [...]},
    {"id": 1, "title": "Review Application", "sender": "officer", "dataSchemas": [...]}
  ]
}
```

#### 2. Conditional Action Inclusion

Include actions based on configuration:

```json
{
  "actions": [
    {"id": 0, "title": "Submit Request", "sender": "requester"},
    {
      "$if": "requiresApproval",
      "then": {"id": 1, "title": "Manager Approval", "sender": "manager"},
      "else": {"$eval": "null"}
    },
    {
      "$if": "requiresFinanceReview && amount > 10000",
      "then": {"id": 2, "title": "Finance Review", "sender": "finance"},
      "else": {"$eval": "null"}
    },
    {"id": 3, "title": "Complete", "sender": "system"}
  ],
  "$let": {
    "requiresApproval": true,
    "requiresFinanceReview": true,
    "amount": 15000
  }
}
```

#### 3. Dynamic Schema Generation

Generate schemas based on workflow type:

```json
{
  "dataSchemas": [
    {
      "type": "object",
      "properties": {
        "$merge": [
          {
            "$eval": "baseFields"
          },
          {
            "$if": "workflowType == 'loan'",
            "then": {
              "loanAmount": {"type": "number", "minimum": 1000},
              "creditScore": {"type": "integer", "minimum": 300, "maximum": 850}
            }
          },
          {
            "$if": "workflowType == 'purchase'",
            "then": {
              "itemName": {"type": "string"},
              "quantity": {"type": "integer", "minimum": 1}
            }
          }
        ]
      }
    }
  ],
  "$let": {
    "workflowType": "loan",
    "baseFields": {
      "submittedAt": {"type": "string", "format": "date-time"},
      "notes": {"type": "string"}
    }
  }
}
```

#### 4. Environment-Specific Configuration

Adapt blueprints for different environments:

```json
{
  "participants": [
    {
      "id": "approver",
      "name": "Approver",
      "organisation": {"$eval": "environment == 'production' ? 'Real Bank Inc' : 'Test Bank LLC'"},
      "walletAddress": {"$eval": "environment == 'production' ? prodWallet : testWallet"}
    }
  ],
  "actions": [
    {
      "id": 0,
      "title": "Submit",
      "calculations": {
        "processingFee": {
          "$eval": "environment == 'production' ? amount * 0.02 : 0"
        }
      }
    }
  ],
  "$let": {
    "environment": "development",
    "prodWallet": "0x742d35Cc6634C0532925a3b844Bc9e7595f0bEb",
    "testWallet": "0x0000000000000000000000000000000000000000"
  }
}
```

### Benefits of JSON-e in Blueprints

1. **Reusability**: Single template generates multiple blueprint variants
2. **Maintainability**: Update template once, propagate to all instances
3. **Policy-Driven**: Business rules determine workflow structure
4. **Testing**: Easy environment-specific configurations
5. **Reduced Errors**: Consistent structure across similar workflows

### Restrictions

1. **Complexity**: Templates can become difficult to read
2. **Debugging**: Errors in templates are harder to trace
3. **Performance**: Runtime template evaluation overhead
4. **Learning Curve**: Developers need to learn JSON-e syntax
5. **Limited Tooling**: Less IDE support than plain JSON

---

## JSON Logic: Runtime Evaluation

### What is JSON Logic?

JSON Logic is a format for expressing logic rules in JSON that can be evaluated at runtime. It supports:

- Comparison operators: `==`, `!=`, `>`, `>=`, `<`, `<=`
- Logical operators: `and`, `or`, `!`, `!!`, `if`
- Arithmetic: `+`, `-`, `*`, `/`, `%`
- Data access: `var` (variable references)
- Array operations: `map`, `filter`, `reduce`, `all`, `some`, `in`
- String operations: `cat`, `substr`, `in` (substring check)

### Current JSON Logic Usage in Sorcha

JSON Logic is **actively used** in Sorcha blueprints through the fluent builder API:

#### 1. Conditional Routing

Route to different participants based on data:

```csharp
.RouteConditionally(c => c
    .When(jl => jl.GreaterThan("amount", 10000))
    .ThenRoute("director")
    .When(jl => jl.GreaterThan("amount", 5000))
    .ThenRoute("manager")
    .ElseRoute("auto-approve"))
```

**Generated JSON Logic:**

```json
{
  "if": [
    {">": [{"var": "amount"}, 10000]},
    "director",
    {">": [{"var": "amount"}, 5000]},
    "manager",
    "auto-approve"
  ]
}
```

#### 2. Field Calculations

Compute derived values:

```csharp
.Calculate("totalPrice", c => c
    .WithExpression(c.Multiply(
        c.Variable("quantity"),
        c.Variable("unitPrice"))))

.Calculate("discount", c => c
    .WithExpression(c.Multiply(
        c.Variable("totalPrice"),
        c.Constant(0.1))))
```

**Generated JSON Logic:**

```json
{
  "totalPrice": {
    "*": [{"var": "quantity"}, {"var": "unitPrice"}]
  },
  "discount": {
    "*": [{"var": "totalPrice"}, 0.1]
  }
}
```

#### 3. Complex Conditional Logic

Combine multiple conditions:

```csharp
.RouteConditionally(c => c
    .When(jl => jl.And(
        jl.GreaterThan("amount", 5000),
        jl.Equals("department", "finance")))
    .ThenRoute("cfo")
    .When(jl => jl.Or(
        jl.Equals("priority", "urgent"),
        jl.GreaterThan("amount", 10000)))
    .ThenRoute("director")
    .ElseRoute("manager"))
```

**Generated JSON Logic:**

```json
{
  "if": [
    {"and": [
      {">": [{"var": "amount"}, 5000]},
      {"==": [{"var": "department"}, "finance"]}
    ]},
    "cfo",
    {"or": [
      {"==": [{"var": "priority"}, "urgent"]},
      {">": [{"var": "amount"}, 10000]}
    ]},
    "director",
    "manager"
  ]
}
```

#### 4. Conditional Display Rules

Show/hide form fields based on data:

```csharp
.AddControl(ctrl => ctrl
    .OfType(ControlTypes.TextArea)
    .WithTitle("Rejection Reason")
    .BoundTo("/rejectionReason")
    .ShowWhen(jl => jl.Equals("status", "rejected")))
```

### JSON Logic Operators Reference

**Comparison:**
```json
{"==": [{"var": "status"}, "approved"]}
{"!=": [{"var": "status"}, "rejected"]}
{">": [{"var": "amount"}, 1000]}
{">=": [{"var": "age"}, 18]}
{"<": [{"var": "score"}, 50]}
{"<=": [{"var": "count"}, 100]}
```

**Logical:**
```json
{"and": [condition1, condition2, ...]}
{"or": [condition1, condition2, ...]}
{"!": condition}
{"!!": {"var": "optionalField"}}  // Truthy check
```

**Arithmetic:**
```json
{"+": [1, 2, 3]}               // 6
{"-": [10, 3]}                 // 7
{"*": [5, 4]}                  // 20
{"/": [20, 4]}                 // 5
{"%": [10, 3]}                 // 1 (modulo)
```

**Ternary (if-then-else):**
```json
{"if": [
  condition1, resultIfTrue1,
  condition2, resultIfTrue2,
  defaultResult
]}
```

**Array Operations:**
```json
{"map": [{"var": "items"}, {"*": [{"var": ""}, 2]}]}      // Double each item
{"filter": [{"var": "items"}, {">": [{"var": ""}, 10]}]}   // Items > 10
{"all": [{"var": "items"}, {">": [{"var": ""}, 0]}]}       // All positive
{"some": [{"var": "items"}, {"==": [{"var": ""}, 5]}]}     // Has value 5
{"in": [5, {"var": "items"}]}                              // Contains 5
```

**String Operations:**
```json
{"cat": ["Hello, ", {"var": "name"}]}           // Concatenate
{"substr": [{"var": "text"}, 0, 5]}             // Substring
{"in": ["foo", {"var": "text"}]}                // Contains substring
```

### Benefits of JSON Logic in Blueprints

1. **Declarative**: Logic expressed as data, not code
2. **Portable**: Same logic runs in frontend, backend, blockchain
3. **Sandboxed**: Cannot execute arbitrary code (secure)
4. **Serializable**: Store logic in database, send over network
5. **Dynamic**: Change routing logic without code deployment
6. **Auditable**: Logic is transparent and traceable

### Restrictions

1. **Limited Operations**: Cannot call external functions
2. **No Loops**: Limited iteration (map/reduce only)
3. **Performance**: Complex expressions can be slow
4. **Debugging**: Errors in expressions are hard to troubleshoot
5. **Type Safety**: No compile-time type checking
6. **Complexity Limits**: Very complex logic becomes unreadable

---

## Simple Example: Loan Application

This example demonstrates a basic two-party loan application workflow using JSON Schema, JSON Logic, and (optionally) JSON-LD.

### Scenario

1. **Applicant** submits a loan application with personal info and requested amount
2. **Loan Officer** reviews and either approves or rejects
3. Routing logic: amounts over $50,000 require senior approval

### Blueprint Definition (C# Fluent API)

```csharp
var loanBlueprint = BlueprintBuilder.Create()
    .WithTitle("Simple Loan Application")
    .WithDescription("Two-party loan application with conditional routing")
    .WithVersion(1)
    .WithMetadata("category", "finance")
    .WithMetadata("author", "Sorcha Team")

    // Participants
    .AddParticipant("applicant", p => p
        .Named("Loan Applicant")
        .FromOrganisation("Self")
        .WithDidUri("did:example:applicant-123"))

    .AddParticipant("loan-officer", p => p
        .Named("Loan Officer")
        .FromOrganisation("Community Bank")
        .WithWallet("0x742d35Cc6634C0532925a3b844Bc9e7595f0bEb")
        .WithDidUri("did:example:officer-456"))

    .AddParticipant("senior-officer", p => p
        .Named("Senior Loan Officer")
        .FromOrganisation("Community Bank")
        .WithWallet("0x1234567890abcdef1234567890abcdef12345678")
        .WithDidUri("did:example:senior-789"))

    // Action 0: Applicant submits application
    .AddAction(0, a => a
        .WithTitle("Submit Loan Application")
        .WithDescription("Applicant provides personal information and loan details")
        .SentBy("applicant")

        // Define required data with JSON Schema
        .RequiresData(d => d
            .AddString("firstName", f => f
                .WithTitle("First Name")
                .WithMinLength(2)
                .WithMaxLength(50)
                .IsRequired())

            .AddString("lastName", f => f
                .WithTitle("Last Name")
                .WithMinLength(2)
                .WithMaxLength(50)
                .IsRequired())

            .AddString("email", f => f
                .WithTitle("Email Address")
                .WithFormat("email")
                .IsRequired())

            .AddNumber("requestedAmount", f => f
                .WithTitle("Requested Loan Amount")
                .WithMinimum(1000)
                .WithMaximum(500000)
                .IsRequired())

            .AddString("purpose", f => f
                .WithTitle("Loan Purpose")
                .WithMinLength(10)
                .WithMaxLength(500)
                .IsRequired())

            .AddInteger("creditScore", f => f
                .WithTitle("Credit Score")
                .WithMinimum(300)
                .WithMaximum(850)))

        // Disclose all fields to loan officer
        .Disclose("loan-officer", d => d.AllFields())
        .Disclose("senior-officer", d => d.AllFields())

        // Calculate monthly payment estimate
        .Calculate("estimatedMonthlyPayment", c => c
            .WithExpression(c.Divide(
                c.Multiply(
                    c.Variable("requestedAmount"),
                    c.Constant(1.05)),  // 5% interest rough estimate
                c.Constant(60))))  // 5-year term

        // Conditional routing: amounts over $50,000 go to senior officer
        .RouteConditionally(c => c
            .When(jl => jl.GreaterThan("requestedAmount", 50000))
            .ThenRoute("senior-officer")
            .ElseRoute("loan-officer"))

        .WithForm(f => f
            .WithLayout(LayoutTypes.VerticalLayout)
            .WithTitle("Loan Application Form")
            .AddControl(ctrl => ctrl
                .OfType(ControlTypes.TextLine)
                .WithTitle("First Name")
                .BoundTo("/firstName"))
            .AddControl(ctrl => ctrl
                .OfType(ControlTypes.TextLine)
                .WithTitle("Last Name")
                .BoundTo("/lastName"))
            .AddControl(ctrl => ctrl
                .OfType(ControlTypes.TextLine)
                .WithTitle("Email")
                .BoundTo("/email"))
            .AddControl(ctrl => ctrl
                .OfType(ControlTypes.Numeric)
                .WithTitle("Requested Amount ($)")
                .BoundTo("/requestedAmount"))
            .AddControl(ctrl => ctrl
                .OfType(ControlTypes.TextArea)
                .WithTitle("Purpose of Loan")
                .BoundTo("/purpose"))
            .AddControl(ctrl => ctrl
                .OfType(ControlTypes.Numeric)
                .WithTitle("Credit Score (optional)")
                .BoundTo("/creditScore"))))

    // Action 1: Loan officer reviews
    .AddAction(1, a => a
        .WithTitle("Review Application")
        .WithDescription("Loan officer reviews and makes decision")
        .SentBy("loan-officer")

        .RequiresData(d => d
            .AddString("decision", f => f
                .WithTitle("Decision")
                .WithEnum(new[] { "approved", "rejected", "needs-info" })
                .IsRequired())

            .AddString("notes", f => f
                .WithTitle("Review Notes")
                .WithMaxLength(1000)))

        .Disclose("applicant", d => d
            .Field("/decision")
            .Field("/notes"))

        .WithForm(f => f
            .WithLayout(LayoutTypes.VerticalLayout)
            .WithTitle("Loan Review")
            .AddControl(ctrl => ctrl
                .OfType(ControlTypes.Label)
                .WithTitle("Applicant: {previousData.firstName} {previousData.lastName}"))
            .AddControl(ctrl => ctrl
                .OfType(ControlTypes.Label)
                .WithTitle("Amount: ${previousData.requestedAmount}"))
            .AddControl(ctrl => ctrl
                .OfType(ControlTypes.Selection)
                .WithTitle("Decision")
                .BoundTo("/decision"))
            .AddControl(ctrl => ctrl
                .OfType(ControlTypes.TextArea)
                .WithTitle("Notes")
                .BoundTo("/notes"))))

    .Build();
```

### Generated JSON (Simplified)

```json
{
  "id": "loan-app-simple-001",
  "title": "Simple Loan Application",
  "description": "Two-party loan application with conditional routing",
  "version": 1,
  "metadata": {
    "category": "finance",
    "author": "Sorcha Team"
  },
  "participants": [
    {
      "id": "applicant",
      "name": "Loan Applicant",
      "organisation": "Self",
      "didUri": "did:example:applicant-123"
    },
    {
      "id": "loan-officer",
      "name": "Loan Officer",
      "organisation": "Community Bank",
      "walletAddress": "0x742d35Cc6634C0532925a3b844Bc9e7595f0bEb",
      "didUri": "did:example:officer-456"
    },
    {
      "id": "senior-officer",
      "name": "Senior Loan Officer",
      "organisation": "Community Bank",
      "walletAddress": "0x1234567890abcdef1234567890abcdef12345678",
      "didUri": "did:example:senior-789"
    }
  ],
  "actions": [
    {
      "id": 0,
      "title": "Submit Loan Application",
      "sender": "applicant",
      "dataSchemas": [
        {
          "type": "object",
          "properties": {
            "firstName": {
              "type": "string",
              "title": "First Name",
              "minLength": 2,
              "maxLength": 50
            },
            "lastName": {
              "type": "string",
              "title": "Last Name",
              "minLength": 2,
              "maxLength": 50
            },
            "email": {
              "type": "string",
              "title": "Email Address",
              "format": "email"
            },
            "requestedAmount": {
              "type": "number",
              "title": "Requested Loan Amount",
              "minimum": 1000,
              "maximum": 500000
            },
            "purpose": {
              "type": "string",
              "title": "Loan Purpose",
              "minLength": 10,
              "maxLength": 500
            },
            "creditScore": {
              "type": "integer",
              "title": "Credit Score",
              "minimum": 300,
              "maximum": 850
            }
          },
          "required": ["firstName", "lastName", "email", "requestedAmount", "purpose"]
        }
      ],
      "disclosures": [
        {
          "participantAddress": "loan-officer",
          "dataPointers": ["/*"]
        },
        {
          "participantAddress": "senior-officer",
          "dataPointers": ["/*"]
        }
      ],
      "calculations": {
        "estimatedMonthlyPayment": {
          "/": [
            {"*": [{"var": "requestedAmount"}, 1.05]},
            60
          ]
        }
      },
      "condition": {
        "if": [
          {">": [{"var": "requestedAmount"}, 50000]},
          "senior-officer",
          "loan-officer"
        ]
      }
    },
    {
      "id": 1,
      "title": "Review Application",
      "sender": "loan-officer",
      "dataSchemas": [
        {
          "type": "object",
          "properties": {
            "decision": {
              "type": "string",
              "title": "Decision",
              "enum": ["approved", "rejected", "needs-info"]
            },
            "notes": {
              "type": "string",
              "title": "Review Notes",
              "maxLength": 1000
            }
          },
          "required": ["decision"]
        }
      ],
      "disclosures": [
        {
          "participantAddress": "applicant",
          "dataPointers": ["/decision", "/notes"]
        }
      ]
    }
  ]
}
```

### With JSON-LD Context

Add semantic meaning with JSON-LD:

```json
{
  "@context": {
    "@vocab": "https://sorcha.dev/blueprint/v1#",
    "schema": "https://schema.org/",
    "did": "https://www.w3.org/ns/did#",
    "xsd": "http://www.w3.org/2001/XMLSchema#",

    "id": "@id",
    "type": "@type",
    "title": "schema:name",
    "description": "schema:description",
    "participants": "schema:participant",
    "Participant": "schema:Person",
    "name": "schema:name",
    "organisation": "schema:affiliation",
    "didUri": {
      "@id": "did:Document",
      "@type": "@id"
    },
    "requestedAmount": {
      "@id": "schema:amount",
      "@type": "schema:MonetaryAmount"
    }
  },
  "id": "https://sorcha.dev/blueprints/loan-app-simple-001",
  "type": "Blueprint",
  "title": "Simple Loan Application",
  "participants": [
    {
      "id": "did:example:applicant-123",
      "type": "Participant",
      "name": "Loan Applicant"
    }
  ]
}
```

### Runtime Transaction Example

**Transaction 0 (Applicant submits):**

```json
{
  "transactionId": "0xabc123def456...",
  "blueprintId": "loan-app-simple-001",
  "actionId": 0,
  "sender": "did:example:applicant-123",
  "timestamp": "2025-01-15T10:30:00Z",
  "data": {
    "firstName": "John",
    "lastName": "Doe",
    "email": "john.doe@example.com",
    "requestedAmount": 75000,
    "purpose": "Home renovation and energy efficiency upgrades",
    "creditScore": 720
  },
  "calculatedValues": {
    "estimatedMonthlyPayment": 1312.50
  },
  "nextParticipant": "senior-officer"  // Routed to senior because amount > $50k
}
```

**JSON Logic Evaluation:**

```javascript
// Condition: {"if": [{">": [{"var": "requestedAmount"}, 50000]}, "senior-officer", "loan-officer"]}
// Data: {"requestedAmount": 75000}

// Step 1: Evaluate condition
{">": [{"var": "requestedAmount"}, 50000]}
// => {">": [75000, 50000]}
// => true

// Step 2: Select result
// If true: "senior-officer"
// Result: "senior-officer"
```

### Explanation

**JSON Schema:**
- Validates applicant input (email format, amount ranges, required fields)
- Provides field metadata (titles, constraints) for UI generation
- Ensures data quality before blockchain submission

**JSON Logic:**
- **Routing**: Automatically routes high-value loans to senior officer
- **Calculation**: Estimates monthly payment for applicant information
- Evaluated at runtime based on submitted data

**Disclosures:**
- Loan officers see all applicant data
- Applicant only sees decision and notes (privacy)

**Transaction Chaining:**
- Action 1 receives `previousTxId` and `previousData` from Action 0
- Blockchain ensures immutability and auditability

**Benefits:**
- **Declarative**: Entire workflow defined as data
- **Dynamic**: Routing changes based on data without code changes
- **Auditable**: All logic and data on blockchain
- **Portable**: Same definition works across environments

---

## Complex Example: Multi-Party Supply Chain

This example demonstrates a sophisticated multi-party supply chain workflow using JSON Schema, JSON-LD for semantic interoperability, JSON-e for dynamic configuration, and JSON Logic for complex routing.

### Scenario

A purchase order workflow involving:
1. **Buyer** creates purchase order
2. **Seller** accepts or rejects order
3. **Logistics** arranges shipping (conditional based on order value)
4. **Finance** processes payment (conditional based on amount and credit terms)
5. **Quality** inspects goods (conditional based on product category)

### Blueprint with JSON-e Template

First, define a parameterized template:

```json
{
  "$eval": "blueprintTemplate",
  "context": {
    "blueprintId": "supply-chain-po-001",
    "requiresLogistics": true,
    "requiresQualityInspection": true,
    "autoApproveThreshold": 5000,
    "qualityInspectionCategories": ["electronics", "medical"],

    "blueprintTemplate": {
      "@context": {
        "@vocab": "https://sorcha.dev/blueprint/v1#",
        "schema": "https://schema.org/",
        "gs1": "https://gs1.org/voc/",
        "did": "https://www.w3.org/ns/did#",

        "id": "@id",
        "type": "@type",
        "Order": "schema:Order",
        "Product": "schema:Product",
        "Organization": "schema:Organization",
        "MonetaryAmount": "schema:MonetaryAmount"
      },

      "id": {"$eval": "blueprintId"},
      "type": "Blueprint",
      "title": "Supply Chain Purchase Order",
      "description": "Multi-party purchase order workflow with conditional routing",
      "version": 1,
      "metadata": {
        "category": "supply-chain",
        "industry": "manufacturing",
        "author": "Sorcha Team"
      },

      "participants": [
        {
          "id": "buyer",
          "type": "Organization",
          "name": "Acme Manufacturing",
          "organisation": "Acme Corp",
          "walletAddress": "0x742d35Cc6634C0532925a3b844Bc9e7595f0bEb",
          "didUri": "did:example:acme-buyer",
          "useStealthAddress": false
        },
        {
          "id": "seller",
          "type": "Organization",
          "name": "Global Supplies Inc",
          "organisation": "Global Supplies",
          "walletAddress": "0x1234567890abcdef1234567890abcdef12345678",
          "didUri": "did:example:global-seller"
        },
        {
          "$if": "requiresLogistics",
          "then": {
            "id": "logistics",
            "type": "Organization",
            "name": "FastShip Logistics",
            "organisation": "FastShip",
            "walletAddress": "0xabcdefabcdefabcdefabcdefabcdefabcdefabcd",
            "didUri": "did:example:fastship"
          }
        },
        {
          "id": "finance",
          "type": "Organization",
          "name": "Finance Department",
          "organisation": "Acme Corp",
          "walletAddress": "0x9876543210fedcba9876543210fedcba98765432",
          "didUri": "did:example:acme-finance"
        },
        {
          "$if": "requiresQualityInspection",
          "then": {
            "id": "quality",
            "type": "Organization",
            "name": "Quality Assurance",
            "organisation": "Acme Corp",
            "walletAddress": "0x1111222233334444555566667777888899990000",
            "didUri": "did:example:acme-qa"
          }
        }
      ],

      "actions": [
        {
          "id": 0,
          "type": "OrderAction",
          "title": "Create Purchase Order",
          "description": "Buyer creates and submits purchase order",
          "sender": "buyer",

          "dataSchemas": [
            {
              "@context": {
                "schema": "https://schema.org/",
                "gs1": "https://gs1.org/voc/"
              },
              "type": "object",
              "properties": {
                "orderNumber": {
                  "type": "string",
                  "title": "Order Number",
                  "pattern": "^PO-[0-9]{6}$",
                  "@id": "schema:orderNumber"
                },
                "items": {
                  "type": "array",
                  "title": "Order Items",
                  "minItems": 1,
                  "items": {
                    "type": "object",
                    "properties": {
                      "productId": {
                        "type": "string",
                        "title": "Product ID",
                        "@id": "gs1:gtin"
                      },
                      "productName": {
                        "type": "string",
                        "title": "Product Name",
                        "@id": "schema:name"
                      },
                      "category": {
                        "type": "string",
                        "title": "Category",
                        "enum": ["electronics", "machinery", "raw-materials", "medical", "office"]
                      },
                      "quantity": {
                        "type": "integer",
                        "title": "Quantity",
                        "minimum": 1,
                        "@id": "schema:orderQuantity"
                      },
                      "unitPrice": {
                        "type": "number",
                        "title": "Unit Price",
                        "minimum": 0,
                        "@id": "schema:price"
                      }
                    },
                    "required": ["productId", "productName", "category", "quantity", "unitPrice"]
                  }
                },
                "deliveryAddress": {
                  "type": "object",
                  "title": "Delivery Address",
                  "@id": "schema:deliveryAddress",
                  "properties": {
                    "street": {"type": "string"},
                    "city": {"type": "string"},
                    "state": {"type": "string"},
                    "postalCode": {"type": "string"},
                    "country": {"type": "string"}
                  },
                  "required": ["street", "city", "country"]
                },
                "requestedDeliveryDate": {
                  "type": "string",
                  "format": "date",
                  "title": "Requested Delivery Date",
                  "@id": "schema:expectedDeliveryDate"
                },
                "paymentTerms": {
                  "type": "string",
                  "title": "Payment Terms",
                  "enum": ["net-30", "net-60", "net-90", "immediate"],
                  "@id": "schema:paymentDue"
                }
              },
              "required": ["orderNumber", "items", "deliveryAddress", "requestedDeliveryDate", "paymentTerms"]
            }
          ],

          "disclosures": [
            {"participantAddress": "seller", "dataPointers": ["/*"]},
            {"participantAddress": "finance", "dataPointers": ["/orderNumber", "/items", "/paymentTerms", "/totalAmount"]},
            {
              "$if": "requiresLogistics",
              "then": {
                "participantAddress": "logistics",
                "dataPointers": ["/orderNumber", "/items/*/productName", "/items/*/quantity", "/deliveryAddress"]
              }
            },
            {
              "$if": "requiresQualityInspection",
              "then": {
                "participantAddress": "quality",
                "dataPointers": ["/orderNumber", "/items"]
              }
            }
          ],

          "calculations": {
            "itemTotals": {
              "map": [
                {"var": "items"},
                {"*": [{"var": "quantity"}, {"var": "unitPrice"}]}
              ]
            },
            "totalAmount": {
              "reduce": [
                {"var": "itemTotals"},
                {"+": [{"var": "accumulator"}, {"var": "current"}]},
                0
              ]
            },
            "requiresFinanceApproval": {
              "or": [
                {">": [{"var": "totalAmount"}, {"$eval": "autoApproveThreshold"}]},
                {"in": [{"var": "paymentTerms"}, ["net-60", "net-90"]]}
              ]
            },
            "requiresQualityCheck": {
              "$if": "requiresQualityInspection",
              "then": {
                "some": [
                  {"var": "items"},
                  {"in": [{"var": "category"}, {"$eval": "qualityInspectionCategories"}]}
                ]
              },
              "else": false
            }
          },

          "condition": {
            "if": [true, "seller"]
          }
        },

        {
          "id": 1,
          "type": "AcceptAction",
          "title": "Accept or Reject Order",
          "description": "Seller reviews and accepts/rejects the order",
          "sender": "seller",

          "dataSchemas": [
            {
              "type": "object",
              "properties": {
                "decision": {
                  "type": "string",
                  "title": "Decision",
                  "enum": ["accepted", "rejected", "counter-offer"]
                },
                "estimatedShipDate": {
                  "type": "string",
                  "format": "date",
                  "title": "Estimated Ship Date"
                },
                "notes": {
                  "type": "string",
                  "title": "Notes",
                  "maxLength": 500
                }
              },
              "required": ["decision"]
            }
          ],

          "disclosures": [
            {"participantAddress": "buyer", "dataPointers": ["/*"]},
            {
              "$if": "requiresLogistics",
              "then": {"participantAddress": "logistics", "dataPointers": ["/estimatedShipDate"]}
            }
          ],

          "condition": {
            "if": [
              {"==": [{"var": "decision"}, "rejected"]},
              null,

              {"and": [
                {"$eval": "requiresLogistics"},
                {"==": [{"var": "decision"}, "accepted"]}
              ]},
              "logistics",

              {"var": "previousData.requiresFinanceApproval"},
              "finance",

              "buyer"
            ]
          }
        },

        {
          "$if": "requiresLogistics",
          "then": {
            "id": 2,
            "type": "ShipAction",
            "title": "Arrange Shipping",
            "description": "Logistics provider arranges shipping",
            "sender": "logistics",

            "dataSchemas": [
              {
                "type": "object",
                "properties": {
                  "carrier": {
                    "type": "string",
                    "title": "Carrier"
                  },
                  "trackingNumber": {
                    "type": "string",
                    "title": "Tracking Number",
                    "@id": "gs1:trackingNumber"
                  },
                  "estimatedDelivery": {
                    "type": "string",
                    "format": "date",
                    "title": "Estimated Delivery"
                  }
                },
                "required": ["carrier", "trackingNumber", "estimatedDelivery"]
              }
            ],

            "disclosures": [
              {"participantAddress": "buyer", "dataPointers": ["/*"]},
              {"participantAddress": "seller", "dataPointers": ["/*"]}
            ],

            "condition": {
              "if": [
                {"var": "previousData.requiresQualityCheck"},
                "quality",

                {"var": "previousData.requiresFinanceApproval"},
                "finance",

                "buyer"
              ]
            }
          }
        },

        {
          "$if": "requiresQualityInspection",
          "then": {
            "id": 3,
            "type": "InspectAction",
            "title": "Quality Inspection",
            "description": "QA inspects received goods",
            "sender": "quality",

            "dataSchemas": [
              {
                "type": "object",
                "properties": {
                  "inspectionResult": {
                    "type": "string",
                    "title": "Inspection Result",
                    "enum": ["passed", "failed", "conditional-pass"]
                  },
                  "defects": {
                    "type": "array",
                    "title": "Defects Found",
                    "items": {
                      "type": "object",
                      "properties": {
                        "productId": {"type": "string"},
                        "defectDescription": {"type": "string"}
                      }
                    }
                  },
                  "inspectorNotes": {
                    "type": "string",
                    "title": "Inspector Notes"
                  }
                },
                "required": ["inspectionResult"]
              }
            ],

            "disclosures": [
              {"participantAddress": "buyer", "dataPointers": ["/*"]},
              {"participantAddress": "seller", "dataPointers": ["/*"]}
            ],

            "condition": {
              "if": [
                {"var": "previousData.requiresFinanceApproval"},
                "finance",
                "buyer"
              ]
            }
          }
        },

        {
          "id": 4,
          "type": "PaymentAction",
          "title": "Process Payment",
          "description": "Finance processes payment",
          "sender": "finance",

          "dataSchemas": [
            {
              "type": "object",
              "properties": {
                "paymentMethod": {
                  "type": "string",
                  "title": "Payment Method",
                  "enum": ["wire-transfer", "ach", "check", "crypto"]
                },
                "transactionId": {
                  "type": "string",
                  "title": "Transaction ID"
                },
                "paymentDate": {
                  "type": "string",
                  "format": "date",
                  "title": "Payment Date"
                },
                "amount": {
                  "type": "number",
                  "title": "Amount Paid",
                  "@id": "schema:totalPaymentDue"
                }
              },
              "required": ["paymentMethod", "transactionId", "paymentDate", "amount"]
            }
          ],

          "calculations": {
            "paymentMatchesOrder": {
              "==": [
                {"var": "amount"},
                {"var": "previousData.totalAmount"}
              ]
            }
          },

          "disclosures": [
            {"participantAddress": "buyer", "dataPointers": ["/*"]},
            {"participantAddress": "seller", "dataPointers": ["/*"]}
          ]
        }
      ]
    }
  }
}
```

### Evaluated Blueprint (JSON-e Output)

After JSON-e evaluation with the provided context:

```json
{
  "@context": {
    "@vocab": "https://sorcha.dev/blueprint/v1#",
    "schema": "https://schema.org/",
    "gs1": "https://gs1.org/voc/",
    "did": "https://www.w3.org/ns/did#",
    "id": "@id",
    "type": "@type",
    "Order": "schema:Order",
    "Product": "schema:Product",
    "Organization": "schema:Organization"
  },

  "id": "supply-chain-po-001",
  "type": "Blueprint",
  "title": "Supply Chain Purchase Order",
  "version": 1,

  "participants": [
    {
      "id": "buyer",
      "type": "Organization",
      "name": "Acme Manufacturing",
      "didUri": "did:example:acme-buyer"
    },
    {
      "id": "seller",
      "type": "Organization",
      "name": "Global Supplies Inc",
      "didUri": "did:example:global-seller"
    },
    {
      "id": "logistics",
      "type": "Organization",
      "name": "FastShip Logistics",
      "didUri": "did:example:fastship"
    },
    {
      "id": "finance",
      "type": "Organization",
      "name": "Finance Department",
      "didUri": "did:example:acme-finance"
    },
    {
      "id": "quality",
      "type": "Organization",
      "name": "Quality Assurance",
      "didUri": "did:example:acme-qa"
    }
  ],

  "actions": [
    {
      "id": 0,
      "title": "Create Purchase Order",
      "sender": "buyer",
      "dataSchemas": [ /* JSON Schema with JSON-LD context */ ],
      "calculations": {
        "totalAmount": {
          "reduce": [
            {"map": [{"var": "items"}, {"*": [{"var": "quantity"}, {"var": "unitPrice"}]}]},
            {"+": [{"var": "accumulator"}, {"var": "current"}]},
            0
          ]
        },
        "requiresFinanceApproval": {
          "or": [
            {">": [{"var": "totalAmount"}, 5000]},
            {"in": [{"var": "paymentTerms"}, ["net-60", "net-90"]]}
          ]
        },
        "requiresQualityCheck": {
          "some": [
            {"var": "items"},
            {"in": [{"var": "category"}, ["electronics", "medical"]]}
          ]
        }
      }
    },
    /* Additional actions... */
  ]
}
```

### Runtime Transaction Example

**Transaction 0 (Buyer creates order):**

```json
{
  "@context": "https://sorcha.dev/blueprint/v1#",
  "transactionId": "0xabc123def456789...",
  "blueprintId": "supply-chain-po-001",
  "actionId": 0,
  "sender": "did:example:acme-buyer",
  "timestamp": "2025-01-15T09:00:00Z",

  "data": {
    "orderNumber": "PO-000123",
    "items": [
      {
        "productId": "GTIN-12345",
        "productName": "Industrial Sensor Model X200",
        "category": "electronics",
        "quantity": 50,
        "unitPrice": 125.00
      },
      {
        "productId": "GTIN-67890",
        "productName": "Mounting Bracket Type B",
        "category": "machinery",
        "quantity": 100,
        "unitPrice": 15.00
      }
    ],
    "deliveryAddress": {
      "street": "123 Industrial Pkwy",
      "city": "Detroit",
      "state": "MI",
      "postalCode": "48201",
      "country": "USA"
    },
    "requestedDeliveryDate": "2025-02-15",
    "paymentTerms": "net-30"
  },

  "calculatedValues": {
    "itemTotals": [6250.00, 1500.00],
    "totalAmount": 7750.00,
    "requiresFinanceApproval": true,  // Amount > 5000
    "requiresQualityCheck": true      // Has electronics category
  },

  "nextParticipant": "seller"
}
```

**JSON Logic Evaluation - Finance Approval Check:**

```javascript
// Expression:
{
  "or": [
    {">": [{"var": "totalAmount"}, 5000]},
    {"in": [{"var": "paymentTerms"}, ["net-60", "net-90"]]}
  ]
}

// Data: {"totalAmount": 7750.00, "paymentTerms": "net-30"}

// Evaluation:
// Condition 1: 7750.00 > 5000 => true
// Condition 2: "net-30" in ["net-60", "net-90"] => false
// OR: true || false => true
// Result: requiresFinanceApproval = true
```

**JSON Logic Evaluation - Quality Check:**

```javascript
// Expression:
{
  "some": [
    {"var": "items"},
    {"in": [{"var": "category"}, ["electronics", "medical"]]}
  ]
}

// Data: {"items": [{category: "electronics"}, {category: "machinery"}]}

// Evaluation:
// Check each item:
//   Item 0: "electronics" in ["electronics", "medical"] => true
//   (some() returns true on first match)
// Result: requiresQualityCheck = true
```

**Transaction 1 (Seller accepts):**

```json
{
  "transactionId": "0x789def012abc345...",
  "blueprintId": "supply-chain-po-001",
  "actionId": 1,
  "previousTxId": "0xabc123def456789...",
  "sender": "did:example:global-seller",
  "timestamp": "2025-01-15T11:30:00Z",

  "data": {
    "decision": "accepted",
    "estimatedShipDate": "2025-01-20",
    "notes": "Order confirmed. Will ship via standard freight."
  },

  "previousData": {
    "totalAmount": 7750.00,
    "requiresFinanceApproval": true,
    "requiresQualityCheck": true
  },

  "nextParticipant": "logistics"  // Routed by condition
}
```

**Routing Logic Evaluation:**

```javascript
// Condition from Action 1:
{
  "if": [
    {"==": [{"var": "decision"}, "rejected"]},
    null,  // End workflow

    {"and": [
      true,  // requiresLogistics from template
      {"==": [{"var": "decision"}, "accepted"]}
    ]},
    "logistics",

    {"var": "previousData.requiresFinanceApproval"},
    "finance",

    "buyer"  // Default
  ]
}

// Data: {"decision": "accepted", "previousData": {"requiresFinanceApproval": true}}

// Evaluation:
// Check 1: "accepted" == "rejected" => false, skip
// Check 2: true && ("accepted" == "accepted") => true && true => true
// Result: "logistics"
```

### Explanation

#### JSON-LD Integration

**Semantic Context:**
- `@context` maps blueprint fields to standard vocabularies (schema.org, GS1)
- Product IDs use GS1 GTIN standard
- Addresses use schema.org vocabulary
- Participants identified by DIDs (decentralized identifiers)

**Benefits:**
- External systems can understand blueprint data without custom documentation
- GS1 product identifiers enable supply chain interoperability
- DIDs enable cross-organizational identity federation
- RDF triples can be extracted for graph queries

**Example RDF Triple:**
```turtle
<did:example:acme-buyer> schema:participant <https://sorcha.dev/blueprints/supply-chain-po-001> .
<GTIN-12345> schema:name "Industrial Sensor Model X200" .
<GTIN-12345> schema:price "125.00"^^xsd:decimal .
```

#### JSON-e Dynamic Configuration

**Template Variables:**
- `requiresLogistics`: Toggles logistics participant and action
- `requiresQualityInspection`: Toggles QA participant and action
- `autoApproveThreshold`: Sets approval limit
- `qualityInspectionCategories`: Defines which products need inspection

**Benefits:**
- Single template generates multiple workflow variants
- Configure workflow behavior without editing code
- Environment-specific settings (dev/staging/prod)
- A/B testing different approval thresholds

**Example Variants:**
```javascript
// Small orders: no logistics, no QA
{"requiresLogistics": false, "requiresQualityInspection": false}

// High-value medical: all checks enabled
{"autoApproveThreshold": 1000, "qualityInspectionCategories": ["medical", "electronics", "machinery"]}
```

#### JSON Logic Complex Routing

**Multi-Condition Routing:**
- Rejected orders end workflow immediately
- Accepted orders route to logistics (if enabled)
- High-value or extended-term orders require finance approval
- Electronics/medical items trigger quality inspection

**Calculated Fields:**
- `totalAmount`: Sum of all line items
- `requiresFinanceApproval`: Boolean based on amount and payment terms
- `requiresQualityCheck`: Boolean based on product categories

**Cascading Logic:**
- Each action's routing depends on calculations from previous actions
- `previousData` carries forward calculated flags
- Enables complex multi-step decision trees

### Benefits and Trade-offs

#### Combined Benefits

**1. Flexibility:**
- JSON-e templates adapt workflows to different scenarios
- JSON Logic enables data-driven routing without code deployment
- JSON-LD enables semantic interoperability

**2. Auditability:**
- All logic expressed as data (stored on blockchain)
- Transparent decision-making process
- Reproducible workflow execution

**3. Interoperability:**
- JSON-LD enables cross-system understanding
- Standard vocabularies (schema.org, GS1) reduce integration effort
- DIDs enable federated identity

**4. Maintainability:**
- Change routing logic without code changes
- Update templates to create new workflow variants
- Business users can understand JSON Logic expressions

#### Restrictions and Challenges

**1. Complexity:**
- Learning curve for JSON-e, JSON Logic, JSON-LD
- Templates can become difficult to read
- Debugging nested expressions is challenging

**2. Performance:**
- JSON-e evaluation adds runtime overhead
- Complex JSON Logic expressions slow execution
- JSON-LD processing increases payload size

**3. Tooling:**
- Limited IDE support for JSON-e templates
- No compile-time type checking
- Error messages can be cryptic

**4. Expressiveness Limits:**
- JSON Logic cannot call external functions
- No support for loops (only map/reduce)
- Complex business rules may be difficult to express

**5. Testing:**
- Unit testing JSON Logic expressions requires specialized tools
- Integration testing templates is complex
- Mocking previousData for testing is cumbersome

---

## Benefits and Restrictions

### Overall Benefits

#### 1. Declarative Workflow Definition
- Entire workflow defined as data (JSON)
- No code deployment required for changes
- Version control friendly (git diff shows logical changes)

#### 2. Portability
- Same blueprint runs on any Sorcha instance
- Client-side validation matches server-side
- Blockchain-backed immutability

#### 3. Auditability
- All workflow logic stored on blockchain
- Transparent decision-making
- Compliance-friendly (regulatory requirements)

#### 4. Dynamic Routing
- JSON Logic enables data-driven participant selection
- No hardcoded workflow paths
- Adapts to business rules at runtime

#### 5. Semantic Interoperability (JSON-LD)
- Standard vocabularies reduce integration effort
- Machine-readable data semantics
- Cross-organizational data exchange

#### 6. Template Reusability (JSON-e)
- Single template generates multiple workflows
- Policy-driven workflow instantiation
- Environment-specific configuration

### Overall Restrictions

#### 1. Complexity Trade-off
- Additional abstractions increase learning curve
- Templates and expressions can become unreadable
- Requires training for non-technical stakeholders

#### 2. Performance Overhead
- Runtime evaluation slower than compiled code
- Large templates increase payload size
- JSON-LD context resolution adds latency

#### 3. Debugging Difficulty
- Errors in JSON Logic expressions are hard to trace
- No IDE support for JSON-e templates
- Stack traces don't show expression location

#### 4. Limited Expressiveness
- JSON Logic cannot call external APIs
- No support for complex algorithms
- Recursive logic is difficult to express

#### 5. Tooling Gaps
- Limited editor support for JSON-e
- No visual debugger for JSON Logic
- Type checking only at runtime

#### 6. Testing Challenges
- Unit testing expressions requires specialized tools
- Mocking data for integration tests is complex
- No standard test framework

---

## Implementation Recommendations

### 1. JSON-LD Integration

**Immediate Steps:**
1. Add `@context` field to Blueprint model
2. Support content negotiation (`application/ld+json`)
3. Map core fields to schema.org vocabulary
4. Use W3C DID spec for participant identifiers

**Implementation:**

```csharp
// Blueprint.cs
public class Blueprint
{
    [JsonPropertyName("@context")]
    public JsonNode? JsonLdContext { get; set; }

    // ... existing properties
}

// Default context
public static class BlueprintJsonLdContext
{
    public static readonly JsonNode Default = JsonNode.Parse(@"
    {
      ""@vocab"": ""https://sorcha.dev/blueprint/v1#"",
      ""schema"": ""https://schema.org/"",
      ""did"": ""https://www.w3.org/ns/did#"",
      ""id"": ""@id"",
      ""type"": ""@type"",
      ""title"": ""schema:name"",
      ""description"": ""schema:description""
    }");
}
```

**Benefits:**
- Semantic interoperability with external systems
- Standards-based identity (DIDs)
- Graph-based querying (SPARQL)

**When to Use:**
- Cross-organizational workflows
- Supply chain integration
- Regulatory compliance (verifiable credentials)

**When to Skip:**
- Internal-only workflows
- Simple two-party exchanges
- Performance-critical applications

### 2. JSON-e Template Support

**Immediate Steps:**
1. Add JSON-e evaluation library (e.g., `json-e` npm package or C# port)
2. Create `BlueprintTemplate` model separate from `Blueprint`
3. Add template evaluation endpoint
4. Support template parameter validation

**Implementation:**

```csharp
// BlueprintTemplate.cs
public class BlueprintTemplate
{
    public string Id { get; set; }
    public string Title { get; set; }
    public JsonNode Template { get; set; }  // JSON-e template
    public JsonSchema ParameterSchema { get; set; }  // Expected parameters
    public Dictionary<string, object> DefaultParameters { get; set; }
}

// Template evaluation service
public class BlueprintTemplateService
{
    public async Task<Blueprint> EvaluateAsync(
        string templateId,
        Dictionary<string, object> parameters)
    {
        var template = await _templateStore.GetAsync(templateId);

        // Validate parameters against schema
        var validationResult = ValidateParameters(template.ParameterSchema, parameters);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        // Merge with defaults
        var context = MergeParameters(template.DefaultParameters, parameters);

        // Evaluate JSON-e template
        var evaluatedJson = JsonE.Evaluate(template.Template, context);

        // Deserialize to Blueprint
        return JsonSerializer.Deserialize<Blueprint>(evaluatedJson);
    }
}

// API endpoint
app.MapPost("/api/blueprints/from-template", async (
    [FromBody] EvaluateTemplateRequest request,
    BlueprintTemplateService templateService) =>
{
    var blueprint = await templateService.EvaluateAsync(
        request.TemplateId,
        request.Parameters);

    return Results.Ok(blueprint);
});
```

**Benefits:**
- Reusable workflow patterns
- Policy-driven instantiation
- Reduced duplication

**When to Use:**
- Multiple similar workflows (e.g., different loan types)
- Environment-specific configuration
- A/B testing workflow variations

**When to Skip:**
- Unique one-off workflows
- Workflows that rarely change
- Teams unfamiliar with templating

### 3. Enhanced JSON Logic Support

**Current State:** Already well-integrated via fluent builders

**Enhancements:**
1. Add visual expression builder to Blueprint Designer UI
2. Create expression library/snippets
3. Add expression validation and testing tools
4. Implement performance optimization (expression caching)

**Implementation:**

```csharp
// Expression library
public static class JsonLogicExpressions
{
    public static readonly JsonNode ApprovalByAmount = JsonNode.Parse(@"
    {
      ""if"": [
        {"">"""": [{""var"": ""amount""}, 100000]}, ""ceo"",
        {"">"""": [{""var"": ""amount""}, 50000]}, ""director"",
        {"">"""": [{""var"": ""amount""}, 10000]}, ""manager"",
        ""auto-approve""
      ]
    }");

    public static readonly JsonNode HighRiskCheck = JsonNode.Parse(@"
    {
      ""or"": [
        {""=="": [{""var"": ""riskLevel""}, ""high""]},
        {"">"""": [{""var"": ""amount""}, 50000]},
        {""in"": [{""var"": ""country""}, [""high-risk-country-1"", ""high-risk-country-2""]]}
      ]
    }");
}

// Expression validator
public class JsonLogicValidator
{
    public ValidationResult Validate(JsonNode expression, JsonSchema dataSchema)
    {
        // Check that all {"var": "fieldName"} references exist in schema
        // Verify operators are valid
        // Check type compatibility
    }
}

// Expression tester
public class JsonLogicTester
{
    public object Evaluate(JsonNode expression, Dictionary<string, object> testData)
    {
        return JsonLogic.Apply(expression, testData);
    }

    public TestReport RunTests(JsonNode expression, IEnumerable<TestCase> tests)
    {
        // Run expression against multiple test cases
        // Report successes/failures
    }
}
```

**UI Enhancement (Blueprint Designer):**
```typescript
// Expression builder component
interface JsonLogicBuilderProps {
  schema: JsonSchema;
  onExpressionChange: (expression: JsonNode) => void;
}

// Visual builder with:
// - Drag-and-drop conditions
// - Field autocomplete from schema
// - Live preview with test data
// - Expression library snippets
```

### 4. Schema Evolution Strategy

**Recommendations:**
1. Version schemas explicitly (semantic versioning)
2. Maintain backward compatibility
3. Support schema migration tools
4. Provide deprecation warnings

**Implementation:**

```csharp
// Schema versioning
public class SchemaDocument
{
    public SchemaMetadata Metadata { get; set; }
    public JsonDocument Schema { get; set; }
    public string SchemaVersion { get; set; }  // e.g., "1.2.0"
    public SchemaCompatibility Compatibility { get; set; }
}

public enum SchemaCompatibility
{
    Breaking,      // Major version change
    Compatible,    // Minor/patch version change
    Identical      // No changes
}

// Schema migration
public class SchemaMigrationService
{
    public async Task<JsonNode> MigrateDataAsync(
        JsonNode data,
        string fromVersion,
        string toVersion)
    {
        // Apply migration transformations
        // E.g., rename fields, add defaults, remove deprecated fields
    }
}
```

### 5. Performance Optimization

**Recommendations:**
1. Cache evaluated JSON Logic expressions
2. Pre-compile JSON-e templates
3. Use lazy loading for schemas
4. Implement expression complexity limits

**Implementation:**

```csharp
// Expression cache
public class JsonLogicCache
{
    private readonly MemoryCache _cache = new();

    public CompiledExpression GetOrCompile(JsonNode expression)
    {
        var key = ComputeHash(expression);

        if (_cache.TryGetValue(key, out CompiledExpression? compiled))
            return compiled;

        compiled = JsonLogic.Compile(expression);
        _cache.Set(key, compiled, TimeSpan.FromHours(1));

        return compiled;
    }
}

// Complexity limits
public class ExpressionComplexityValidator
{
    public ValidationResult ValidateComplexity(JsonNode expression)
    {
        var depth = CalculateDepth(expression);
        var nodeCount = CountNodes(expression);

        if (depth > 10)
            return ValidationResult.Error("Expression too deeply nested");

        if (nodeCount > 100)
            return ValidationResult.Error("Expression too complex");

        return ValidationResult.Success();
    }
}
```

### 6. Developer Experience

**Recommendations:**
1. Create Blueprint Designer Visual Studio Code extension
2. Provide JSON Logic syntax highlighting
3. Build expression debugger
4. Generate TypeScript types from schemas

**Implementation:**

```json
// VSCode extension: .vscode/extensions/sorcha-blueprint/
{
  "contributes": {
    "languages": [{
      "id": "json-logic",
      "extensions": [".jsonlogic"],
      "configuration": "./language-configuration.json"
    }],
    "grammars": [{
      "language": "json-logic",
      "scopeName": "source.jsonlogic",
      "path": "./syntaxes/jsonlogic.tmLanguage.json"
    }],
    "commands": [{
      "command": "sorcha.validateBlueprint",
      "title": "Sorcha: Validate Blueprint"
    }, {
      "command": "sorcha.testJsonLogic",
      "title": "Sorcha: Test JSON Logic Expression"
    }]
  }
}
```

### 7. Documentation and Training

**Recommendations:**
1. Create interactive JSON Logic playground
2. Provide blueprint example library
3. Build video tutorial series
4. Offer certification program

**Resources to Create:**
- `json-logic-guide.md` - Comprehensive JSON Logic reference
- `json-e-templates.md` - Template authoring guide
- `/docs/json-ld-integration.md` - Semantic web integration guide
- `/examples/blueprints/` - Curated example library
- Blueprint Designer in-app tutorials

---

## Conclusion

Sorcha's blueprint system provides a powerful, declarative approach to multi-party workflow definition. The integration of JSON Schema, JSON Logic, and (optionally) JSON-LD and JSON-e creates a comprehensive framework that balances:

- **Flexibility**: Workflows adapt to data at runtime
- **Portability**: Same definition works across environments
- **Auditability**: All logic stored immutably on blockchain
- **Interoperability**: Standards-based semantic integration

The fluent builder API makes complex workflows easy to define while maintaining type safety. Transaction chaining ensures data flows correctly through the workflow, and JSON Logic enables sophisticated routing without code deployment.

**When to use these technologies:**

- **JSON Schema**: Always (core validation and UI generation)
- **JSON Logic**: Always for routing and calculations (already integrated)
- **JSON-LD**: Cross-organizational workflows, regulatory compliance
- **JSON-e**: Reusable workflow templates, multi-environment deployment

**Key Success Factors:**

1. Start simple: Use JSON Schema and JSON Logic first
2. Add JSON-LD for external integration needs
3. Introduce JSON-e when patterns emerge
4. Invest in tooling and developer experience
5. Provide comprehensive examples and documentation

The blueprint architecture positions Sorcha as a leader in declarative, blockchain-backed workflow orchestration with strong semantic web integration capabilities.

---

## References

- **Sorcha Blueprint Models**: [Blueprint.cs](../src/Common/Sorcha.Blueprint.Models/Blueprint.cs)
- **Fluent Builders**: [BlueprintBuilder.cs](../src/Core/Sorcha.Blueprint.Fluent/BlueprintBuilder.cs)
- **JSON Logic**: https://jsonlogic.com
- **JSON-e**: https://json-e.js.org
- **JSON-LD**: https://json-ld.org
- **JSON Schema**: https://json-schema.org
- **W3C DIDs**: https://www.w3.org/TR/did-core/
- **Schema.org**: https://schema.org
- **GS1 Web Vocabulary**: https://www.gs1.org/voc/
