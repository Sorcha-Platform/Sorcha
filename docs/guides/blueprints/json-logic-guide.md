# JSON Logic Guide for Sorcha Blueprints

## Table of Contents

1. [Introduction](#introduction)
2. [Basic Concepts](#basic-concepts)
3. [Operators Reference](#operators-reference)
4. [Common Patterns](#common-patterns)
5. [Testing and Validation](#testing-and-validation)
6. [Best Practices](#best-practices)
7. [Advanced Examples](#advanced-examples)

---

## Introduction

JSON Logic is a format for expressing conditional logic and calculations as JSON data structures. In Sorcha blueprints, JSON Logic is used for:

- **Conditional routing** - Route actions to different participants based on data
- **Calculations** - Compute derived values from action data
- **Conditional display** - Show/hide form fields based on conditions

### Why JSON Logic?

✓ **Declarative** - Logic expressed as data, not code
✓ **Portable** - Same logic runs in frontend, backend, and blockchain
✓ **Sandboxed** - Cannot execute arbitrary code (secure)
✓ **Serializable** - Store logic in database, send over network
✓ **Dynamic** - Change logic without code deployment
✓ **Auditable** - Logic is transparent and traceable

---

## Basic Concepts

### Structure

JSON Logic expressions are JSON objects where:
- **Keys** are operators
- **Values** are operands (can be nested expressions)

```json
{
  "operator": [operand1, operand2, ...]
}
```

### Variable References

Access data using the `var` operator:

```json
{"var": "fieldName"}
```

Nested fields use dot notation:

```json
{"var": "address.city"}
```

Default values if variable is missing:

```json
{"var": ["fieldName", "defaultValue"]}
```

---

## Operators Reference

### Comparison Operators

#### Equality

```json
{"==": [{"var": "status"}, "approved"]}
```

Checks if status equals "approved"

#### Inequality

```json
{"!=": [{"var": "status"}, "rejected"]}
```

Checks if status is not "rejected"

#### Greater Than

```json
{">": [{"var": "amount"}, 1000]}
```

Checks if amount is greater than 1000

#### Greater Than or Equal

```json
{">=": [{"var": "age"}, 18]}
```

Checks if age is 18 or more

#### Less Than

```json
{"<": [{"var": "score"}, 50]}
```

Checks if score is less than 50

#### Less Than or Equal

```json
{"<=": [{"var": "count"}, 100]}
```

Checks if count is 100 or less

### Logical Operators

#### AND

```json
{
  "and": [
    {">": [{"var": "amount"}, 5000]},
    {"==": [{"var": "department"}, "finance"]}
  ]
}
```

Both conditions must be true

#### OR

```json
{
  "or": [
    {"==": [{"var": "priority"}, "urgent"]},
    {">": [{"var": "amount"}, 10000]}
  ]
}
```

At least one condition must be true

#### NOT

```json
{"!": {"==": [{"var": "status"}, "draft"]}}
```

Inverts the result (true → false, false → true)

#### Truthy Check

```json
{"!!": {"var": "optionalField"}}
```

Converts value to boolean (checks if field exists and is truthy)

### Arithmetic Operators

#### Addition

```json
{"+": [{"var": "subtotal"}, {"var": "tax"}]}
```

Multiple values:
```json
{"+": [10, 20, 30]} // Result: 60
```

#### Subtraction

```json
{"-": [{"var": "total"}, {"var": "discount"}]}
```

#### Multiplication

```json
{"*": [{"var": "quantity"}, {"var": "unitPrice"}]}
```

#### Division

```json
{"/": [{"var": "total"}, {"var": "count"}]}
```

#### Modulo

```json
{"%": [{"var": "value"}, 10]}
```

Returns remainder of division

### Conditional (If-Then-Else)

The `if` operator supports multiple conditions:

```json
{
  "if": [
    condition1, resultIfTrue1,
    condition2, resultIfTrue2,
    defaultResult
  ]
}
```

**Example:**

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

Evaluates to:
- "director" if amount > 10,000
- "manager" if amount > 5,000
- "auto-approve" otherwise

### Array Operators

#### Map

Transform each item in an array:

```json
{
  "map": [
    {"var": "items"},
    {"*": [{"var": ""}, 2]}
  ]
}
```

Doubles each value in the array. Empty string `""` refers to current item.

#### Filter

Select items matching a condition:

```json
{
  "filter": [
    {"var": "items"},
    {">": [{"var": ""}, 10]}
  ]
}
```

Returns only items greater than 10

#### Reduce

Aggregate array values:

```json
{
  "reduce": [
    {"var": "items"},
    {"+": [{"var": "accumulator"}, {"var": "current"}]},
    0
  ]
}
```

Sums all items (initial value is 0)

#### All

Check if all items match a condition:

```json
{
  "all": [
    {"var": "items"},
    {">": [{"var": ""}, 0]}
  ]
}
```

Returns true if all items are positive

#### Some

Check if any item matches a condition:

```json
{
  "some": [
    {"var": "items"},
    {"==": [{"var": ""}, 5]}
  ]
}
```

Returns true if array contains value 5

#### In (Contains)

Check if value is in array:

```json
{"in": [5, {"var": "items"}]}
```

Or check if substring in string:

```json
{"in": ["foo", {"var": "text"}]}
```

### String Operators

#### Concatenation

```json
{"cat": ["Hello, ", {"var": "name"}, "!"]}
```

Result: "Hello, John!"

#### Substring

```json
{"substr": [{"var": "text"}, 0, 5]}
```

Extract first 5 characters

---

## Common Patterns

### Conditional Routing in Blueprints

Route to different participants based on amount:

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

### Complex Conditions

Combine multiple criteria:

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
    {
      "and": [
        {">": [{"var": "amount"}, 5000]},
        {"==": [{"var": "department"}, "finance"]}
      ]
    },
    "cfo",
    {
      "or": [
        {"==": [{"var": "priority"}, "urgent"]},
        {">": [{"var": "amount"}, 10000]}
      ]
    },
    "director",
    "manager"
  ]
}
```

### Calculations

Calculate total price:

```csharp
.Calculate("totalPrice", c => c
    .WithExpression(c.Multiply(
        c.Variable("quantity"),
        c.Variable("unitPrice"))))
```

**Generated JSON Logic:**

```json
{
  "totalPrice": {
    "*": [{"var": "quantity"}, {"var": "unitPrice"}]
  }
}
```

Calculate discount (10%):

```csharp
.Calculate("discount", c => c
    .WithExpression(c.Multiply(
        c.Variable("totalPrice"),
        c.Constant(0.1))))
```

**Generated JSON Logic:**

```json
{
  "discount": {
    "*": [{"var": "totalPrice"}, 0.1]
  }
}
```

### Conditional Form Fields

Show field only when status is "rejected":

```csharp
.AddControl(ctrl => ctrl
    .OfType(ControlTypes.TextArea)
    .WithTitle("Rejection Reason")
    .BoundTo("/rejectionReason")
    .ShowWhen(jl => jl.Equals("status", "rejected")))
```

---

## Testing and Validation

### Using JsonLogicTester

```csharp
using Sorcha.Blueprint.Engine.Testing;

var tester = new JsonLogicTester(jsonLogicEvaluator);

var expression = JsonNode.Parse(@"{
  ""if"": [
    {"">"""": [{""var"": ""amount""}, 10000]},
    ""director"",
    ""manager""
  ]
}");

var testCases = new[]
{
    JsonLogicTester.CreateTest("High amount")
        .WithInput("amount", 15000)
        .ExpectOutput("director")
        .Build(),

    JsonLogicTester.CreateTest("Medium amount")
        .WithInput("amount", 5000)
        .ExpectOutput("manager")
        .Build(),

    JsonLogicTester.CreateTest("Low amount")
        .WithInput("amount", 1000)
        .ExpectOutput("manager")
        .Build()
};

var report = await tester.RunTestsAsync(expression, testCases);

Console.WriteLine(report); // "Tests: 3/3 passed (100%) in 12.34ms"
```

### Using JsonLogicValidator

```csharp
using Sorcha.Blueprint.Engine.Validation;
using JsonSchema.Net;

var validator = new JsonLogicValidator();

var expression = JsonNode.Parse(@"{
  "">"": [{""var"": ""amount""}, 1000]
}");

var schema = JsonSchema.FromText(@"{
  ""type"": ""object"",
  ""properties"": {
    ""amount"": { ""type"": ""number"" }
  }
}");

var result = validator.Validate(expression, schema);

if (!result.IsValid)
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"Error: {error}");
    }
}
```

---

## Best Practices

### 1. Keep Expressions Simple

✗ **Bad** - Too complex, hard to understand:

```json
{
  "and": [
    {
      "or": [
        {"and": [{">": [{"var": "a"}, 5]}, {"<": [{"var": "b"}, 10]}]},
        {"and": [{">": [{"var": "c"}, 3]}, {"<": [{"var": "d"}, 7]}]}
      ]
    },
    {"!=": [{"var": "status"}, "cancelled"]}
  ]
}
```

✓ **Good** - Break into multiple calculations:

```json
{
  "calculations": {
    "validRange": {
      "and": [
        {">": [{"var": "a"}, 5]},
        {"<": [{"var": "b"}, 10]}
      ]
    },
    "isActive": {
      "!=": [{"var": "status"}, "cancelled"]
    }
  },
  "condition": {
    "and": [
      {"var": "validRange"},
      {"var": "isActive"}
    ]
  }
}
```

### 2. Use Descriptive Variable Names

✗ **Bad**:
```json
{"var": "a"}
```

✓ **Good**:
```json
{"var": "requestedAmount"}
```

### 3. Provide Default Values

✗ **Bad** - Fails if field missing:
```json
{">": [{"var": "amount"}, 1000]}
```

✓ **Good** - Uses default:
```json
{">": [{"var": ["amount", 0]}, 1000]}
```

### 4. Validate Against Schema

Always validate expressions against the action's data schema to catch variable reference errors early.

### 5. Test Edge Cases

Test your expressions with:
- Minimum/maximum values
- Null/undefined values
- Empty strings/arrays
- Boundary conditions

### 6. Document Complex Logic

Add comments in the blueprint metadata:

```json
{
  "metadata": {
    "routingLogic": "Routes to director for amounts over $10k, manager for $5-10k, auto-approves below $5k"
  }
}
```

---

## Advanced Examples

### Multi-Party Supply Chain Routing

```json
{
  "calculations": {
    "totalAmount": {
      "reduce": [
        {
          "map": [
            {"var": "items"},
            {"*": [{"var": "quantity"}, {"var": "unitPrice"}]}
          ]
        },
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
  },
  "condition": {
    "if": [
      {"==": [{"var": "decision"}, "rejected"]},
      null,

      {"var": "requiresQualityCheck"},
      "quality",

      {"var": "requiresFinanceApproval"},
      "finance",

      "buyer"
    ]
  }
}
```

**This expression:**
1. Calculates total order amount from line items
2. Determines if finance approval needed (amount > $5k OR extended payment terms)
3. Checks if quality inspection required (electronics or medical items)
4. Routes accordingly:
   - Rejected orders end workflow (null)
   - Quality check needed → quality dept
   - Finance approval needed → finance dept
   - Otherwise → buyer

### Dynamic Discount Calculation

```json
{
  "discount": {
    "if": [
      {">": [{"var": "quantity"}, 100]},
      {"*": [{"var": "totalPrice"}, 0.15]},

      {">": [{"var": "quantity"}, 50]},
      {"*": [{"var": "totalPrice"}, 0.10]},

      {">": [{"var": "quantity"}, 10]},
      {"*": [{"var": "totalPrice"}, 0.05]},

      0
    ]
  },
  "finalPrice": {
    "-": [{"var": "totalPrice"}, {"var": "discount"}]
  }
}
```

**Tiered discount:**
- 15% for 100+ items
- 10% for 50-99 items
- 5% for 10-49 items
- No discount below 10 items

---

## See Also

- [JSON Logic Official Documentation](https://jsonlogic.com)
- [Blueprint Architecture](./blueprint-architecture.md)
- [JSON-e Templates Guide](./json-e-templates.md)
- [Fluent Builder API](../src/Core/Sorcha.Blueprint.Fluent/)
