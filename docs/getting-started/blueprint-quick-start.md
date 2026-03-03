# Blueprint Quick Start Guide

## Table of Contents

1. [What Are Blueprints?](#what-are-blueprints)
2. [5-Minute Quick Start](#5-minute-quick-start)
3. [Basic Features with Examples](#basic-features-with-examples)
4. [Execution Flow Diagram](#execution-flow-diagram)
5. [Common Patterns](#common-patterns)
6. [Troubleshooting](#troubleshooting)
7. [Next Steps](#next-steps)

---

## What Are Blueprints?

Blueprints are **JSON/YAML workflow definitions** that enable:

✅ **Multi-party workflows** - Buyers, sellers, approvers working together
✅ **Data validation** - Automatic checking using JSON Schema
✅ **Smart routing** - Data-driven participant selection
✅ **Privacy** - Each participant sees only their allowed data
✅ **Calculations** - Automatic field derivation (totals, taxes, etc.)
✅ **Audit trail** - Every step signed and stored on blockchain

---

## 5-Minute Quick Start

### Simplest Possible Blueprint: Two-Party Approval

```json
{
  "id": "simple-approval",
  "title": "Simple Approval Workflow",
  "description": "Requester submits, approver decides",
  "version": 1,

  "participants": [
    {
      "id": "requester",
      "name": "John Doe"
    },
    {
      "id": "approver",
      "name": "Jane Smith"
    }
  ],

  "actions": [
    {
      "id": 0,
      "title": "Submit Request",
      "sender": "requester",
      "dataSchemas": [
        {
          "type": "object",
          "properties": {
            "amount": {
              "type": "number",
              "minimum": 0
            },
            "reason": {
              "type": "string"
            }
          },
          "required": ["amount", "reason"]
        }
      ],
      "disclosures": [
        {
          "participantAddress": "approver",
          "dataPointers": ["/*"]
        }
      ]
    },
    {
      "id": 1,
      "title": "Approve or Reject",
      "sender": "approver",
      "dataSchemas": [
        {
          "type": "object",
          "properties": {
            "decision": {
              "type": "string",
              "enum": ["approved", "rejected"]
            }
          },
          "required": ["decision"]
        }
      ],
      "disclosures": [
        {
          "participantAddress": "requester",
          "dataPointers": ["/decision"]
        }
      ]
    }
  ]
}
```

**That's it!** This complete blueprint defines a two-party approval workflow.

---

## Basic Features with Examples

### 1. Participants (Who's Involved?)

```json
{
  "participants": [
    {
      "id": "buyer",
      "name": "Acme Corp",
      "walletAddress": "0x742d35Cc6634C0532925a3b844Bc9e7595f0bEb"
    },
    {
      "id": "seller",
      "name": "Global Supplies",
      "walletAddress": "0x1234567890abcdef1234567890abcdef12345678"
    }
  ]
}
```

**What This Does:**
- Defines the parties in the workflow
- `id` = short identifier used in actions
- `name` = display name
- `walletAddress` = blockchain address for signing transactions

---

### 2. Data Validation (JSON Schema)

```json
{
  "dataSchemas": [
    {
      "type": "object",
      "properties": {
        "email": {
          "type": "string",
          "format": "email"
        },
        "age": {
          "type": "integer",
          "minimum": 18,
          "maximum": 120
        },
        "amount": {
          "type": "number",
          "minimum": 0.01
        }
      },
      "required": ["email", "age"]
    }
  ]
}
```

**What This Does:**
- `email` must be valid email format
- `age` must be 18-120
- `amount` must be at least $0.01
- `email` and `age` are required fields

**Example Data:**
```json
{
  "email": "john@example.com",
  "age": 25,
  "amount": 100.50
}
```
✅ **Passes validation**

```json
{
  "email": "not-an-email",
  "age": 15,
  "amount": -5
}
```
❌ **Fails validation** (invalid email, age < 18, negative amount)

---

### 3. Calculations (Automatic Field Derivation)

```json
{
  "calculations": {
    "totalPrice": {
      "*": [{"var": "quantity"}, {"var": "unitPrice"}]
    },
    "tax": {
      "*": [{"var": "totalPrice"}, 0.10]
    },
    "grandTotal": {
      "+": [{"var": "totalPrice"}, {"var": "tax"}]
    }
  }
}
```

**What This Does:**
- `totalPrice` = quantity × unitPrice
- `tax` = totalPrice × 10%
- `grandTotal` = totalPrice + tax

**Example:**

**Input Data:**
```json
{
  "quantity": 5,
  "unitPrice": 100
}
```

**Calculated Fields (automatically added):**
```json
{
  "quantity": 5,
  "unitPrice": 100,
  "totalPrice": 500,
  "tax": 50,
  "grandTotal": 550
}
```

---

### 4. Conditional Routing (Smart Participant Selection)

```json
{
  "condition": {
    "if": [
      {">": [{"var": "amount"}, 10000]},
      "senior-manager",

      {">": [{"var": "amount"}, 5000]},
      "manager",

      "auto-approve"
    ]
  }
}
```

**What This Does:**
- If amount > $10,000 → route to senior-manager
- If amount > $5,000 → route to manager
- Otherwise → auto-approve

**Examples:**

| Amount | Routes To |
|--------|-----------|
| $15,000 | senior-manager |
| $7,500 | manager |
| $2,000 | auto-approve |

---

### 5. Selective Disclosure (Privacy Control)

```json
{
  "disclosures": [
    {
      "participantAddress": "buyer",
      "dataPointers": ["/status", "/estimatedDelivery"]
    },
    {
      "participantAddress": "seller",
      "dataPointers": ["/*"]
    }
  ]
}
```

**What This Does:**
- **Buyer** sees only `status` and `estimatedDelivery` fields
- **Seller** sees all fields (`/*`)

**Example:**

**Full Data:**
```json
{
  "status": "confirmed",
  "estimatedDelivery": "2025-02-15",
  "internalCost": 450,
  "profitMargin": 0.25,
  "supplierNotes": "VIP customer"
}
```

**Buyer Sees:**
```json
{
  "status": "confirmed",
  "estimatedDelivery": "2025-02-15"
}
```

**Seller Sees:**
```json
{
  "status": "confirmed",
  "estimatedDelivery": "2025-02-15",
  "internalCost": 450,
  "profitMargin": 0.25,
  "supplierNotes": "VIP customer"
}
```

---

### 6. UI Form Generation

```json
{
  "form": {
    "type": "Layout",
    "layout": "VerticalLayout",
    "title": "Purchase Request",
    "elements": [
      {
        "type": "TextLine",
        "scope": "/itemName",
        "title": "Item Name"
      },
      {
        "type": "Numeric",
        "scope": "/quantity",
        "title": "Quantity"
      },
      {
        "type": "Selection",
        "scope": "/urgency",
        "title": "Urgency"
      }
    ]
  }
}
```

**What This Does:**
- Automatically generates a form UI
- `scope` binds control to data field (JSON Pointer)
- Different control types for different data

**Control Types:**
- `TextLine` - Single-line text
- `TextArea` - Multi-line text
- `Numeric` - Number input
- `DateTime` - Date/time picker
- `Selection` - Dropdown
- `Checkbox` - Boolean checkbox
- `Choice` - Radio buttons

---

## Execution Flow Diagram

```
┌─────────────────────────────────────────────────────────┐
│ Requester Submits Request                              │
│                                                         │
│ Data: { "amount": 7500, "reason": "New equipment" }    │
└──────────────────────┬──────────────────────────────────┘
                       ↓
        ┌──────────────────────────────┐
        │ 1. VALIDATE                  │
        │ ✓ amount is a number         │
        │ ✓ reason is provided         │
        └──────────┬───────────────────┘
                   ↓
        ┌──────────────────────────────┐
        │ 2. CALCULATE                 │
        │ (if any calculations)        │
        └──────────┬───────────────────┘
                   ↓
        ┌──────────────────────────────┐
        │ 3. ROUTE                     │
        │ amount = 7500                │
        │ → routes to "manager"        │
        └──────────┬───────────────────┘
                   ↓
        ┌──────────────────────────────┐
        │ 4. DISCLOSE                  │
        │ Manager sees all fields      │
        └──────────┬───────────────────┘
                   ↓
        ┌──────────────────────────────┐
        │ 5. SIGN & STORE              │
        │ Transaction on blockchain    │
        └──────────────────────────────┘
                   ↓
┌─────────────────────────────────────────────────────────┐
│ Manager Approves/Rejects                                │
│                                                         │
│ Data: { "decision": "approved" }                        │
└──────────────────────┬──────────────────────────────────┘
                       ↓
        ┌──────────────────────────────┐
        │ 1-5. Same Process            │
        └──────────┬───────────────────┘
                   ↓
        ┌──────────────────────────────┐
        │ Workflow Complete            │
        └──────────────────────────────┘
```

---

## Common Patterns

### Pattern 1: Three-Tier Approval

```json
{
  "condition": {
    "if": [
      {">": [{"var": "amount"}, 50000]},
      "ceo",

      {">": [{"var": "amount"}, 10000]},
      "director",

      {">": [{"var": "amount"}, 1000]},
      "manager",

      "auto-approve"
    ]
  }
}
```

### Pattern 2: Calculate Totals from Line Items

```json
{
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
    }
  }
}
```

**Input:**
```json
{
  "items": [
    {"quantity": 2, "unitPrice": 10},
    {"quantity": 3, "unitPrice": 15}
  ]
}
```

**Output:**
```json
{
  "items": [...],
  "itemTotals": [20, 45],
  "totalAmount": 65
}
```

### Pattern 3: Conditional Field Display

```json
{
  "form": {
    "elements": [
      {
        "type": "Selection",
        "scope": "/status",
        "title": "Status"
      },
      {
        "type": "TextArea",
        "scope": "/rejectionReason",
        "title": "Rejection Reason",
        "rule": {
          "effect": "SHOW",
          "condition": {
            "==": [{"var": "status"}, "rejected"]
          }
        }
      }
    ]
  }
}
```

Shows "Rejection Reason" field only when status = "rejected"

### Pattern 4: Multi-Party Visibility

```json
{
  "disclosures": [
    {
      "participantAddress": "requester",
      "dataPointers": ["/status", "/comments"]
    },
    {
      "participantAddress": "manager",
      "dataPointers": ["/*"]
    },
    {
      "participantAddress": "auditor",
      "dataPointers": ["/status", "/amount", "/timestamp"]
    }
  ]
}
```

- **Requester**: sees status and comments
- **Manager**: sees everything
- **Auditor**: sees status, amount, timestamp (read-only audit)

---

## Troubleshooting

### Problem: "Validation failed"

**Cause:** Submitted data doesn't match JSON Schema

**Solution:**
1. Check required fields are present
2. Verify data types (string vs number vs boolean)
3. Check min/max constraints
4. Validate format (email, date, etc.)

**Example:**
```json
// ❌ Wrong
{
  "amount": "100"  // String instead of number
}

// ✅ Correct
{
  "amount": 100  // Number
}
```

### Problem: "No next participant found"

**Cause:** Routing condition didn't match any participant

**Solution:**
1. Always provide a default route (else clause)
2. Check variable names in conditions
3. Verify data is available for routing logic

**Example:**
```json
// ❌ Wrong (no default)
{
  "if": [
    {">": [{"var": "amount"}, 1000]},
    "manager"
  ]
}

// ✅ Correct (with default)
{
  "if": [
    {">": [{"var": "amount"}, 1000]},
    "manager",
    "auto-approve"  // Default
  ]
}
```

### Problem: "Participant sees wrong data"

**Cause:** Incorrect disclosure configuration

**Solution:**
1. Check `dataPointers` use correct JSON Pointer syntax
2. Verify `participantAddress` matches participant ID
3. Remember `/*` means ALL fields

**Example:**
```json
// ❌ Wrong
{
  "dataPointers": ["status"]  // Missing leading slash
}

// ✅ Correct
{
  "dataPointers": ["/status"]  // JSON Pointer format
}
```

### Problem: "Calculation not working"

**Cause:** Invalid JSON Logic expression or missing variables

**Solution:**
1. Verify variable names match data fields
2. Check JSON Logic syntax
3. Test with simpler expressions first

**Example:**
```json
// ❌ Wrong
{
  "total": {
    "*": [{"var": "qty"}, {"var": "price"}]
  }
}
// Data has "quantity" and "unitPrice", not "qty" and "price"

// ✅ Correct
{
  "total": {
    "*": [{"var": "quantity"}, {"var": "unitPrice"}]
  }
}
```

---

## Next Steps

### Learn More

📖 **Comprehensive Documentation:**
- [Blueprint Architecture](./blueprint-architecture.md) - Deep dive into implementation
- [JSON Logic Guide](./json-logic-guide.md) - Complete operator reference
- [JSON-e Templates](./json-e-templates.md) - Dynamic blueprint generation
- [Blueprint Format](./blueprint-format.md) - Complete specification

### Examples

📁 **Example Blueprints:**
- Simple Invoice Approval: `examples/blueprints/simple-invoice-approval.json`
- Purchase Order Workflow: `examples/blueprints/moderate-purchase-order-approval.json`
- Multi-Party Supply Chain: `examples/blueprints/complex-supply-chain.json`

### Try It Out

🚀 **Hands-On:**

1. **Start Sorcha locally:**
   ```bash
   dotnet run --project src/Apps/Sorcha.AppHost
   ```

2. **Open API Gateway:**
   - Navigate to https://localhost:7082
   - Try the `/api/blueprints` endpoints

3. **Create your first blueprint:**
   - Use the simple approval example above
   - POST to `/api/blueprints`
   - Execute actions via `/api/blueprints/{id}/actions/{actionId}/execute`

### Advanced Topics

🎓 **Once you're comfortable with basics:**

1. **JSON-LD Integration** - Add semantic meaning for cross-system interoperability
2. **JSON-e Templates** - Create reusable blueprint templates
3. **Complex Routing** - Multi-condition routing with AND/OR logic
4. **Form Customization** - Advanced UI controls and layouts
5. **Performance Optimization** - Caching and expression optimization

---

## Quick Reference Card

### Blueprint Skeleton

```json
{
  "id": "...",
  "title": "...",
  "description": "...",
  "version": 1,
  "participants": [ /* at least 2 */ ],
  "actions": [ /* at least 1 */ ]
}
```

### Action Skeleton

```json
{
  "id": 0,
  "title": "...",
  "sender": "participant-id",
  "dataSchemas": [ /* JSON Schema */ ],
  "calculations": { /* JSON Logic */ },
  "condition": { /* JSON Logic */ },
  "disclosures": [ /* visibility rules */ ],
  "form": { /* UI definition */ }
}
```

### JSON Logic Operators

```json
// Comparison
{">": [a, b]}       // Greater than
{">=": [a, b]}      // Greater or equal
{"<": [a, b]}       // Less than
{"<=": [a, b]}      // Less or equal
{"==": [a, b]}      // Equal
{"!=": [a, b]}      // Not equal

// Logical
{"and": [a, b]}     // AND
{"or": [a, b]}      // OR
{"!": a}            // NOT

// Arithmetic
{"+": [a, b]}       // Add
{"-": [a, b]}       // Subtract
{"*": [a, b]}       // Multiply
{"/": [a, b]}       // Divide

// Conditional
{"if": [condition, thenValue, elseValue]}

// Variables
{"var": "fieldName"}
```

### Disclosure Pointers

```json
"/*"                // All fields
"/fieldName"        // Single field
"/parent/child"     // Nested field
"/items/0/price"    // Array element
```

---

**Happy Blueprint Building! 🎉**

For questions or issues, see the [main documentation](./README.md) or create an issue on GitHub.
