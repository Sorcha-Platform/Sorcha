# .NET 10 / C# 14 Patterns Reference

## Contents
- C# 14 Language Features
- Project Configuration
- Dependency Injection Patterns
- Common Anti-Patterns

---

## C# 14 Language Features

### Primary Constructors

Used throughout services for clean dependency injection.

```csharp
// GOOD - Primary constructor for services
public class BlueprintService(IBlueprintStore store) : IBlueprintService
{
    // Parameter captured as field automatically
    public async Task<Blueprint?> GetByIdAsync(string id)
        => await store.GetByIdAsync(id);
}

// GOOD - Multiple dependencies
public class PublishService(
    IBlueprintStore blueprintStore,
    IPublishedBlueprintStore publishedStore) : IPublishService
{
    // Both parameters available
}
```

### Collection Expressions

Replace `new List<T>()` with target-typed `[]`:

```csharp
// GOOD - Clean default initialization
public List<Participant> Participants { get; set; } = [];
public IEnumerable<string> Criteria { get; set; } = [];
public Dictionary<string, JsonNode>? Calculations { get; set; } = [];

// BAD - Verbose pre-C#12 syntax
public List<Participant> Participants { get; set; } = new List<Participant>();
```

### Required Members

Enforce mandatory initialization in DTOs:

```csharp
// GOOD - Required properties must be set
public class WalletDto
{
    public required string Address { get; set; }
    public required string Name { get; set; }
    public required string Algorithm { get; set; }
    public DateTime CreatedAt { get; set; }  // Optional
}

// Usage enforces initialization
var wallet = new WalletDto
{
    Address = "0x123",
    Name = "Primary",
    Algorithm = "ED25519"  // All required members must be set
};
```

### Records for Value Objects

```csharp
// GOOD - Record with positional parameters
public record PagedResult<T>(
    IEnumerable<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
}

// GOOD - Record for domain value objects
public record Mnemonic
{
    private readonly NBitcoin.Mnemonic _mnemonic;

    public string Phrase => _mnemonic.ToString();
    public int WordCount => _mnemonic.Words.Length;

    // Override ToString to prevent accidental logging of sensitive data
    public override string ToString() => $"Mnemonic({WordCount} words)";
}
```

### Raw String Literals

Used for multi-line OpenAPI documentation:

```csharp
document.Info.Description = """
    # Wallet Service API

    ## Overview
    The Wallet Service provides **cryptographic wallet management**
    and **transaction signing** capabilities.

    ## Supported Algorithms
    - **ED25519**: Edwards-curve (default, fastest)
    - **NIST P-256**: FIPS 186-4 compliant
    - **RSA-4096**: Traditional RSA
    """;
```

---

## Project Configuration

### Standard Library Project

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Sorcha.Blueprint.Models</RootNamespace>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>
</Project>
```

### Web Service Project

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.1" />
    <PackageReference Include="Scalar.AspNetCore" Version="2.12.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Common\Sorcha.ServiceDefaults\..." />
  </ItemGroup>
</Project>
```

### Test Project with Global Usings

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <Using Include="Xunit" />
    <Using Include="Moq" />
    <Using Include="FluentAssertions" />
  </ItemGroup>
</Project>
```

---

## Dependency Injection Patterns

### Service Registration

```csharp
// Register interfaces to implementations
builder.Services.AddScoped<IBlueprintService, BlueprintService>();
builder.Services.AddSingleton<IBlueprintStore, InMemoryBlueprintStore>();

// Use extension methods for complex registrations
builder.Services.AddWalletService(builder.Configuration);
builder.Services.AddServiceClients(builder.Configuration);
```

### Constructor Injection with Primary Constructors

```csharp
// GOOD - Primary constructor keeps it clean
public class ActionExecutionService(
    IActionResolverService actionResolver,
    ITransactionBuilderService txBuilder,
    INotificationService notifications,
    ILogger<ActionExecutionService> logger) : IActionExecutionService
{
    public async Task<ExecutionResult> ExecuteAsync(ExecutionRequest request)
    {
        logger.LogInformation("Executing action {ActionId}", request.ActionId);
        // Use injected services
    }
}
```

---

## Common Anti-Patterns

### WARNING: Using JsonNode with JsonSchema.Net

**The Problem:**

```csharp
// BAD - JsonSchema.Net Evaluate() requires JsonElement, not JsonNode
var node = JsonNode.Parse(json);
var result = schema.Evaluate(node);  // COMPILATION ERROR
```

**Why This Breaks:**
1. `Evaluate()` signature expects `JsonElement`
2. No implicit conversion between `JsonNode` and `JsonElement`
3. This is a common mistake in the codebase

**The Fix:**

```csharp
// GOOD - Use JsonElement for schema validation
JsonElement element = JsonSerializer.Deserialize<JsonElement>(json);
var result = schema.Evaluate(element);
```

### WARNING: Missing CancellationToken

**The Problem:**

```csharp
// BAD - No cancellation support
public async Task<Wallet> GetWalletAsync(string id)
{
    return await _repository.GetByIdAsync(id);
}
```

**The Fix:**

```csharp
// GOOD - Pass CancellationToken through the call chain
public async Task<Wallet?> GetWalletAsync(
    string id,
    CancellationToken cancellationToken = default)
{
    return await _repository.GetByIdAsync(id, cancellationToken);
}
```

### WARNING: Not Using `is null` Pattern

**The Problem:**

```csharp
// BAD - Can be overridden by custom == operator
if (blueprint == null) { }
```

**The Fix:**

```csharp
// GOOD - Always uses reference equality
if (blueprint is null) { }
if (blueprint is not null) { }