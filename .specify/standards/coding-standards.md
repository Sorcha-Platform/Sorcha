# Coding Standards

**Version:** 1.1.0
**Status:** MANDATORY
**Audience:** All developers and AI assistants

---

## Overview

This document defines the coding standards and conventions for the Sorcha project. These standards ensure code consistency, readability, maintainability, and quality across the entire codebase.

---

## 1. C# Language Standards

### Target Framework & Language Version
- **Framework**: .NET 10 (net10.0)
- **Language**: C# 14
- **Nullable Reference Types**: ENABLED (mandatory)
- **Implicit Usings**: ENABLED

### Project Configuration

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <WarningsAsErrors>$(WarningsAsErrors);nullable</WarningsAsErrors>
  </PropertyGroup>
</Project>
```

---

## 2. Naming Conventions

### Casing Rules

| Element | Casing | Example |
|---------|--------|---------|
| Namespace | PascalCase | `Sorcha.Blueprint.Engine` |
| Class | PascalCase | `BlueprintExecutor` |
| Interface | PascalCase with 'I' prefix | `IBlueprintRepository` |
| Method | PascalCase | `ExecuteAsync()` |
| Property | PascalCase | `BlueprintId` |
| Field (private) | camelCase with '_' prefix | `_repository` |
| Field (const) | PascalCase | `MaxRetryCount` |
| Parameter | camelCase | `blueprintId` |
| Local Variable | camelCase | `executionResult` |
| Type Parameter | PascalCase with 'T' prefix | `TEntity`, `TKey` |

### Naming Rules

```csharp
// CORRECT: Descriptive, clear names
public class BlueprintExecutionService { }
public interface IBlueprintRepository { }
public async Task<Blueprint> GetBlueprintByIdAsync(string blueprintId) { }
private readonly IBlueprintRepository _blueprintRepository;

// WRONG: Abbreviations, unclear names
public class BpExecSvc { } // VIOLATION
public interface IRepo { } // VIOLATION
public async Task<Blueprint> GetAsync(string id) { } // Too generic
private readonly IBlueprintRepository repo; // Missing prefix
```

### Async Method Naming

```csharp
// CORRECT: Async suffix
public async Task<Blueprint> CreateBlueprintAsync(BlueprintRequest request) { }

// WRONG: Missing Async suffix
public async Task<Blueprint> CreateBlueprint(BlueprintRequest request) { } // VIOLATION
```

### Boolean Names

```csharp
// CORRECT: Use positive, question format
public bool IsValid { get; set; }
public bool HasActions { get; set; }
public bool CanExecute { get; set; }

// WRONG: Negative or unclear
public bool NotValid { get; set; } // VIOLATION
public bool Flag { get; set; } // VIOLATION
```

---

## 3. Code Organization

### File Structure

```csharp
// 1. File header (if required)
// 2. Usings (organized by System, then third-party, then project)
// 3. Namespace
// 4. Type definition

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Engine;

/// <summary>
/// Executes blueprint workflows with validation and error handling.
/// </summary>
public class BlueprintExecutor : IBlueprintExecutor
{
    // 1. Private fields
    // 2. Constructor(s)
    // 3. Public properties
    // 4. Public methods
    // 5. Private methods

    private readonly ILogger<BlueprintExecutor> _logger;
    private readonly IBlueprintValidator _validator;

    public BlueprintExecutor(
        ILogger<BlueprintExecutor> logger,
        IBlueprintValidator validator)
    {
        _logger = logger;
        _validator = validator;
    }

    public string CurrentBlueprintId { get; private set; } = string.Empty;

    public async Task<ExecutionResult> ExecuteAsync(Blueprint blueprint)
    {
        ValidateBlueprint(blueprint);
        return await ExecuteInternalAsync(blueprint);
    }

    private void ValidateBlueprint(Blueprint blueprint)
    {
        // Implementation
    }
}
```

### One Type Per File

Each class should be in its own file. **Exception**: Nested types and closely related helper types.

---

## 4. Modern C# Features

### Records for DTOs and Value Objects

```csharp
// CORRECT: Use records for immutable data
public record Participant
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Organization { get; init; }
}

public record BlueprintRequest(
    string Title,
    string Description,
    List<Participant> Participants);
```

### Init-only and Required Properties

```csharp
// CORRECT: Use required for mandatory properties
public class Action
{
    public required int Index { get; init; }
    public required string Title { get; set; }
    public string? Description { get; init; } // Optional
}
```

### Pattern Matching

```csharp
// CORRECT: Modern pattern matching
string GetStatusMessage(ExecutionResult result) => result switch
{
    { Success: true, Actions.Count: 0 } => "No actions executed",
    { Success: true } => $"Successfully executed {result.Actions.Count} actions",
    { Success: false, Error: not null } => $"Failed: {result.Error}",
    _ => "Unknown status"
};
```

### Collection Expressions (C# 12+)

```csharp
// CORRECT: Collection expressions
List<string> ids = ["id1", "id2", "id3"];
List<string> combined = [..existingIds, "newId"];
```

### Primary Constructors (C# 12+)

```csharp
// CORRECT: Primary constructors for simple classes
public class BlueprintService(
    IBlueprintRepository repository,
    ILogger<BlueprintService> logger) : IBlueprintService
{
    public async Task<Blueprint> GetByIdAsync(string id)
    {
        logger.LogInformation("Fetching blueprint {Id}", id);
        return await repository.GetByIdAsync(id);
    }
}
```

---

## 5. Async/Await Best Practices

### Async All the Way

```csharp
// CORRECT: Async all the way down
public async Task<Blueprint> GetBlueprintAsync(string id)
{
    var blueprint = await _repository.GetByIdAsync(id);
    await ValidateAsync(blueprint);
    return blueprint;
}

