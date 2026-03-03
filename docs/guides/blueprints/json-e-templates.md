# JSON-e Templates Guide for Sorcha Blueprints

## Table of Contents

1. [Introduction](#introduction)
2. [Basic Syntax](#basic-syntax)
3. [Operators Reference](#operators-reference)
4. [Blueprint Template Patterns](#blueprint-template-patterns)
5. [Best Practices](#best-practices)
6. [Complete Examples](#complete-examples)
7. [Troubleshooting](#troubleshooting)

---

## Introduction

JSON-e is a templating language that allows you to generate JSON dynamically. In Sorcha, JSON-e templates enable you to create reusable, parameterized blueprint definitions.

### Why Use Templates?

✓ **Reusability** - Single template generates multiple blueprint variants
✓ **Maintainability** - Update template once, propagate to all instances
✓ **Policy-Driven** - Business rules determine workflow structure
✓ **Environment-Specific** - Different configs for dev/staging/prod
✓ **Reduced Errors** - Consistent structure across similar workflows

### When to Use Templates

Use templates when:
- Creating multiple similar blueprints (e.g., different loan types)
- Supporting multi-environment deployments
- Implementing policy-driven workflows
- Testing different workflow configurations

Don't use templates for:
- One-off unique workflows
- Simple blueprints that rarely change
- When team is unfamiliar with templating

---

## Basic Syntax

### Variable Substitution

Replace a value with a variable from context:

```json
{
  "name": { "$eval": "userName" }
}
```

**Context:**
```json
{
  "userName": "John Doe"
}
```

**Result:**
```json
{
  "name": "John Doe"
}
```

### Conditional Rendering

Include/exclude sections based on conditions:

```json
{
  "$if": "includeField",
  "then": { "field": "value" },
  "else": { "field": "default" }
}
```

**Context:**
```json
{
  "includeField": true
}
```

**Result:**
```json
{
  "field": "value"
}
```

### Iteration

Generate multiple items from an array:

```json
{
  "items": {
    "$map": { "$eval": "products" },
    "each(product)": {
      "name": { "$eval": "product.name" },
      "price": { "$eval": "product.price" }
    }
  }
}
```

**Context:**
```json
{
  "products": [
    { "name": "Widget", "price": 9.99 },
    { "name": "Gadget", "price": 19.99 }
  ]
}
```

**Result:**
```json
{
  "items": [
    { "name": "Widget", "price": 9.99 },
    { "name": "Gadget", "price": 19.99 }
  ]
}
```

---

## Operators Reference

### $eval

Evaluate an expression from context:

```json
{ "$eval": "variableName" }
```

Can evaluate complex expressions:

```json
{ "$eval": "basePrice * quantity" }
```

### $if / $then / $else

Conditional rendering:

```json
{
  "$if": "condition",
  "then": valueIfTrue,
  "else": valueIfFalse
}
```

**Example:**

```json
{
  "approver": {
    "$if": "requiresSeniorApproval",
    "then": "senior-officer",
    "else": "loan-officer"
  }
}
```

### $map

Iterate over an array:

```json
{
  "$map": { "$eval": "arrayVariable" },
  "each(item)": {
    "value": { "$eval": "item.field" }
  }
}
```

**With index:**

```json
{
  "$map": { "$eval": "items" },
  "each(item, index)": {
    "id": { "$eval": "index" },
    "name": { "$eval": "item.name" }
  }
}
```

### $flattenDeep

Flatten nested arrays:

```json
{
  "$flattenDeep": [
    [1, 2],
    [[3, 4], 5],
    6
  ]
}
```

**Result:** `[1, 2, 3, 4, 5, 6]`

### $merge

Merge multiple objects:

```json
{
  "$merge": [
    { "a": 1 },
    { "b": 2 },
    { "c": 3 }
  ]
}
```

**Result:** `{ "a": 1, "b": 2, "c": 3 }`

### $let

Define local variables:

```json
{
  "$let": {
    "doubled": { "$eval": "value * 2" }
  },
  "in": {
    "original": { "$eval": "value" },
    "doubled": { "$eval": "doubled" }
  }
}
```

### $switch

Pattern matching (like switch/case):

```json
{
  "$switch": {
    "category": {
      "$eval": "productType"
    },
    "electronics": { "taxRate": 0.08 },
    "food": { "taxRate": 0.02 },
    "default": { "taxRate": 0.05 }
  }
}
```

---

## Blueprint Template Patterns

### Parameterized Participants

Add participants conditionally:

```json
{
  "participants": [
    {
      "id": "applicant",
      "name": "Applicant"
    },
    {
      "id": "reviewer",
      "name": "Reviewer"
    },
    {
      "$if": "requiresSeniorApproval",
      "then": {
        "id": "senior-reviewer",
        "name": "Senior Reviewer"
      }
    }
  ]
}
```

**Note:** JSON-e will remove null values, so when condition is false, the participant won't appear.

### Dynamic Action Count

Generate variable numbers of actions:

```json
{
  "actions": {
    "$flattenDeep": [
      [
        {
          "id": 0,
          "title": "Submit Request",
          "sender": "requester"
        }
      ],
      {
        "$if": "requiresManagerApproval",
        "then": [
          {
            "id": 1,
            "title": "Manager Approval",
            "sender": "manager"
          }
        ],
        "else": []
      },
      {
        "$if": "requiresFinanceReview",
        "then": [
          {
            "id": 2,
            "title": "Finance Review",
            "sender": "finance"
          }
        ],
        "else": []
      },
      [
        {
          "id": 3,
          "title": "Complete",
          "sender": "system"
        }
      ]
    ]
  }
}
```

### Environment-Specific Configuration

Different settings per environment:

```json
{
  "participants": [
    {
      "id": "approver",
      "name": "Approver",
      "organisation": {
        "$eval": "environment == 'production' ? 'Real Bank Inc' : 'Test Bank LLC'"
      },
      "walletAddress": {
        "$eval": "environment == 'production' ? prodWallet : testWallet"
      }
    }
  ]
}
```

**Context:**
```json
{
  "environment": "development",
  "prodWallet": "0x742d35Cc6634C0532925a3b844Bc9e7595f0bEb",
  "testWallet": "0x0000000000000000000000000000000000000000"
}
```

### Nested Context Pattern

For complex templates, use nested context:

```json
{
  "$eval": "blueprintTemplate",
  "context": {
    "blueprintTemplate": {
      "id": { "$eval": "blueprintId" },
      "title": { "$eval": "blueprintTitle" },
      "participants": { "$eval": "participants" }
    },
    "blueprintId": "my-blueprint-001",
    "blueprintTitle": "My Blueprint",
    "participants": [...]
  }
}
```

This pattern:
1. Keeps template definition separate
2. Makes context variables explicit
3. Easier to debug and test

---

## Best Practices

### 1. Use Nested Context for Complex Templates

✓ **Good:**
```json
{
  "$eval": "template",
  "context": {
    "template": { ... },
    "param1": "value1",
    "param2": "value2"
  }
}
```

✗ **Bad:**
```json
{
  "field1": { "$eval": "param1" },
  "field2": {
    "nested": {
      "field3": { "$eval": "param2" }
    }
  }
}
```

### 2. Provide Sensible Defaults

Always include default parameters:

```json
{
  "defaultParameters": {
    "minAmount": 1000,
    "maxAmount": 100000,
    "requiresApproval": true
  }
}
```

### 3. Validate Parameters

Define a JSON Schema for parameters:

```json
{
  "parameterSchema": {
    "type": "object",
    "properties": {
      "blueprintId": {
        "type": "string",
        "minLength": 3
      },
      "minAmount": {
        "type": "number",
        "minimum": 0
      }
    },
    "required": ["blueprintId"]
  }
}
```

### 4. Document with Examples

Provide 2-3 concrete examples:

```json
{
  "examples": [
    {
      "name": "standard",
      "description": "Standard configuration",
      "parameters": { ... }
    },
    {
      "name": "high-value",
      "description": "For high-value transactions",
      "parameters": { ... }
    }
  ]
}
```

### 5. Keep Templates Readable

✗ **Bad** - Deeply nested and hard to read:

```json
{
  "$if": "a",
  "then": {
    "$if": "b",
    "then": {
      "$if": "c",
      "then": "value"
    }
  }
}
```

✓ **Good** - Use $let for clarity:

```json
{
  "$let": {
    "shouldInclude": { "$eval": "a && b && c" }
  },
  "in": {
    "$if": "shouldInclude",
    "then": "value"
  }
}
```

### 6. Test All Examples

Always verify that examples evaluate correctly:

```bash
GET /api/templates/{id}/examples/{exampleName}
```

---

## Complete Examples

### Simple Loan Application Template

```json
{
  "id": "loan-app-simple",
  "title": "Simple Loan Application",
  "template": {
    "$eval": "blueprint",
    "context": {
      "blueprint": {
        "id": { "$eval": "blueprintId" },
        "title": { "$eval": "title" },
        "description": "Loan application workflow",
        "version": 1,
        "participants": [
          {
            "id": "applicant",
            "name": "Applicant"
          },
          {
            "id": "officer",
            "name": { "$eval": "officerTitle" },
            "organisation": { "$eval": "bankName" }
          }
        ],
        "actions": [
          {
            "id": 0,
            "title": "Submit Application",
            "sender": "applicant",
            "dataSchemas": [
              {
                "type": "object",
                "properties": {
                  "amount": {
                    "type": "number",
                    "minimum": { "$eval": "minAmount" },
                    "maximum": { "$eval": "maxAmount" }
                  },
                  "purpose": {
                    "type": "string"
                  }
                },
                "required": ["amount", "purpose"]
              }
            ]
          },
          {
            "id": 1,
            "title": "Review",
            "sender": "officer",
            "dataSchemas": [
              {
                "type": "object",
                "properties": {
                  "decision": {
                    "type": "string",
                    "enum": ["approved", "rejected"]
                  }
                }
              }
            ]
          }
        ]
      },
      "blueprintId": { "$eval": "blueprintId" },
      "title": { "$eval": "title" },
      "officerTitle": { "$eval": "officerTitle" },
      "bankName": { "$eval": "bankName" },
      "minAmount": { "$eval": "minAmount" },
      "maxAmount": { "$eval": "maxAmount" }
    }
  },
  "parameterSchema": {
    "type": "object",
    "properties": {
      "blueprintId": { "type": "string" },
      "title": { "type": "string" },
      "bankName": { "type": "string" },
      "officerTitle": { "type": "string" },
      "minAmount": { "type": "number" },
      "maxAmount": { "type": "number" }
    },
    "required": ["blueprintId", "title", "bankName"]
  },
  "defaultParameters": {
    "blueprintId": "loan-001",
    "title": "Loan Application",
    "bankName": "Community Bank",
    "officerTitle": "Loan Officer",
    "minAmount": 1000,
    "maxAmount": 100000
  }
}
```

### Multi-Environment Template

```json
{
  "id": "env-aware-workflow",
  "template": {
    "$eval": "blueprint",
    "context": {
      "blueprint": {
        "id": { "$eval": "blueprintId" },
        "participants": [
          {
            "id": "approver",
            "walletAddress": {
              "$switch": {
                "env": { "$eval": "environment" },
                "production": { "$eval": "prodWallet" },
                "staging": { "$eval": "stagingWallet" },
                "default": { "$eval": "devWallet" }
              }
            }
          }
        ],
        "actions": [
          {
            "id": 0,
            "calculations": {
              "fee": {
                "$if": "environment == 'production'",
                "then": { "$eval": "amount * 0.02" },
                "else": 0
              }
            }
          }
        ]
      },
      "blueprintId": { "$eval": "blueprintId" },
      "environment": { "$eval": "environment" },
      "prodWallet": { "$eval": "prodWallet" },
      "stagingWallet": { "$eval": "stagingWallet" },
      "devWallet": { "$eval": "devWallet" },
      "amount": { "$eval": "amount" }
    }
  },
  "defaultParameters": {
    "environment": "development",
    "prodWallet": "0x742d35Cc6634C0532925a3b844Bc9e7595f0bEb",
    "stagingWallet": "0x1111111111111111111111111111111111111111",
    "devWallet": "0x0000000000000000000000000000000000000000"
  }
}
```

---

## Troubleshooting

### Template Evaluation Fails

**Problem:** Template doesn't evaluate

**Solutions:**
1. Check JSON syntax (commas, brackets, quotes)
2. Verify all `$eval` variables exist in context
3. Test with simple template first
4. Enable trace mode: `includeTrace: true`

### Generated Blueprint is Invalid

**Problem:** Evaluation succeeds but blueprint fails validation

**Solutions:**
1. Enable validation: `validate: true`
2. Check required fields (title, description, participants, actions)
3. Verify participant references in actions
4. Test with minimal template

### Parameters Not Applied

**Problem:** Template uses default values instead of provided parameters

**Solutions:**
1. Verify parameter names match template `$eval` expressions
2. Check context nesting
3. Ensure parameters are in correct format (string vs number)
4. Use trace mode to see context values

### Conditional Not Working

**Problem:** `$if` always evaluates to same result

**Solutions:**
1. Check boolean logic in condition
2. Verify variable names
3. Remember: `$if` expects truthy/falsy values
4. Use explicit comparisons: `environment == 'production'`

### Array Generation Issues

**Problem:** Extra nulls or missing items in arrays

**Solutions:**
1. Use `$flattenDeep` to remove nulls
2. Return empty array `[]` in else clause
3. Filter nulls after generation

---

## See Also

- [JSON-e Official Documentation](https://json-e.js.org/)
- [Template Examples](../examples/templates/)
- [JSON Logic Guide](./json-logic-guide.md)
- [Blueprint Architecture](./blueprint-architecture.md)
