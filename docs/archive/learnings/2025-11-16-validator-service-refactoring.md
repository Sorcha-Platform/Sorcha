# Learnings: Validator Service Refactoring & Test Implementation

**Date:** 2025-11-16
**Topic:** Validator Service Security Boundary, Test Implementation, C# Best Practices
**Status:** Documented

---

## Overview

This document captures key learnings from implementing comprehensive unit tests for the Register Service (Phase 1-2) and discovering the need to refactor `DocketManager` and `ChainValidator` into a secure Validator Service.

---

## 1. Architectural Learnings

### 1.1 Security Boundaries Must Be Established Early

**Learning:** Components with cryptographic responsibilities should be identified and isolated during initial design, not after implementation.

**What Happened:**
- `DocketManager` and `ChainValidator` were initially placed in `Sorcha.Register.Core`
- During test implementation, we realized these components need:
  - Access to cryptographic signing keys
  - Secure execution environment (enclaves/HSMs)
  - Controlled auditability

**Impact:**
- Required architectural refactoring
- Needed to move code to `Sorcha.Validator.Service`
- Test updates across multiple projects

**Best Practice Going Forward:**
```
Security Requirement Checklist:
☐ Does this component perform cryptographic operations?
☐ Does it need access to private keys?
☐ Should it run in a secure enclave/HSM?
☐ Does it require audit logging for compliance?
☐ Should access be restricted to authorized services only?

If ANY answer is YES → Consider dedicated secure service
```

**Example:**
```csharp
// ❌ BAD: Cryptographic logic in general library
namespace Sorcha.Register.Core.Managers
{
    public class DocketManager  // Has access to signing keys!
    {
        public Docket SealDocket(Docket docket)
        {
            // Needs signing key access - security boundary violation!
        }
    }
}

// ✅ GOOD: Cryptographic logic in secure service
namespace Sorcha.Validator.Core.Managers
{
    public class DocketManager  // Runs in secure environment
    {
        private readonly IWalletService _walletService;  // Keys managed externally

        public async Task<Docket> SealDocketAsync(Docket docket)
        {
            // Delegates to Wallet Service for key operations
            var signature = await _walletService.SignAsync(docket.Hash);
        }
    }
}
```

---

### 1.2 Service Decomposition Should Follow Security Requirements

**Learning:** Don't group services purely by functional domain - security requirements should heavily influence service boundaries.

**What Happened:**
- Initial design grouped all Register operations together
- Test implementation revealed different security postures needed:
  - **Register Service:** Read/write blockchain data (CRUD operations)
  - **Validator Service:** Build/validate blocks (needs key access)

**Decision Matrix:**
| Component | Needs Keys? | Needs Enclave? | Service Placement |
|-----------|-------------|----------------|-------------------|
| RegisterManager | ❌ No | ❌ No | Register.Core |
| TransactionManager | ❌ No | ❌ No | Register.Core |
| QueryManager | ❌ No | ❌ No | Register.Core |
| **DocketManager** | ✅ Yes | ✅ Yes | **Validator.Core** |
| **ChainValidator** | ✅ Yes | ✅ Yes | **Validator.Core** |

---

## 2. C# Language and Framework Learnings

### 2.1 Reserved Keywords as Identifiers

**Learning:** C# reserved keywords like `sealed`, `base`, `new`, etc. cannot be used as variable names.

**What Happened:**
```csharp
// ❌ COMPILE ERROR: 'sealed' is a reserved keyword
var sealed = await _docketManager.SealDocketAsync(docket);
//  ^^^^^^
// error CS1002: ; expected

// ✅ FIX: Use descriptive alternative
var sealedDocket = await _docketManager.SealDocketAsync(docket);
```

**Common C# Reserved Keywords to Avoid:**
```csharp
// Keywords that might seem like good variable names:
sealed      // ❌ Use: sealedDocket, sealedBlock
base        // ❌ Use: baseValue, baseClass
new         // ❌ Use: newItem, newInstance
override    // ❌ Use: overrideValue
virtual     // ❌ Use: virtualEntity
abstract    // ❌ Use: abstractModel
interface   // ❌ Use: interfaceType
internal    // ❌ Use: internalData
event       // ❌ Use: eventData, eventInfo
delegate    // ❌ Use: delegateHandler
```

