# Blueprint Format Specification

## Overview

Blueprints are the core workflow definitions in Sorcha. They define multi-party, data-driven workflows with conditional routing, selective data disclosure, and JSON Logic-based business rules.

## Primary Blueprint Format: JSON/YAML

**IMPORTANT**: Blueprints should **always** be created as JSON or YAML documents. This is the primary and recommended format for:
- Creating workflow templates and demos
- Storing blueprint definitions
- Sharing blueprint specifications
- AI agent-generated blueprints

**YAML Format** is supported for space savings and improved readability when needed.

**Fluent API** (C# code-based blueprint creation) should only be used in rare cases where developers need to programmatically generate blueprints at runtime. For most use cases, use JSON or YAML documents.

## Blueprint Templating with JSON-e

Blueprints often need dynamic runtime values (e.g., wallet addresses, timestamps, user-specific data). Use **JSON-e** templating for runtime variable replacement:

```json
{
  "participants": [
    {
      "id": "applicant",
      "name": "Loan Applicant",
      "walletAddress": {"$eval": "walletAddresses.applicant"}
    },
    {
      "id": "officer",
      "name": "Loan Officer",
      "walletAddress": {"$eval": "walletAddresses.officer"}
    }
  ]
}
```

At runtime, provide template context:
```json
{
  "walletAddresses": {
    "applicant": "0x742d35Cc6634C0532925a3b844Bc9e7595f0bEb",
    "officer": "0x8Bb5C5e7b4e4C8d8D5e5B8c8B5d5e5B8c8B5d5e5"
  }
}
```

**JSON-e Resources:**
- Specification: https://json-e.js.org/
- NuGet Package: `JsonE.NET` for C# integration

## JSON Schema

The complete JSON Schema is available at [`src/Common/blueprint.schema.json`](../src/Common/blueprint.schema.json).

## Core Concepts

### 1. Blueprint

The root object representing a complete workflow definition.

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "title": "Purchase Order Workflow",
  "description": "A workflow for creating and approving purchase orders",
  "version": 1,
  "createdAt": "2025-01-15T10:30:00Z",
  "updatedAt": "2025-01-15T10:30:00Z",
  "metadata": {
    "department": "Procurement",
    "category": "Financial"
  },
  "participants": [...],
  "actions": [...]
}
```

**Required Fields:**
- `id` (string, max 64): Unique identifier (typically a GUID)
- `title` (string, 3-200 chars): Human-readable title
- `description` (string, 5-2048 chars): Detailed description
- `participants` (array, min 2): List of workflow participants
- `actions` (array, min 1): List of workflow actions

**Optional Fields:**
- `version` (integer, default 1): Blueprint version number
- `createdAt` (datetime): Creation timestamp
- `updatedAt` (datetime): Last modification timestamp
- `metadata` (object): Additional key-value metadata

### 2. Participant

Represents an entity that can perform actions in the workflow.

```json
{
  "id": "buyer",
  "name": "Purchasing Department",
  "organisation": "ACME Corp",
  "walletAddress": "0x742d35Cc6634C0532925a3b844Bc9e7595f0bEb",
  "didUri": "did:example:123456789abcdefghi",
  "useStealthAddress": false
}
```

**Required Fields:**
- `id` (string, max 64): Unique participant identifier
- `name` (string, max 100): Display name

**Optional Fields:**
- `organisation` (string, max 200): Organization name
- `walletAddress` (string, max 100): Blockchain wallet address
- `didUri` (string, max 200): Decentralized Identifier
- `useStealthAddress` (boolean, default false): Enable privacy features

### 3. Action

Defines a step in the workflow with data requirements, routing logic, and UI.

```json
{
  "id": 0,
  "title": "Create Purchase Order",
  "description": "Buyer creates a new purchase order",
  "sender": "buyer",
  "dataSchemas": [{
    "type": "object",
    "properties": {
      "item": { "type": "string", "title": "Item Description" },
      "quantity": { "type": "integer", "minimum": 1 },
      "price": { "type": "number", "minimum": 0 }
    },
    "required": ["item", "quantity", "price"]
  }],
  "disclosures": [...],
  "condition": {...},
  "form": {...}
}
```

**Required Fields:**
- `id` (integer): Sequential action ID (0-based)
- `title` (string, 1-100 chars): Action title

**Optional Fields:**
- `description` (string, max 2048): Detailed description
- `sender` (string): Participant ID of the sender
- `participants` (array): Conditional routing rules
- `dataSchemas` (array): JSON Schemas for required data
- `disclosures` (array): Data visibility rules
- `condition` (object): JSON Logic routing expression
- `calculations` (object): JSON Logic calculations
- `form` (object): UI form definition
- `previousData` (object): Data from previous action
- `requiredActionData` (array): Required data IDs
- `additionalRecipients` (array): CC recipients

### 4. Disclosure

Defines what data fields a participant can see.

```json
{
  "participantAddress": "approver",
  "dataPointers": [
    "/item",
    "/quantity",
    "/price"
  ]
}
```

**Required Fields:**
- `participantAddress` (string): Participant ID
- `dataPointers` (array, min 1): JSON Pointers to fields

**JSON Pointer Format:**
- Single field: `"/fieldName"`
- Nested field: `"/parent/child"`
- All fields: `"/*"`

### 5. Condition

Conditional routing logic for determining next participants.

```json
{
  "principal": "approver",
  "criteria": [
    "{\">\": [{\"var\": \"price\"}, 1000]}"
  ]
}
```

**Required Fields:**
- `criteria` (array, min 1): JSON Logic expressions as strings

**Optional Fields:**
- `principal` (string): Target participant ID

### 6. Control (UI Form)

Defines the user interface for data entry.

```json
{
  "type": "Layout",
  "layout": "VerticalLayout",
  "title": "Purchase Order Form",
  "elements": [
    {
      "type": "TextLine",
      "scope": "/item",
      "title": "Item Description"
    },
    {
      "type": "Numeric",
      "scope": "/quantity",
      "title": "Quantity"
    }
  ]
}
```

**Control Types:**
- `Layout`: Container for other controls
- `Label`: Static text
- `TextLine`: Single-line text input
- `TextArea`: Multi-line text input
- `Numeric`: Number input
- `DateTime`: Date/time picker
- `File`: File upload
- `Choice`: Radio buttons
- `Checkbox`: Boolean checkbox
- `Selection`: Dropdown list

**Layout Types:**
- `VerticalLayout`: Stack controls vertically
- `HorizontalLayout`: Arrange controls horizontally
- `Group`: Grouped section
- `Categorization`: Tabbed interface

## JSON Logic

Sorcha uses [JSON Logic](https://jsonlogic.com/) for conditional routing and calculations.

### Common Operators

**Comparison:**
```json
{">": [{"var": "price"}, 1000]}          // price > 1000
{">=": [{"var": "quantity"}, 10]}        // quantity >= 10
{"<": [{"var": "age"}, 18]}              // age < 18
{"<=": [{"var": "score"}, 100]}          // score <= 100
{"==": [{"var": "status"}, "approved"]}  // status == "approved"
{"!=": [{"var": "type"}, "urgent"]}      // type != "urgent"
```

**Logical:**
```json
{"and": [condition1, condition2]}        // AND
{"or": [condition1, condition2]}         // OR
{"!": condition}                         // NOT
```

**Arithmetic:**
```json
{"+": [{"var": "price"}, {"var": "tax"}]}           // price + tax
{"-": [{"var": "total"}, {"var": "discount"}]}      // total - discount
{"*": [{"var": "quantity"}, {"var": "unitPrice"}]}  // quantity * unitPrice
{"/": [{"var": "total"}, {"var": "count"}]}         // total / count
{"%": [{"var": "value"}, 10]}                       // value % 10
```

**Conditional:**
```json
{
  "if": [
    {">": [{"var": "price"}, 1000]},
    "senior-approver",
    "standard-approver"
  ]
}
```

## Fluent API (Rare Developer Use Only)

**NOTE**: The Fluent API should only be used in rare cases where developers need to programmatically generate blueprints at runtime within C# applications. For most use cases, including demos, templates, and AI-generated blueprints, **use JSON or YAML documents instead**.

The Fluent API provides a code-first approach for dynamic blueprint generation:

```csharp
using Sorcha.Blueprint.Fluent;