// WRONG: Blocking on async code
public Blueprint GetBlueprint(string id)
{
    return _repository.GetByIdAsync(id).Result; // DEADLOCK RISK
}
```

### Cancellation Tokens

```csharp
// CORRECT: Always accept CancellationToken
public async Task<Blueprint> ExecuteBlueprintAsync(
    string blueprintId,
    CancellationToken cancellationToken = default)
{
    var blueprint = await _repository
        .GetByIdAsync(blueprintId, cancellationToken);
    return blueprint;
}
```

---

## 6. LINQ Best Practices

### Readable LINQ

```csharp
// CORRECT: Clear, readable LINQ
var activeBlueprints = blueprints
    .Where(b => b.IsActive)
    .OrderBy(b => b.CreatedAt)
    .Take(10)
    .ToList();

// WRONG: Overly complex single-line LINQ
var result = blueprints.Where(b => b.IsActive).SelectMany(b => b.Actions).Where(a => a.Status == ActionStatus.Completed).GroupBy(a => a.ParticipantId).Select(g => new { ParticipantId = g.Key, Count = g.Count() }).ToList(); // VIOLATION
```

### Async LINQ (Entity Framework)

```csharp
// CORRECT: Use async methods
var blueprints = await context.Blueprints
    .Where(b => b.IsActive)
    .ToListAsync();

// WRONG: Sync enumeration of IQueryable
var blueprints = context.Blueprints
    .Where(b => b.IsActive)
    .ToList(); // VIOLATION - Use ToListAsync()
```

---

## 7. Exception Handling

### Guard Clauses

```csharp
// CORRECT: Guard clauses at start of method
public async Task<Blueprint> GetBlueprintAsync(string blueprintId)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);

    var blueprint = await _repository.GetByIdAsync(blueprintId);

    if (blueprint == null)
    {
        throw new NotFoundException($"Blueprint {blueprintId} not found");
    }

    return blueprint;
}
```

### Exception Handling Patterns

```csharp
// CORRECT: Specific exceptions first
try
{
    await ExecuteBlueprintAsync(blueprintId);
}
catch (BlueprintValidationException ex)
{
    _logger.LogWarning(ex, "Blueprint validation failed");
    throw;
}
catch (Exception ex)
{
    _logger.LogError(ex, "Unexpected error");
    throw;
}

// WRONG: Swallowing exceptions
try { await ExecuteBlueprintAsync(blueprintId); }
catch { } // VIOLATION - Exception lost
```

---

## 8. Documentation Standards

### XML Documentation

```csharp
/// <summary>
/// Executes a blueprint workflow with the specified execution context.
/// </summary>
/// <param name="blueprint">The blueprint to execute.</param>
/// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
/// <returns>The execution result containing status and output data.</returns>
/// <exception cref="BlueprintValidationException">
/// Thrown when the blueprint fails validation.
/// </exception>
public async Task<ExecutionResult> ExecuteAsync(
    Blueprint blueprint,
    CancellationToken cancellationToken = default)
{
    // Implementation
}
```

### Required Documentation

- All public types (classes, interfaces, enums)
- All public members (methods, properties, events)
- All parameters with `<param>`
- Return values with `<returns>`
- Exceptions with `<exception>`

### Code Comments

```csharp
// CORRECT: Explain WHY, not WHAT
// Apply disclosure rules to ensure participants only see authorized data
var visibleData = ApplyDisclosureRules(data, participant);

// WRONG: Obvious comments
// Get the blueprint by ID
var blueprint = await GetByIdAsync(id); // VIOLATION
```

---

## 9. Performance Best Practices

### String Handling

```csharp
// CORRECT: StringBuilder for concatenation
var builder = new StringBuilder();
foreach (var action in blueprint.Actions)
{
    builder.AppendLine($"Action {action.Index}: {action.Title}");
}

// WRONG: String concatenation in loop
string result = "";
foreach (var action in blueprint.Actions)
{
    result += $"Action {action.Index}: {action.Title}\n"; // VIOLATION
}
```

### Collection Pre-allocation

```csharp
// CORRECT: Pre-allocate known capacity
var results = new List<ExecutionResult>(blueprint.Actions.Count);
```

---

## 10. Anti-Patterns to Avoid

### Magic Numbers

```csharp
// WRONG
if (blueprint.Actions.Count > 100) { }

// CORRECT
private const int MaxActionsPerBlueprint = 100;
if (blueprint.Actions.Count > MaxActionsPerBlueprint) { }
```

### Large Methods

Methods should be under 50 lines. Extract to smaller methods.

### God Classes

Classes should have a single responsibility. Split large classes with multiple responsibilities.

---

## Compliance Checklist

Before committing code:

- [ ] Nullable reference types enabled
- [ ] All public APIs have XML documentation
- [ ] Async methods have 'Async' suffix
- [ ] No compiler warnings
- [ ] Naming conventions followed
- [ ] Modern C# features used appropriately
- [ ] LINQ queries are readable
- [ ] Exceptions handled properly
- [ ] Tests follow naming conventions
- [ ] Code coverage meets minimum (80%)

---

## References

- [C# Coding Conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- [Framework Design Guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/)
- [Async Best Practices](https://learn.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)