**Workaround (if absolutely necessary):**
```csharp
// You CAN use @-prefix, but it's ugly and confusing
var @sealed = await _docketManager.SealDocketAsync(docket);  // Legal but NOT recommended
```

---

### 2.2 Namespace vs. Type Naming Conflicts

**Learning:** When a type name matches a namespace, you get ambiguous reference errors.

**What Happened:**
```csharp
// PROBLEM: Class named "Register" in namespace containing "Register"
namespace Sorcha.Register.Models
{
    public class Register { }  // ← Type name
}

namespace Sorcha.Register.Core.Tests.Models  // ← Namespace contains "Register"
{
    public class RegisterTests
    {
        public void Test()
        {
            var register = new Register();  // ❌ Error: 'Register' is a namespace
        }
    }
}
```

**Solutions:**

**Option 1: Type Alias (Quick Fix)**
```csharp
using RegisterModel = Sorcha.Register.Models.Register;

namespace Sorcha.Register.Core.Tests.Models;

public class RegisterTests
{
    public void Test()
    {
        var register = new RegisterModel();  // ✅ Works
    }
}
```

**Option 2: Fully Qualified Name**
```csharp
public void Test()
{
    var register = new Sorcha.Register.Models.Register();  // ✅ Works
}
```

**Option 3: Better Naming (Best Long-Term)**
```csharp
// Rename the class to avoid conflict
namespace Sorcha.Register.Models
{
    public class RegisterEntity { }  // or RegisterModel, BlockchainRegister, etc.
}
```

**Best Practice:**
- Avoid naming types the same as their containing namespace segments
- Use suffixes like `Model`, `Entity`, `Data` to differentiate
- Use type aliases in test files when refactoring is not feasible

---

## 3. XUnit Testing Framework Learnings

### 3.1 Required Using Statements

**Learning:** XUnit attributes require explicit `using Xunit;` - they're not included in implicit usings.

**What Happened:**
```csharp
// ❌ COMPILE ERROR: 'FactAttribute' could not be found
public class MyTests
{
    [Fact]  // ← Error: Unknown attribute
    public void MyTest() { }
}
```

**Fix:**
```csharp
using Xunit;  // ✅ Must add explicitly

public class MyTests
{
    [Fact]  // ✅ Now works
    public void MyTest() { }
}
```

**Why:** XUnit is not part of .NET's implicit global usings, unlike `System`, `System.Linq`, etc.

---

### 3.2 NuGet Package Versions and .NET 10

**Learning:** Some NuGet packages don't have versions compatible with pre-release .NET versions.

**What Happened:**
```xml
<!-- ❌ ERROR: Package not found -->
<PackageReference Include="System.ComponentModel.Annotations" Version="10.0.0" />
<!-- Version 10.0.0 doesn't exist on NuGet - .NET 10 is preview -->
```

**Fix:**
```xml
<!-- ✅ SOLUTION 1: Remove - it's in framework -->
<!-- System.ComponentModel.Annotations is included in .NET 10 framework -->

<!-- ✅ SOLUTION 2: Use latest stable version if really needed -->
<PackageReference Include="System.ComponentModel.Annotations" Version="6.0.0" />
```

**Best Practice:**
- Check if package is part of framework before adding explicit reference
- Use latest STABLE version for preview .NET releases
- Consult https://www.nuget.org to verify package versions exist

---

### 3.3 Test Organization and Naming

**Learning:** Clear test organization and naming conventions dramatically improve maintainability.

**Structure Used:**
```
tests/
├── Sorcha.Register.Core.Tests/
│   ├── Models/                    # Domain model tests
│   │   ├── RegisterTests.cs
│   │   ├── TransactionModelTests.cs
│   │   ├── DocketTests.cs
│   │   └── PayloadModelTests.cs
│   ├── Managers/                  # Manager/service tests
│   │   ├── RegisterManagerTests.cs
│   │   ├── TransactionManagerTests.cs
│   │   ├── DocketManagerTests.cs
│   │   └── QueryManagerTests.cs
│   └── Validators/                # Validation logic tests
│       └── ChainValidatorTests.cs
```

