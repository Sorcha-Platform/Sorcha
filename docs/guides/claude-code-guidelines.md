# Claude Code Guidelines for Sorcha Project

**Purpose:** This document captures learnings, functional requirements, and non-functional requirements to ensure high-quality code that aligns with Sorcha's design and architecture principles.

**Last Updated:** 2025-11-16
**Version:** 1.0
**Audience:** AI code assistants working on the Sorcha codebase

---

## Table of Contents

1. [Critical Learnings from Common Errors](#critical-learnings-from-common-errors)
2. [Functional Requirements](#functional-requirements)
3. [Non-Functional Requirements](#non-functional-requirements)
4. [Architecture Principles](#architecture-principles)
5. [Code Quality Standards](#code-quality-standards)
6. [Testing Requirements](#testing-requirements)
7. [Common Patterns and Anti-Patterns](#common-patterns-and-anti-patterns)
8. [Pre-Implementation Checklist](#pre-implementation-checklist)

---

## Critical Learnings from Common Errors

### 1. Type System Errors

**Pattern:** Mismatching types between property definitions and assignments

**Common Mistakes:**
- ✗ `Action.Id = "action1"` when `Id` is `int`
- ✗ Assigning string literals to integer properties
- ✗ Type conversions without explicit casting

**Prevention:**
```csharp
// ✓ CORRECT: Match property types exactly
var action = new Action { Id = 1, Title = "Action 1" };

// ✗ WRONG: String to int mismatch
var action = new Action { Id = "action1" }; // CS0029 error
```

**Rule:** ALWAYS verify property types in the model definition before assignment. Never assume types.

---

### 2. Namespace Conflicts and Ambiguity

**Pattern:** Confusion between type names and namespace names

**Common Mistakes:**
- ✗ Using `Blueprint` when both `Sorcha.Blueprint` namespace and `Blueprint` type exist
- ✗ Missing namespace aliases when there are naming conflicts
- ✗ Ambiguous type references causing CS0104 errors

**Prevention:**
```csharp
// ✓ CORRECT: Use namespace alias when conflicts exist
using BpModels = Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Engine;

var blueprint = new BpModels.Blueprint(); // Clear which Blueprint
var action = new BpModels.Action();       // Clear which Action

// ✗ WRONG: Ambiguous reference
using Sorcha.Blueprint;
using Sorcha.Blueprint.Models;
var blueprint = new Blueprint(); // CS0104: Ambiguous reference
```

**Rule:** When a namespace name matches a type name (e.g., `Sorcha.Blueprint` namespace and `Blueprint` type), ALWAYS use a namespace alias.

**Required Aliases for Sorcha:**
```csharp
using BpModels = Sorcha.Blueprint.Models;
```

---

### 3. Missing Using Directives

**Pattern:** Forgetting to include required namespaces for types

**Common Mistakes:**
- ✗ Using `JsonNode` without `using System.Text.Json.Nodes;`
- ✗ Using LINQ methods without `using System.Linq;`
- ✗ Using validation attributes without proper namespaces

**Prevention:**
```csharp
// ✓ CORRECT: Include all required usings
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Sorcha.Blueprint.Models;

// If using implicit usings (enabled in Sorcha), verify GlobalUsings.cs
```

**Rule:** Always check if the type requires an explicit using directive. Don't rely on memory—verify in the codebase.

---

### 4. Incorrect Test Assertion Methods

**Pattern:** Using wrong FluentAssertions method names

**Common Mistakes:**
- ✗ `.ShouldBe()` (from Shouldly, not FluentAssertions)
- ✗ `.ShouldEqual()` (doesn't exist)
- ✗ Mixing assertion libraries

**Prevention:**
```csharp
// ✓ CORRECT: FluentAssertions syntax
result.Should().Be(expected);
result.Should().BeEquivalentTo(expected);
result.Should().NotBeNull();
collection.Should().HaveCount(5);
action.Should().Throw<InvalidOperationException>();

// ✗ WRONG: Incorrect library
result.ShouldBe(expected);        // Shouldly, not FluentAssertions
result.ShouldEqual(expected);     // Doesn't exist
```

**Rule:** Sorcha uses **FluentAssertions**. Always use `.Should()` syntax. Never use Shouldly syntax.

---

### 5. Type Alias Confusion

**Pattern:** Missing or incorrect type aliases in interfaces

**Common Mistakes:**
- ✗ Returning `ValidationResult` from `JsonSchema.Net` when expecting custom result type
- ✗ Not defining type aliases for commonly confused types

**Prevention:**
```csharp
// ✓ CORRECT: Define type alias when needed
using ValidationResult = Sorcha.Blueprint.Engine.Models.ValidationResult;
// NOT using JsonSchema.Net's EvaluationResult

public interface IBlueprintTemplateService
{
    Task<ValidationResult> ValidateAsync(...); // Clear which type
}

// ✗ WRONG: Ambiguous ValidationResult type
public ValidationResult Validate(...); // Which ValidationResult?
```

**Rule:** When multiple libraries provide types with the same name, use type aliases at the file level.

---

### 6. Property Access Errors

**Pattern:** Accessing properties that don't exist or using wrong casing

**Common Mistakes:**
- ✗ `action.name` instead of `action.Title`
- ✗ Accessing properties before null-checking
- ✗ Using wrong property names from mental model vs actual code

**Prevention:**
```csharp
// ✓ CORRECT: Verify property names from actual model
public class Action
{
    public int Id { get; set; }          // NOT string
    public string Title { get; set; }    // NOT Name
    public string? Description { get; set; }
}

// Always check the actual model definition, don't assume
var action = new Action
{
    Id = 1,                    // int, not string
    Title = "My Action",       // Title, not Name
    Description = "Details"
};
```

**Rule:** ALWAYS reference the actual model class definition before writing code that uses it.

---

## Functional Requirements

### 1. Portable Execution Engine

**Requirement:** The Blueprint Execution Engine MUST run both client-side (Blazor WASM) and server-side (ASP.NET Core).

**Implementation Rules:**
- ✓ Target `net10.0` framework only (class library)
- ✓ Zero ASP.NET dependencies
- ✓ Stateless design - no internal state storage
- ✓ Pure functions - deterministic outputs for same inputs
- ✓ Async throughout - all operations return `Task<T>`
- ✗ NO dependency injection of service-specific components
- ✗ NO session state or caching within the engine

**Example:**
```csharp
// ✓ CORRECT: Stateless, portable
public class ExecutionEngine : IExecutionEngine
{
    private readonly ISchemaValidator _validator;
    private readonly IJsonLogicEvaluator _evaluator;

    public async Task<ActionResult> ProcessActionAsync(
        Blueprint blueprint,
        Action action,
        JsonObject data)
    {
        // All state passed as parameters
        // No internal state modification
        // Returns result object
    }
}

// ✗ WRONG: Stateful, has dependencies
public class ExecutionEngine
{
    private ActionResult _lastResult; // Stateful!
    private readonly IDbContext _db;   // Service dependency!
}
```

---

### 2. JSON Schema Validation

**Requirement:** Validate all blueprint action data against JSON Schema Draft 2020-12.

**Implementation Rules:**
- ✓ Use `JsonSchema.Net` version 7.4.0+
- ✓ Support all Draft 2020-12 features
- ✓ Return detailed validation errors with JSON Pointers
- ✓ Support custom schema vocabularies
- ✗ NO fallback to older draft versions
- ✗ NO silent failure on validation errors

**Example:**
```csharp
// ✓ CORRECT: Detailed validation with error paths
public async Task<ValidationResult> ValidateAsync(
    JsonNode schema,
    JsonObject data)
{
    var schemaObj = JsonSchema.FromText(schema.ToJsonString());
    var result = schemaObj.Evaluate(data);

    return new ValidationResult
    {
        IsValid = result.IsValid,
        Errors = result.Errors?.Select(e => new ValidationError
        {
            Path = e.InstanceLocation.ToString(),
            Message = e.Message
        }).ToList()
    };
}
```

---

### 3. JSON Logic Evaluation

**Requirement:** Execute JSON Logic expressions for calculations and conditional routing.

**Implementation Rules:**
- ✓ Use `JsonLogic` version 5.4.3+
- ✓ Support all standard JSON Logic operators
- ✓ Handle errors gracefully with clear messages
- ✓ Support nested data access
- ✗ NO custom operators without documentation
- ✗ NO silent failures on evaluation errors

---

### 4. Selective Disclosure

**Requirement:** Filter action data using JSON Pointers (RFC 6901) to show only disclosed fields to each participant.

**Implementation Rules:**
- ✓ Use `JsonPath.Net` version 2.1.1+
- ✓ Support all RFC 6901 pointer syntax
- ✓ Return only disclosed fields (whitelist approach)
- ✓ Handle nested objects and arrays
- ✗ NO exposure of undisclosed fields in errors
- ✗ NO field name leakage in error messages

---

### 5. Blueprint CRUD Operations

**Requirement:** Provide complete CRUD operations for blueprints via REST API.

**Implementation Rules:**
- ✓ Use Minimal APIs pattern (.NET 10)
- ✓ OpenAPI documentation for ALL endpoints
- ✓ Use `Microsoft.AspNetCore.OpenApi` (NOT Swagger/Swashbuckle)
- ✓ Use `Scalar.AspNetCore` for API documentation UI
- ✓ Validate blueprints before saving
- ✓ Version control for published blueprints
- ✗ NO anonymous blueprint modification
- ✗ NO deletion of published blueprints (soft delete only)

---

## Non-Functional Requirements

### 1. Performance Targets

**Requirements:**

| Operation | Target (p95) | Max (p99) |
|-----------|--------------|-----------|
| GET endpoint | < 200ms | < 500ms |
| POST endpoint | < 500ms | < 1s |
| Schema validation | < 50ms | < 100ms |
| JSON Logic evaluation | < 10ms | < 50ms |
| Disclosure processing | < 20ms | < 100ms |

**Implementation Rules:**
- ✓ Use async/await throughout
- ✓ Avoid blocking operations
- ✓ Use connection pooling
- ✓ Implement output caching where appropriate
- ✗ NO synchronous I/O operations
- ✗ NO Thread.Sleep() in production code

---

### 2. Test Coverage

**Requirements:**

| Component Type | Minimum Coverage | Target Coverage |
|----------------|------------------|------------------|
| Core Libraries | 80% | 90%+ |
| Services | 70% | 85%+ |
| Integration | N/A (scenario-based) | All critical paths |

**Implementation Rules:**
- ✓ Write tests BEFORE or WITH implementation
- ✓ Use AAA pattern (Arrange, Act, Assert)
- ✓ Name tests: `MethodName_Scenario_ExpectedBehavior`
- ✓ Mock external dependencies with Moq
- ✓ Use FluentAssertions for assertions
- ✗ NO tests that depend on external services (use mocks/containers)
- ✗ NO tests that depend on specific execution order

---

### 3. Security Requirements

**Requirements:**
- ✓ All endpoints require authentication (JWT Bearer tokens)
- ✓ Input validation at all boundaries
- ✓ No secrets in source code
- ✓ Encryption at rest for sensitive data (AES-256-GCM)
- ✓ Audit logging for all mutations
- ✗ NO plaintext passwords or keys
- ✗ NO SQL injection vulnerabilities
- ✗ NO XSS vulnerabilities

---

### 4. Observability

**Requirements:**
- ✓ Structured logging with Serilog
- ✓ OpenTelemetry for distributed tracing
- ✓ Include correlation IDs in all logs
- ✓ Health checks (`/health`, `/alive`)
- ✓ Metrics collection (request count, duration, errors)
- ✗ NO sensitive data in logs (PII, secrets)
- ✗ NO excessive logging in hot paths

---

## Architecture Principles

### 1. Microservices-First Architecture

**Principle:** Each service must be independently deployable and maintainable.

**Rules:**
- ✓ Services communicate via well-defined APIs
- ✓ Use .NET Aspire for orchestration
- ✓ Maintain clear service boundaries
- ✓ Minimize inter-service coupling
- ✗ NO direct database access across services
- ✗ NO shared mutable state

---

### 2. Cloud-Native Design

**Principle:** Design for containerization and cloud deployment from day one.

**Rules:**
- ✓ Support Docker containers
- ✓ Support Kubernetes deployment
- ✓ Implement health checks and readiness probes
- ✓ Externalize configuration
- ✓ Design for horizontal scalability
- ✗ NO hardcoded infrastructure endpoints
- ✗ NO file system dependencies (use blob storage)

---

### 3. API-First Design

**Principle:** All REST endpoints MUST have OpenAPI documentation.

**Rules:**
- ✓ Use .NET 10's built-in OpenAPI support (`Microsoft.AspNetCore.OpenApi`)
- ✓ Use Scalar.AspNetCore for interactive documentation
- ✓ Auto-generate OpenAPI specs from code annotations
- ✓ Document request/response models with XML comments
- ✓ Define appropriate HTTP status codes
- ✗ NO Swagger/Swashbuckle (use built-in .NET 10 support)
- ✗ NO undocumented endpoints

**Example:**
```csharp
// ✓ CORRECT: .NET 10 OpenAPI pattern
app.MapGet("/api/blueprints/{id}",
    async (string id, IBlueprintRepository repo) =>
{
    var blueprint = await repo.GetByIdAsync(id);
    return blueprint is not null
        ? Results.Ok(blueprint)
        : Results.NotFound();
})
.WithName("GetBlueprint")
.WithOpenApi(operation => new(operation)
{
    Summary = "Retrieves a blueprint by ID",
    Description = "Returns a single blueprint or 404 if not found"
});

// ✗ WRONG: No OpenAPI metadata
app.MapGet("/api/blueprints/{id}",
    async (string id) => { /* ... */ });
```

---

### 4. Domain-Driven Design

**Principle:** Rich domain models with behavior, not anemic data containers.

**Rules:**
- ✓ Use value objects for immutable data
- ✓ Use fluent builders for complex construction
- ✓ Validate at domain boundaries
- ✓ Use ubiquitous language (Blueprint, Action, Participant, Disclosure)
- ✗ NO anemic domain models (getters/setters only)
- ✗ NO business logic in controllers

---

## Code Quality Standards

### 1. C# Language Standards

**Requirements:**
- ✓ Target .NET 10 (`<TargetFramework>net10.0</TargetFramework>`)
- ✓ Enable nullable reference types (`<Nullable>enable</Nullable>`)
- ✓ Use C# 13 language features
- ✓ Enable implicit usings where appropriate
- ✓ Use file-scoped namespaces
- ✗ NO obsolete APIs without migration plan
- ✗ NO suppressing nullable warnings without justification

**Example:**
```csharp
// ✓ CORRECT: .NET 10, C# 13, nullable enabled
namespace Sorcha.Blueprint.Engine;  // File-scoped namespace

public class ExecutionEngine : IExecutionEngine
{
    private readonly ISchemaValidator _validator;  // Non-nullable

    public ExecutionEngine(ISchemaValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<ActionResult> ProcessAsync(
        Blueprint blueprint,    // Non-nullable
        JsonObject? data)       // Explicitly nullable
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        // ...
    }
}
```

---

### 2. Documentation Standards

**Requirements:**
- ✓ XML documentation for ALL public APIs
- ✓ Document parameters, return values, exceptions
- ✓ Include usage examples for complex APIs
- ✓ Keep comments synchronized with code
- ✗ NO redundant comments (don't say what code obviously does)
- ✗ NO commented-out code in commits

**Example:**
```csharp
/// <summary>
/// Validates action data against the specified JSON Schema.
/// </summary>
/// <param name="schema">JSON Schema Draft 2020-12 schema definition</param>
/// <param name="data">Action data to validate</param>
/// <param name="cancellationToken">Cancellation token</param>
/// <returns>Validation result with detailed error information</returns>
/// <exception cref="ArgumentNullException">Thrown when schema or data is null</exception>
/// <example>
/// <code>
/// var result = await validator.ValidateAsync(schema, data);
/// if (!result.IsValid)
/// {
///     foreach (var error in result.Errors)
///         Console.WriteLine($"{error.Path}: {error.Message}");
/// }
/// </code>
/// </example>
public async Task<ValidationResult> ValidateAsync(
    JsonNode schema,
    JsonObject data,
    CancellationToken cancellationToken = default)
{
    // Implementation
}
```

---

### 3. File Headers

**Requirement:** ALL source files MUST include SPDX license header.

**Format:**
```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
// ...
```

---

### 4. Project File Standards

**Requirements:**
- ✓ Include SPDX license comment
- ✓ Enable nullable reference types
- ✓ Enable XML documentation generation
- ✓ Suppress warning 1591 (missing XML docs) if needed
- ✓ Set appropriate RootNamespace

**Example:**
```xml
<!-- SPDX-License-Identifier: MIT -->
<!-- Copyright (c) 2026 Sorcha Contributors -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <DocumentationFile>Sorcha.Blueprint.Engine.xml</DocumentationFile>
    <NoWarn>$(NoWarn);1591</NoWarn>
    <RootNamespace>Sorcha.Blueprint.Engine</RootNamespace>
  </PropertyGroup>

</Project>
```

---

## Testing Requirements

### 1. Test Framework

**Requirements:**
- ✓ Use xUnit as the test framework
- ✓ Use Moq for mocking
- ✓ Use FluentAssertions for assertions
- ✓ Use Testcontainers for integration tests (Docker)
- ✗ NO mixing test frameworks
- ✗ NO Shouldly or other assertion libraries

---

### 2. Test Naming Convention

**Pattern:** `MethodName_Scenario_ExpectedBehavior`

**Examples:**
```csharp
[Fact]
public void Build_WithoutTitle_ThrowsInvalidOperationException()
{
    // Arrange
    var builder = BlueprintBuilder.Create();

    // Act
    var act = () => builder.Build();

    // Assert
    act.Should().Throw<InvalidOperationException>()
       .WithMessage("*title*required*");
}

[Theory]
[InlineData("")]
[InlineData("ab")]
public void WithTitle_TooShort_ThrowsArgumentException(string title)
{
    // Arrange
    var builder = BlueprintBuilder.Create();

    // Act
    var act = () => builder.WithTitle(title);

    // Assert
    act.Should().Throw<ArgumentException>()
       .WithMessage("*minimum 3 characters*");
}
```

---

### 3. Test Organization

**Requirements:**
- ✓ Follow AAA pattern (Arrange, Act, Assert)
- ✓ One assertion per test (when possible)
- ✓ Use descriptive variable names
- ✓ Group related tests in nested classes
- ✗ NO magic numbers without explanation
- ✗ NO copy-paste tests without clear purpose

---

### 4. Integration Tests

**Requirements:**
- ✓ Use WebApplicationFactory for API tests
- ✓ Use Testcontainers for external dependencies (Redis, databases)
- ✓ Clean up resources after tests
- ✓ Test realistic scenarios end-to-end
- ✗ NO dependencies on external services (use containers)
- ✗ NO hardcoded URLs or ports (use service discovery)

---

## Common Patterns and Anti-Patterns

### 1. Async/Await Patterns

**✓ CORRECT:**
```csharp
// Proper async method
public async Task<Blueprint> GetBlueprintAsync(string id)
{
    var blueprint = await _repository.GetByIdAsync(id);
    return blueprint;
}

// Async all the way down
public async Task<ActionResult> ProcessAsync(...)
{
    var validated = await _validator.ValidateAsync(...);
    var calculated = await _evaluator.EvaluateAsync(...);
    return result;
}
```

**✗ WRONG:**
```csharp
// Blocking on async
public Blueprint GetBlueprint(string id)
{
    return _repository.GetByIdAsync(id).Result; // Deadlock risk!
}

// Unnecessary async
public async Task<int> GetCountAsync()
{
    return await Task.FromResult(42); // Just return 42!
}

// Async void (only for event handlers)
public async void ProcessBlueprint() // Should be async Task
{
    await DoWorkAsync();
}
```

---

### 2. Dependency Injection

**✓ CORRECT:**
```csharp
public class ExecutionEngine : IExecutionEngine
{
    private readonly ISchemaValidator _validator;
    private readonly ILogger<ExecutionEngine> _logger;

    public ExecutionEngine(
        ISchemaValidator validator,
        ILogger<ExecutionEngine> logger)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
}

// Registration in Program.cs
builder.Services.AddScoped<IExecutionEngine, ExecutionEngine>();
builder.Services.AddScoped<ISchemaValidator, SchemaValidator>();
```

**✗ WRONG:**
```csharp
// Service Locator anti-pattern
public class ExecutionEngine
{
    private ISchemaValidator _validator;

    public ExecutionEngine()
    {
        _validator = ServiceLocator.Get<ISchemaValidator>(); // Anti-pattern!
    }
}

// New keyword for dependencies
public class ExecutionEngine
{
    private readonly ISchemaValidator _validator = new SchemaValidator(); // Tight coupling!
}
```

---

### 3. Result Objects vs Exceptions

**✓ CORRECT:** Use result objects for expected failures (validation)
```csharp
public class ValidationResult
{
    public bool IsValid { get; init; }
    public List<ValidationError> Errors { get; init; } = new();
}

public async Task<ValidationResult> ValidateAsync(JsonObject data)
{
    if (/* validation fails */)
    {
        return new ValidationResult
        {
            IsValid = false,
            Errors = new() { new ValidationError { ... } }
        };
    }

    return new ValidationResult { IsValid = true };
}
```

**✓ CORRECT:** Use exceptions for unexpected failures
```csharp
public async Task<Blueprint> GetBlueprintAsync(string id)
{
    ArgumentNullException.ThrowIfNull(id);

    var blueprint = await _repository.GetByIdAsync(id);
    if (blueprint is null)
        throw new BlueprintNotFoundException(id);

    return blueprint;
}
```

**✗ WRONG:** Exceptions for expected conditions
```csharp
public void ValidateBlueprint(Blueprint blueprint)
{
    if (string.IsNullOrEmpty(blueprint.Title))
        throw new Exception("Title required"); // Use ValidationResult instead!
}
```

---

### 4. Null Handling

**✓ CORRECT:**
```csharp
// Nullable reference types
public class Action
{
    public string Title { get; set; } = string.Empty;  // Non-nullable
    public string? Description { get; set; }            // Nullable
}

// Null parameter check (.NET 10)
public void Process(Blueprint blueprint)
{
    ArgumentNullException.ThrowIfNull(blueprint);
    // ...
}

// Null-conditional and coalescing
var title = action?.Title ?? "Untitled";
var count = participants?.Count() ?? 0;
```

**✗ WRONG:**
```csharp
// Ignoring nullability
public void Process(Blueprint? blueprint)
{
    var title = blueprint.Title; // CS8602: Dereference of possibly null reference
}

// Old-style null check
if (blueprint == null)
    throw new ArgumentNullException("blueprint"); // Use nameof(blueprint)
```

---

## Pre-Implementation Checklist

Before writing ANY code, verify:

### ✅ Type System
- [ ] I have checked the ACTUAL model class definitions for property types
- [ ] I am not assuming property types from mental models
- [ ] String properties get string values, int properties get int values
- [ ] I am using the correct property names (Title vs Name, Id vs Identifier, etc.)

### ✅ Namespaces
- [ ] I have identified potential namespace vs type name conflicts
- [ ] I am using namespace aliases (e.g., `BpModels`) where needed
- [ ] I have included all necessary using directives
- [ ] I have checked GlobalUsings.cs for implicit usings

### ✅ Dependencies
- [ ] I understand which libraries are used (FluentAssertions, NOT Shouldly)
- [ ] I am using correct NuGet package versions from .csproj
- [ ] I am using correct API method names (`.Should().Be()` not `.ShouldBe()`)

### ✅ Architecture
- [ ] I understand if this is a portable library (no ASP.NET dependencies)
- [ ] I am following stateless design for the execution engine
- [ ] I am using interfaces for dependency injection
- [ ] I am using async/await throughout

### ✅ Documentation
- [ ] I will include SPDX license headers
- [ ] I will write XML documentation for public APIs
- [ ] I will follow .NET 10 OpenAPI patterns for REST endpoints

### ✅ Testing
- [ ] I will write tests using xUnit
- [ ] I will use FluentAssertions for assertions
- [ ] I will follow naming convention: `MethodName_Scenario_ExpectedBehavior`
- [ ] I will use AAA pattern

---

## Quick Reference: Common Type Mappings

### Blueprint.Models Namespace

```csharp
using BpModels = Sorcha.Blueprint.Models;

// Core types
BpModels.Blueprint      // NOT just "Blueprint" to avoid conflicts
BpModels.Action         // Has int Id, string Title
BpModels.Participant    // Has string Id, string Name
BpModels.Disclosure     // Selective data visibility
BpModels.Condition      // Routing conditions
BpModels.Control        // UI form controls
BpModels.Calculation    // JSON Logic calculations
```

### Action Model Properties (Common Source of Errors)

```csharp
public class Action
{
    public int Id { get; set; }                    // ← INT, not string!
    public string Title { get; set; }              // ← Title, not Name!
    public string? Description { get; set; }       // ← Nullable
    public JsonObject? DataSchema { get; set; }    // ← Nullable
    public List<Disclosure> Disclosures { get; set; }
    public List<Condition> Conditions { get; set; }
    // ... other properties
}
```

### Assertion Library: FluentAssertions

```csharp
// ✓ CORRECT
result.Should().Be(expected);
result.Should().NotBeNull();
collection.Should().HaveCount(5);
action.Should().Throw<Exception>();

// ✗ WRONG (Shouldly syntax)
result.ShouldBe(expected);
result.ShouldNotBeNull();
```

---

## Final Principles

### Code Review Self-Checklist

Before submitting code, ask:

1. **Did I check actual types?** Have I verified property types from the actual model definitions?
2. **Did I handle namespaces correctly?** Am I using aliases where needed to avoid ambiguity?
3. **Did I use the right libraries?** Am I using FluentAssertions (not Shouldly), xUnit (not NUnit)?
4. **Did I follow async patterns?** Am I using async/await consistently without blocking?
5. **Did I write tests?** Do I have unit tests with good coverage?
6. **Did I add documentation?** Do public APIs have XML comments?
7. **Did I follow portability rules?** If this is engine code, is it stateless and portable?
8. **Did I add headers?** Does every file have SPDX license headers?

### The Golden Rule

**When in doubt, CHECK THE ACTUAL CODE, don't assume from memory or mental models.**

---

**Document Maintenance:**
- This document should be updated whenever new patterns or common errors are identified
- Version history should be maintained
- Changes should be reviewed by the architecture team

**References:**
- [Constitution](.specify/constitution.md)
- [Architecture](../reference/architecture.md)
- [Contributing](../CONTRIBUTING.md)
- [Unified Design Summary](.specify/UNIFIED-DESIGN-SUMMARY.md)