// Only use Fluent API when blueprints must be generated programmatically
// For static blueprints, use JSON/YAML files instead
var blueprint = BlueprintBuilder.Create()
    .WithTitle("Purchase Order Workflow")
    .WithDescription("A workflow for creating and approving purchase orders")
    .AddParticipant("buyer", p => p
        .Named("Purchasing Department")
        .FromOrganisation("ACME Corp")
        .WithWallet("0x742d35Cc6634C0532925a3b844Bc9e7595f0bEb"))
    .AddParticipant("approver", p => p
        .Named("Finance Manager")
        .FromOrganisation("ACME Corp"))
    .AddAction(0, a => a
        .WithTitle("Create Purchase Order")
        .SentBy("buyer")
        .RequiresData(schema => schema
            .AddString("item", f => f
                .WithTitle("Item Description")
                .IsRequired())
            .AddInteger("quantity", f => f
                .WithTitle("Quantity")
                .WithMinimum(1)
                .IsRequired())
            .AddNumber("price", f => f
                .WithTitle("Unit Price")
                .WithMinimum(0)
                .IsRequired()))
        .Disclose("approver", d => d
            .AllFields())
        .RouteConditionally(c => c
            .When(logic => logic.GreaterThan("price", 1000))
            .ThenRoute("senior-approver")
            .ElseRoute("approver"))
        .WithForm(form => form
            .WithLayout(LayoutTypes.VerticalLayout)
            .AddControl(ctrl => ctrl
                .OfType(ControlTypes.TextLine)
                .WithTitle("Item Description")
                .BoundTo("/item"))))
    .Build();