**Naming Convention:**
```csharp
// Pattern: [MethodName]_[Scenario]_[ExpectedResult]
[Fact]
public async Task CreateRegisterAsync_WithValidData_ShouldCreateRegister()
[Fact]
public async Task CreateRegisterAsync_WithNullName_ShouldThrowException()
[Fact]
public async Task GetRegisterAsync_WithNonExistingId_ShouldReturnNull()

// For validation tests:
[Theory]
[InlineData("")]
[InlineData(null)]
public void Register_WithInvalidName_ShouldFailValidation(string? invalidName)
```

**Benefits:**
- Instantly understand what's being tested
- Easy to identify missing test coverage
- Self-documenting test suite

---

## 4. FluentAssertions Best Practices

### 4.1 DateTime Comparison with Tolerance

**Learning:** Comparing DateTime.UtcNow requires tolerance for execution time.

```csharp
// ❌ FLAKY: Might fail due to millisecond differences
register.CreatedAt.Should().Be(DateTime.UtcNow);

// ✅ ROBUST: Allow reasonable tolerance
register.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
```

### 4.2 Collection Assertions

```csharp
// ✅ Check count
docket.TransactionIds.Should().HaveCount(3);

// ✅ Check contains
docket.TransactionIds.Should().Contain("tx1");

// ✅ Check all satisfy condition
transactions.Should().AllSatisfy(t =>
    t.SenderWallet.Should().Be("wallet-A"));

// ✅ Check order matters
dockets.Select(d => d.Id).Should().ContainInOrder(1ul, 2ul, 3ul);

// ✅ Check equivalence (order doesn't matter)
result.Items.Should().BeEquivalentTo(expected);
```

---

## 5. Dependency Management Learnings

### 5.1 In-Memory Implementations for Testing

**Learning:** In-memory implementations are crucial for fast, isolated unit tests.

**Pattern Used:**
```csharp
// Production: IRegisterRepository → MongoDbRegisterRepository
// Testing:    IRegisterRepository → InMemoryRegisterRepository

public class InMemoryRegisterRepository : IRegisterRepository
{
    private readonly ConcurrentDictionary<string, Register> _registers = new();

    // Fast, thread-safe, no external dependencies
    public Task<Register?> GetRegisterAsync(string id, CancellationToken ct)
    {
        _registers.TryGetValue(id, out var register);
        return Task.FromResult(register);
    }
}
```

**Benefits:**
- ✅ No database setup required
- ✅ Tests run in milliseconds
- ✅ Perfect isolation between tests
- ✅ Deterministic and repeatable
- ✅ Can run in CI/CD without infrastructure

---

### 5.2 Event Publisher Testing Pattern

**Learning:** Capture events in tests to verify publishing behavior.

```csharp
public class InMemoryEventPublisher : IEventPublisher
{
    private readonly List<PublishedEvent> _publishedEvents = new();

    public Task PublishAsync<TEvent>(string topic, TEvent eventData, CancellationToken ct)
    {
        _publishedEvents.Add(new PublishedEvent
        {
            Topic = topic,
            EventType = typeof(TEvent).Name,
            Event = eventData,
            PublishedAt = DateTime.UtcNow
        });
        return Task.CompletedTask;
    }

    // Test helper methods
    public IEnumerable<TEvent> GetPublishedEvents<TEvent>() where TEvent : class
    {
        return _publishedEvents
            .Where(e => e.Event is TEvent)
            .Select(e => (TEvent)e.Event);
    }

    public void Clear() => _publishedEvents.Clear();
}

// Usage in tests:
var events = _eventPublisher.GetPublishedEvents<RegisterCreatedEvent>();
events.Should().ContainSingle();
```

---

## 6. Test Coverage Learnings

### 6.1 Comprehensive Test Categories

**Learning:** Test multiple dimensions for robust coverage.

**Categories Implemented:**

1. **Happy Path Tests**
   ```csharp
   [Fact]
   public async Task CreateRegisterAsync_WithValidData_ShouldCreateRegister()
   ```

2. **Validation Tests**
   ```csharp
   [Theory]
   [InlineData("")]
   [InlineData(null)]
   public void Register_WithInvalidName_ShouldFailValidation(string? invalidName)
   ```

