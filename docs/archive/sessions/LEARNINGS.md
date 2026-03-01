# Sorcha Development Learnings

**Purpose:** Capture important learnings, best practices, and gotchas discovered during development.

**Last Updated:** 2025-11-16

---

## Table of Contents

1. [Testing with XUnit](#testing-with-xunit)
2. [C# Language Gotchas](#c-language-gotchas)
3. [Architecture Decisions](#architecture-decisions)
4. [Security Best Practices](#security-best-practices)

---

## Testing with XUnit

### Best Practices

**1. Global Usings for Test Projects**
Always add XUnit, FluentAssertions, and Moq to global usings in `.csproj`:

```xml
<ItemGroup>
  <Using Include="Xunit" />
  <Using Include="FluentAssertions" />
  <Using Include="Moq" />
</ItemGroup>
```

**2. Test Naming Convention**
Follow the pattern: `MethodName_Scenario_ExpectedBehavior`

```csharp
[Fact]
public void CreateDocket_WithNoTransactions_ThrowsArgumentException() { }

[Fact]
public void VerifyDocketHash_WithValidDocket_ReturnsTrue() { }
```

**3. Theory vs Fact**
- Use `[Fact]` for single-case tests
- Use `[Theory]` with `[InlineData]` for multiple test cases

```csharp
[Theory]
[InlineData("")]
[InlineData(null)]
[InlineData("   ")]
public void CreateDocket_WithInvalidRegisterId_ThrowsException(string registerId) { }
```

**4. Async Testing**
XUnit fully supports async tests:

```csharp
[Fact]
public async Task CreateDocketAsync_WithValidData_ReturnsDocket()
{
    // Arrange
    var manager = new DocketManager(mockRepo.Object, mockPublisher.Object);

    // Act
    var result = await manager.CreateDocketAsync("reg_123", ["tx1"], CancellationToken.None);

    // Assert
    result.Should().NotBeNull();
}
```

**5. FluentAssertions Best Practices**
Use FluentAssertions for more readable assertions:

```csharp
// Good
result.Should().NotBeNull();
result.Hash.Should().NotBeNullOrWhiteSpace();
result.TransactionIds.Should().HaveCount(3);
result.State.Should().Be(DocketState.Init);

// Avoid
Assert.NotNull(result);
Assert.NotEmpty(result.Hash);
Assert.Equal(3, result.TransactionIds.Count);
```

### Common Gotchas

**1. Async Void Tests**
❌ WRONG:
```csharp
[Fact]
public async void CreateDocket_Test() { } // XUnit won't await this!
```

✅ CORRECT:
```csharp
[Fact]
public async Task CreateDocket_Test() { }
```

**2. Collection Fixtures**
For shared test context across multiple test classes, use `ICollectionFixture<T>`:

```csharp
[CollectionDefinition("Database collection")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }

[Collection("Database collection")]
public class MyTests
{
    private readonly DatabaseFixture _fixture;

    public MyTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }
}
```

**3. Test Parallelization**
XUnit runs test classes in parallel by default. To disable:

```csharp
[Collection("Sequential")] // All tests with this attribute run sequentially
public class MyTests { }
```

Or in `xunit.runner.json`:
```json
{
  "parallelizeAssembly": false,
  "parallelizeTestCollections": false
}
```

---

## C# Language Gotchas

### Reserved Keywords and Contextual Keywords

**1. Using `@` Prefix for Reserved Words**
If you need to use a reserved keyword as an identifier, prefix with `@`:

```csharp
// If "event" is needed as a variable name
var @event = new Event(); // Valid

// Common cases
var @class = "MyClass";
var @namespace = "MyNamespace";
var @default = GetDefault();
```

**2. Contextual Keywords**
Some keywords are only reserved in specific contexts:

```csharp
// "value" is a contextual keyword in property setters
public string Name
{
    get => _name;
    set => _name = value; // "value" is special here
}

// But can be used elsewhere
var value = 123; // Valid outside property context

// Other contextual keywords:
// - add, remove (event accessors)
// - get, set (property accessors)
// - value (property/indexer setters)
// - partial, where, select, from, etc. (LINQ/generics)
```

**3. `var` vs Explicit Types**
**Best Practice**: Use `var` when the type is obvious from the right side:

```csharp
// Good uses of var
var customers = new List<Customer>();
var name = "John Doe";
var result = await GetDataAsync();

// Avoid var when type is not obvious
Customer customer = GetCustomer(); // Better than var here
IEnumerable<string> names = ProcessNames(); // Type clarity
```

**4. Nullable Reference Types (.NET 10)**
Always enable nullable reference types in `.csproj`:

```xml
<Nullable>enable</Nullable>
```

This helps catch null reference bugs at compile time:

```csharp
// With nullable enabled
public class Docket
{
    public required string RegisterId { get; set; } // Must be set
    public string? OptionalField { get; set; } // Can be null
}
```

**5. Required Members (C# 11+)**
Use `required` keyword instead of constructor parameters for DTOs:

```csharp
// Modern approach
public class ChainValidationResult
{
    public required string RegisterId { get; set; }
    public bool IsValid { get; set; }
}

// Usage
var result = new ChainValidationResult
{
    RegisterId = "reg_123" // Compile error if missing
};
```

---

## Architecture Decisions

### Security-Driven Component Placement

**Learning**: Components performing cryptographic operations must run in secured environments.

**Example**: DocketManager and ChainValidator Migration

**Before:**
- Located in `Sorcha.Register.Core` (general business logic layer)
- Mixed with storage and repository concerns
- No security isolation

**After:**
- Moved to `Sorcha.Validator.Service` (secured service layer)
- Isolated in environment with encryption key access
- Supports future enclave deployment (Intel SGX/AMD SEV)

**Rationale:**
1. **Security Isolation**: Cryptographic operations (SHA256 hashing) require secured environment
2. **Separation of Concerns**: Validation logic separate from storage logic
3. **Enclave Support**: Pure validation logic can run in secure enclaves
4. **Zero-Trust Architecture**: Components with crypto operations need explicit security boundaries

**Decision Criteria for Component Placement:**
- ✅ **Sorcha.Validator.Service** (SECURED):
  - Cryptographic operations (hashing, signing)
  - Chain integrity validation
  - Consensus coordination
  - Access to encryption keys

- ✅ **Sorcha.Register.Core** (BUSINESS LOGIC):
  - Repository interfaces
  - Storage abstractions
  - Data models
  - Non-cryptographic business logic

- ✅ **Sorcha.Validator.Core** (PURE LIBRARY):
  - Stateless validation functions
  - No I/O operations
  - No network calls
  - Enclave-safe logic

### Project Reference Hygiene

**Learning**: Keep dependency graphs clean and unidirectional.

**Rule**: Services can reference Core/Common libraries, but not vice versa.

```
Services (Validator.Service)
    ↓ references
Core (Register.Core)
    ↓ references
Common (Register.Models)
```

**Anti-pattern**: Core libraries should NEVER reference Services.

---

## Security Best Practices

### Cryptographic Operations

**1. Use Built-in .NET Crypto**
Always use `System.Security.Cryptography` for hashing:

```csharp
// Good
using System.Security.Cryptography;
var hashBytes = SHA256.HashData(bytes);

// Avoid custom implementations
```

**2. Deterministic Serialization for Hashing**
When hashing objects, ensure deterministic serialization:

```csharp
// Sort collections before hashing
var hashInput = new
{
    docket.Id,
    docket.RegisterId,
    docket.PreviousHash,
    TransactionIds = docket.TransactionIds.OrderBy(t => t).ToList(), // SORT!
    TimeStamp = docket.TimeStamp.ToString("O") // ISO 8601 for consistency
};
```

**3. Hash Comparison**
Use case-insensitive comparison for hex strings:

```csharp
return calculatedHash.Equals(docket.Hash, StringComparison.OrdinalIgnoreCase);
```

**4. Document Security Requirements**
Add comments indicating security-sensitive code:

```csharp
/// <summary>
/// Calculates the hash of a docket using SHA256
/// SECURITY: This method performs cryptographic operations and must run in a secure environment
/// </summary>
private string CalculateDocketHash(Docket docket) { }
```

### Input Validation

**Use ArgumentException.ThrowIfNullOrWhiteSpace** (C# 11+):

```csharp
public async Task<Docket> CreateDocketAsync(string registerId, ...)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
    ArgumentNullException.ThrowIfNull(transactionIds);
    // ...
}
```

---

## Documentation Best Practices

### XML Documentation

Always document public APIs with XML comments:

```csharp
/// <summary>
/// Validates the entire docket chain for a register
/// </summary>
/// <param name="registerId">The unique identifier of the register</param>
/// <param name="cancellationToken">Cancellation token</param>
/// <returns>Chain validation result with errors, warnings, and info</returns>
public async Task<ChainValidationResult> ValidateDocketChainAsync(
    string registerId,
    CancellationToken cancellationToken = default)
```

### Architectural Decision Records (ADRs)

Document major architectural decisions in architecture.md:

```markdown
**Architectural Note:**
DocketManager and ChainValidator were moved from `Sorcha.Register.Core`
to `Sorcha.Validator.Service` to ensure they run in a secured environment
with proper access to encryption keys and cryptographic operations.
```

---

## Related Documentation

- [Architecture](architecture.md)
- [Validator Service Design](validator-service-design.md)
- [Testing Guide](testing.md)
- [Security Guidelines](security-guidelines.md) (planned)

---

**Contributors:**
- Claude Code (Anthropic) - Initial learnings compilation

## Component Placement Enforcement

### Permanent Requirements Added (2025-11-16)

**Functional Requirement FR-SEC-001**: Components performing cryptographic operations MUST run in secured environments.

**Non-Functional Requirement NFR-SEC-002**: Security isolation for cryptographic validation components.

**Implementation:**
- DocketManager: Moved from Register.Core → Validator.Service (performs SHA256 hashing)
- ChainValidator: Moved from Register.Core → Validator.Service (validates chain integrity)

**Testing Verification:**
- Unit tests confirm no Register.Core dependencies on moved components
- Integration tests verify Validator.Service cryptographic operations
- Security tests validate isolation boundaries

**Documentation:**
- architecture.md: Permanent architectural note added
- validator-service-design.md: Component responsibilities documented
- sorcha-register-service.md: Architectural refinement section updated
- LEARNINGS.md: Decision criteria for future component placement

---

**Changelog:**
- 2025-11-16: Added component placement enforcement requirements
- 2025-11-16: Initial creation with XUnit, C# keywords, and security learnings