```

**When to Use Fluent API:**
- Building blueprint generation tools
- Dynamic blueprint creation based on runtime conditions
- Testing and unit test scenarios

**When NOT to Use Fluent API:**
- Creating workflow templates
- Demo applications
- Static blueprint definitions
- AI-generated blueprints

## Complete Example

```json
{
  "id": "po-workflow-v1",
  "title": "Purchase Order Approval",
  "description": "Three-step purchase order workflow with conditional approval routing",
  "version": 1,
  "participants": [
    {
      "id": "buyer",
      "name": "Purchasing Department",
      "organisation": "ACME Corp",
      "walletAddress": "0x742d35Cc6634C0532925a3b844Bc9e7595f0bEb"
    },
    {
      "id": "approver",
      "name": "Finance Manager",
      "organisation": "ACME Corp",
      "walletAddress": "0x8Bb5C5e7b4e4C8d8D5e5B8c8B5d5e5B8c8B5d5e5"
    },
    {
      "id": "senior-approver",
      "name": "CFO",
      "organisation": "ACME Corp",
      "walletAddress": "0x9Cc6D6f8c5f5D9e9E6f6C9d9C6e6f6C9d9C6e6f6"
    }
  ],
  "actions": [
    {
      "id": 0,
      "title": "Create Purchase Order",
      "description": "Buyer submits a purchase order request",
      "sender": "buyer",
      "dataSchemas": [
        {
          "type": "object",
          "properties": {
            "item": {
              "type": "string",
              "title": "Item Description",
              "minLength": 3,
              "maxLength": 200
            },
            "quantity": {
              "type": "integer",
              "title": "Quantity",
              "minimum": 1
            },
            "unitPrice": {
              "type": "number",
              "title": "Unit Price",
              "minimum": 0
            }
          },
          "required": ["item", "quantity", "unitPrice"]
        }
      ],
      "disclosures": [
        {
          "participantAddress": "approver",
          "dataPointers": ["/*"]
        },
        {
          "participantAddress": "senior-approver",
          "dataPointers": ["/*"]
        }
      ],
      "calculations": {
        "totalPrice": {
          "*": [
            {"var": "quantity"},
            {"var": "unitPrice"}
          ]
        }
      },
      "condition": {
        "if": [
          {">": [{"var": "totalPrice"}, 10000]},
          "senior-approver",
          "approver"
        ]
      },
      "form": {
        "type": "Layout",
        "layout": "VerticalLayout",
        "title": "Purchase Order Details",
        "elements": [
          {
            "type": "TextLine",
            "scope": "/item",
            "title": "Item Description"
          },
          {
            "type": "Numeric",
            "scope": "/quantity",
            "title": "Quantity"
          },
          {
            "type": "Numeric",
            "scope": "/unitPrice",
            "title": "Unit Price ($)"
          }
        ]
      }
    },
    {
      "id": 1,
      "title": "Approve Purchase Order",
      "description": "Finance manager reviews and approves the order",
      "sender": "approver",
      "dataSchemas": [
        {
          "type": "object",
          "properties": {
            "approved": {
              "type": "boolean",
              "title": "Approved"
            },
            "comments": {
              "type": "string",
              "title": "Comments",
              "maxLength": 500
            }
          },
          "required": ["approved"]
        }
      ],
      "disclosures": [
        {
          "participantAddress": "buyer",
          "dataPointers": ["/approved", "/comments"]
        }
      ]
    }
  ]
}
```

## Validation Rules

1. **Blueprint Level:**
   - Must have at least 2 participants
   - Must have at least 1 action
   - Title must be 3-200 characters
   - Description must be 5-2048 characters

2. **Participant Level:**
   - ID must be unique within blueprint
   - Name is required and max 100 characters

3. **Action Level:**
   - IDs should be sequential starting from 0
   - Title is required (1-100 characters)
   - Sender must reference a valid participant ID
   - Disclosure participant addresses must reference valid participants

4. **Data Schema:**
   - Must be valid JSON Schema
   - Supports standard JSON Schema keywords

5. **JSON Logic:**
   - Must be valid JSON Logic expressions
   - Variable references must match data schema fields

## Best Practices

1. **Participant Design:**
   - Use descriptive IDs (e.g., "buyer", "approver")
   - Include organization information for multi-org workflows
   - Consider using stealth addresses for privacy-sensitive workflows

2. **Action Design:**
   - Keep actions focused on single responsibilities
   - Use descriptive titles and descriptions
   - Define clear data schemas with validation

3. **Disclosure Management:**
   - Follow the principle of least privilege
   - Use specific field pointers rather than `/*` when possible
   - Consider data sensitivity in multi-party workflows

4. **Routing Logic:**
   - Keep conditional logic simple and testable
   - Use calculations to derive values before routing
   - Document complex routing rules in action descriptions

5. **Form Design:**
   - Use appropriate control types for data entry
   - Group related fields with layout controls
   - Bind controls to data schema fields using JSON Pointers

## See Also

- [Fluent API Documentation](./fluent-api.md)
- [JSON Logic Reference](https://jsonlogic.com/)
- [JSON Schema Reference](https://json-schema.org/)
- [JSON Pointer (RFC 6901)](https://tools.ietf.org/html/rfc6901)