3. **Error Handling Tests**
   ```csharp
   [Fact]
   public async Task DeleteRegisterAsync_WithWrongTenant_ShouldThrowUnauthorizedException()
   ```

4. **Edge Case Tests**
   ```csharp
   [Fact]
   public void Register_HeightProperty_ShouldAcceptUInt32Values()
   {
       register.Height = uint.MaxValue;  // Test boundary
   }
   ```

5. **Integration Tests**
   ```csharp
   [Fact]
   public async Task SealDocketAsync_ShouldPublishBothEvents()
   {
       // Verify multiple system interactions
   }
   ```

---

## 7. Project Structure Learnings

### 7.1 Solution Organization for Security

**Learning:** Organize solution to reflect security boundaries.

**New Structure:**
```
Sorcha.sln
├── src/
│   ├── Common/                      # Shared, non-sensitive
│   │   ├── Sorcha.Register.Models/
│   │   └── Sorcha.Validator.Models/
│   │
│   ├── Core/                        # Business logic libraries
│   │   ├── Sorcha.Register.Core/   # ← No key access
│   │   └── Sorcha.Validator.Core/  # ← Enclave-safe, key access via Wallet Service
│   │
│   └── Services/                    # Deployable services
│       ├── Sorcha.Register.Service/
│       └── Sorcha.Validator.Service/  # ← Secured environment
│
├── tests/
│   ├── Sorcha.Register.Core.Tests/
│   └── Sorcha.Validator.Core.Tests/   # ← New
```

**Principle:** Physical project structure mirrors security boundaries.

---

## 8. Architectural Patterns Learned

### 8.1 Separation of Concerns in Blockchain Components

**Learning:** Different blockchain operations have different security requirements.

| Operation | Security Needs | Service Placement |
|-----------|---------------|-------------------|
| **Data Storage** | Read/Write permissions | Register Service |
| **Data Queries** | Read permissions | Register Service |
| **Block Building** | Signing keys, enclave | **Validator Service** |
| **Chain Validation** | Hash verification, signature checking | **Validator Service** |
| **Consensus** | Multi-party signatures | **Validator Service** |

---

## 9. Key Takeaways

### For Future Development:

1. **Security First**
   - Identify cryptographic requirements early
   - Establish service boundaries based on security posture
   - Plan for enclave deployment from day one

2. **Test Coverage**
   - Aim for 90%+ coverage on core business logic
   - Test all validation paths (happy + error)
   - Use in-memory implementations for speed

3. **C# Best Practices**
   - Avoid reserved keywords as identifiers
   - Watch for namespace/type naming conflicts
   - Always add `using Xunit;` explicitly

4. **Documentation**
   - Write ADRs for significant architectural decisions
   - Capture learnings immediately while fresh
   - Update all docs when refactoring

5. **Dependencies**
   - Verify NuGet package versions exist
   - Check if package is in framework before adding
   - Use stable versions for preview .NET

---

## 10. Metrics from This Work

**Test Implementation:**
- **186 tests created**
- **181 tests passing** (97% success rate)
- **9 test files** across Models, Managers, Validators
- **Coverage:** Domain models, business logic, validation, integration scenarios

**Code Movement:**
- **2 core classes** moved to secure service boundary
- **2 test files** relocated
- **Multiple documentation updates**
- **1 ADR created**

**Time Investment:**
- Test implementation: ~3 hours
- Architecture refactoring: ~2 hours
- Documentation: ~1 hour
- **Total:** ~6 hours for comprehensive test coverage + architectural improvement

**ROI:**
- Found security boundary violation early
- Established robust test suite for future development
- Documented learnings for team knowledge sharing
- Prevented potential security issues in production

---

## References

- [ADR-005: Validator Service Security Boundary](../../reference/architecture/ADR-005-Validator-Service-Security-Boundary.md)
- [Validator Service Design](../../reference/validator-service-design.md)
- [Register Service Phase 1-2 Completion](../register-service-phase1-2-completion.md)
- [XUnit Documentation](https://xunit.net/)
- [FluentAssertions Documentation](https://fluentassertions.com/)
