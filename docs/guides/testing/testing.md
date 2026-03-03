# Sorcha Testing Guide

## Overview

Sorcha follows a comprehensive testing strategy with multiple layers of tests to ensure code quality, reliability, and maintainability. This document outlines the testing approach, conventions, and best practices for the Sorcha project.

**Last Updated:** 2025-01-04
**Version:** 1.0.0

## Table of Contents

- [Testing Philosophy](#testing-philosophy)
- [Test Projects](#test-projects)
- [Testing Layers](#testing-layers)
- [Test Conventions](#test-conventions)
- [Running Tests](#running-tests)
- [Code Coverage](#code-coverage)
- [CI/CD Integration](#cicd-integration)
- [Writing Tests](#writing-tests)
- [Best Practices](#best-practices)

## Testing Philosophy

Sorcha's testing strategy is based on the following principles:

1. **Test Pyramid**: Majority of tests are unit tests, followed by integration tests, with fewer end-to-end tests
2. **Fast Feedback**: Tests should run quickly to enable rapid development cycles
3. **Isolation**: Unit tests should be isolated and not depend on external systems
4. **Clarity**: Test names and assertions should clearly communicate intent
5. **Maintainability**: Tests are first-class code and should be maintained with the same rigor as production code
6. **Coverage Goals**: Target 80%+ code coverage for business logic, 60%+ overall

## Test Projects

Sorcha includes the following test projects:

### Unit Test Projects

| Project | Purpose | Target |
|---------|---------|--------|
| **Sorcha.Blueprint.Models.Tests** | Tests for domain models | Blueprint, Action, Participant, etc. |
| **Sorcha.Blueprint.Fluent.Tests** | Tests for fluent builders | BlueprintBuilder, ActionBuilder, etc. |
| **Sorcha.Blueprint.Schemas.Tests** | Tests for schema management | SchemaLibraryService, repositories |
| **Sorcha.Blueprint.Engine.Tests** | Tests for Engine API | Health checks, endpoints (when implemented) |
| **Sorcha.Blueprint.Designer.Tests** | Tests for Blazor components | UI components, client logic |

### Integration Test Projects

| Project | Purpose |
|---------|---------|
| **Sorcha.Integration.Tests** | End-to-end integration tests across multiple components |

## Testing Layers

### 1. Unit Tests

**Purpose**: Test individual classes and methods in isolation

**Characteristics**:
- Fast execution (< 100ms per test)
- No external dependencies (use mocks/fakes)
- Test one thing at a time
- High code coverage target (80%+)

**Technologies**:
- xUnit 2.9.3
- Moq 4.20.72
- FluentAssertions 7.0.1

**Example**:
```csharp
[Fact]
public void Build_WithoutTitle_ShouldThrowInvalidOperationException()
{
    // Arrange
    var builder = BlueprintBuilder.Create()
        .WithDescription("Test description");

    // Act & Assert
    builder.Invoking(b => b.Build())
        .Should().Throw<InvalidOperationException>()
        .WithMessage("*title*");
}
```

### 2. Component Tests

**Purpose**: Test Blazor components in isolation

**Characteristics**:
- Test component rendering
- Test user interactions
- Test component state management
- Verify correct markup output

**Technologies**:
- bUnit 1.33.4
- xUnit 2.9.3

**Example**:
```csharp
[Fact]
public void BlueprintCard_ShouldRenderTitle()
{
    // Arrange
    using var ctx = new TestContext();
    var blueprint = new Blueprint { Title = "Test Blueprint" };

    // Act
    var cut = ctx.RenderComponent<BlueprintCard>(parameters => parameters
        .Add(p => p.Blueprint, blueprint));

    // Assert
    cut.Find("h3").TextContent.Should().Be("Test Blueprint");
}
```

### 3. Integration Tests

**Purpose**: Test interactions between multiple components

**Characteristics**:
- Test complete workflows
- Test service-to-service communication
- Test database operations (when implemented)
- Verify end-to-end scenarios

**Technologies**:
- xUnit 2.9.3
- WebApplicationFactory (for API testing)
- Testcontainers (planned, for database testing)

**Example**:
```csharp
[Fact]
public void CreateBlueprintWithFluentAPI_ShouldSucceed()
{
    // Arrange & Act
    var blueprint = BlueprintBuilder.Create()
        .WithTitle("Purchase Order Workflow")
        .WithDescription("A complete purchase order workflow")
        .AddParticipant("buyer", p => p.Named("Buyer"))
        .AddParticipant("seller", p => p.Named("Seller"))
        .AddAction(0, a => a
            .WithTitle("Submit Order")
            .SentBy("buyer")
            .RouteToNext("seller"))
        .Build();

    // Assert
    blueprint.Should().NotBeNull();
    blueprint.Participants.Should().HaveCount(2);
    blueprint.Actions.Should().HaveCount(1);
}
```

### 4. Contract Tests

**Purpose**: Verify API contracts match OpenAPI specifications

**Status**: Planned for future implementation

**Technologies**:
- Pact or similar contract testing framework

## Test Conventions

### Naming Convention

Tests follow the pattern: `MethodName_Scenario_ExpectedBehavior`

**Examples**:
```csharp
// Good names
Build_WithoutTitle_ShouldThrowInvalidOperationException()
AddParticipant_WithValidData_ShouldAddParticipantToBlueprint()
GetSchemaByIdAsync_WithValidId_ShouldReturnSchema()

// Avoid
TestBuild()  // Too vague
BuildTest()  // Not descriptive
```

### Test Structure (AAA Pattern)

All tests follow the **Arrange-Act-Assert** pattern:

```csharp
[Fact]
public void ExampleTest()
{
    // Arrange: Set up test data and preconditions
    var builder = BlueprintBuilder.Create();
    var title = "Test Blueprint";

    // Act: Execute the method under test
    builder.WithTitle(title);
    var blueprint = builder.Build();

    // Assert: Verify the expected outcome
    blueprint.Title.Should().Be(title);
}
```

### Test Attributes

| Attribute | Purpose | Example |
|-----------|---------|---------|
| `[Fact]` | Single test case | `[Fact] public void Test() { }` |
| `[Theory]` | Parameterized test | `[Theory][InlineData("value1")][InlineData("value2")]` |
| `[Trait]` | Categorize tests | `[Trait("Category", "Integration")]` |
| `[Skip]` | Temporarily skip test | `[Fact(Skip = "Waiting for feature X")]` |

### Assertion Style

Use FluentAssertions for readable, expressive assertions:

```csharp
// FluentAssertions (Preferred)
result.Should().NotBeNull();
result.Should().BeOfType<Blueprint>();
blueprint.Participants.Should().HaveCount(2);
action.Title.Should().Be("Submit Order");
exception.Should().BeOfType<InvalidOperationException>()
    .Which.Message.Should().Contain("title");

// Avoid xUnit asserts
Assert.NotNull(result);
Assert.Equal(2, blueprint.Participants.Count);
```

## Running Tests

### Command Line

```bash
# Run all tests
dotnet test

# Run tests with verbose output
dotnet test --verbosity normal

# Run tests in a specific project
dotnet test tests/Sorcha.Blueprint.Api.Tests
dotnet test tests/Sorcha.Cryptography.Tests

# Run tests in parallel (default)
dotnet test --parallel

# Run tests with code coverage
dotnet test --collect:"XPlat Code Coverage"

# Filter tests by name
dotnet test --filter "FullyQualifiedName~BlueprintBuilder"
dotnet test --filter "FullyQualifiedName~CryptoModule"

# Filter tests by category
dotnet test --filter "Category=Integration"

# Run specific crypto algorithm tests
dotnet test tests/Sorcha.Cryptography.Tests --filter "FullyQualifiedName~ED25519"
dotnet test tests/Sorcha.Cryptography.Tests --filter "FullyQualifiedName~NISTP256"
dotnet test tests/Sorcha.Cryptography.Tests --filter "FullyQualifiedName~RSA4096"

# Watch mode (auto-rerun on file changes)
dotnet watch test --project tests/Sorcha.Cryptography.Tests
```

### Visual Studio

1. Open Test Explorer (Test > Test Explorer)
2. Click "Run All" to execute all tests
3. Right-click individual tests to run/debug
4. Use search box to filter tests

### Visual Studio Code

1. Install the .NET Test Explorer extension
2. Tests appear in the Test Explorer view
3. Click the play button next to tests to run
4. Use CodeLens links above test methods

## Code Coverage

### Collecting Coverage

Code coverage is collected automatically in CI/CD and can be collected locally:

```bash
# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Coverage reports are generated in:
# tests/*/TestResults/*/coverage.cobertura.xml
```

### Coverage Reports

Generate HTML reports using ReportGenerator:

```bash
# Install ReportGenerator
dotnet tool install --global dotnet-reportgenerator-globaltool

# Generate HTML report
reportgenerator \
    -reports:"tests/**/coverage.cobertura.xml" \
    -targetdir:"coverage-report" \
    -reporttypes:Html

# Open the report
open coverage-report/index.html
```

### Coverage Targets

| Category | Target | Current |
|----------|--------|---------|
| Overall Solution | 60%+ | TBD |
| Business Logic (Models, Fluent) | 80%+ | TBD |
| Schema Management | 70%+ | TBD |
| API Endpoints | 70%+ | TBD |

## CI/CD Integration

### Build Pipeline Phases

The CI/CD pipeline runs tests in multiple phases:

```yaml
Build Pipeline:
1. Restore Dependencies
2. Build Solution
3. Run Unit Tests (Parallel)
   - Sorcha.Blueprint.Models.Tests
   - Sorcha.Blueprint.Fluent.Tests
   - Sorcha.Blueprint.Schemas.Tests
   - Sorcha.Blueprint.Engine.Tests
   - Sorcha.Blueprint.Designer.Tests
4. Run Component Tests
   - Sorcha.Blueprint.Designer.Tests (Blazor components)
5. Run Integration Tests
   - Sorcha.Integration.Tests
6. Collect Code Coverage
7. Upload Coverage to Codecov
8. Generate Test Reports
```

### Test Failure Handling

- **Unit Test Failure**: Build fails immediately
- **Integration Test Failure**: Build fails, logs are uploaded as artifacts
- **Coverage Below Target**: Warning issued, but build continues

## Writing Tests

### Test File Organization

Each test file should mirror the structure of the source file:

```
src/
  Common/
    Sorcha.Blueprint.Models/
      Blueprint.cs
      Participant.cs
      Action.cs

tests/
  Sorcha.Blueprint.Models.Tests/
    BlueprintTests.cs
    ParticipantTests.cs
    ActionTests.cs
```

### Test Class Structure

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models;
using System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Models.Tests;

public class BlueprintTests
{
    // Group related tests together

    #region Constructor Tests

    [Fact]
    public void Constructor_ShouldInitializeWithDefaults()
    {
        // Test implementation
    }

    #endregion

    #region Validation Tests

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Title_WithEmptyValue_ShouldFailValidation(string? title)
    {
        // Test implementation
    }

    #endregion

    #region Behavior Tests

    [Fact]
    public void Equals_WithSameId_ShouldReturnTrue()
    {
        // Test implementation
    }

    #endregion
}
```

### Testing Async Methods

```csharp
[Fact]
public async Task GetSchemaByIdAsync_WithValidId_ShouldReturnSchema()
{
    // Arrange
    var service = new SchemaLibraryService();

    // Act
    var schema = await service.GetSchemaByIdAsync("person");

    // Assert
    schema.Should().NotBeNull();
    schema!.Metadata.Id.Should().Be("person");
}
```

### Testing Exceptions

```csharp
[Fact]
public void Build_WithoutTitle_ShouldThrowInvalidOperationException()
{
    // Arrange
    var builder = BlueprintBuilder.Create();

    // Act & Assert
    builder.Invoking(b => b.Build())
        .Should().Throw<InvalidOperationException>()
        .WithMessage("*title*at least*");
}
```

### Using Mocks

```csharp
[Fact]
public async Task GetAllSchemasAsync_ShouldCallRepository()
{
    // Arrange
    var mockRepo = new Mock<ISchemaRepository>();
    mockRepo.Setup(r => r.GetAllSchemasAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<SchemaDocument>());

    var service = new SchemaLibraryService();
    service.AddRepository(mockRepo.Object);

    // Act
    await service.GetAllSchemasAsync();

    // Assert
    mockRepo.Verify(r => r.GetAllSchemasAsync(It.IsAny<CancellationToken>()), Times.Once);
}
```

## Cryptography Library Testing

### Overview

The Sorcha.Cryptography library includes comprehensive tests for:
- **Key Generation**: ED25519, NISTP256, RSA4096
- **Digital Signatures**: Sign and verify operations
- **Encryption**: Symmetric and asymmetric encryption
- **Hashing**: SHA-256, SHA-512
- **Encoding**: Base64, Hex utilities

### Running Crypto Tests

```bash
# Run all cryptography tests
dotnet test tests/Sorcha.Cryptography.Tests

# Run specific algorithm tests
dotnet test tests/Sorcha.Cryptography.Tests --filter "FullyQualifiedName~ED25519"
dotnet test tests/Sorcha.Cryptography.Tests --filter "FullyQualifiedName~NISTP256"
dotnet test tests/Sorcha.Cryptography.Tests --filter "FullyQualifiedName~RSA4096"

# Run with detailed output
dotnet test tests/Sorcha.Cryptography.Tests --logger "console;verbosity=detailed"

# Run with code coverage
dotnet test tests/Sorcha.Cryptography.Tests --collect:"XPlat Code Coverage"
```

### Performance Testing Crypto Operations

#### Example: Key Generation Performance Test

```csharp
using System.Diagnostics;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;

public class CryptoPerformanceTests
{
    [Fact]
    public async Task KeyGeneration_ED25519_PerformanceTest()
    {
        // Arrange
        var cryptoModule = new CryptoModule();
        const int iterations = 1000;
        var stopwatch = Stopwatch.StartNew();

        // Act
        for (int i = 0; i < iterations; i++)
        {
            var result = await cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
            result.IsSuccess.Should().BeTrue();
        }

        stopwatch.Stop();

        // Assert
        var avgTimeMs = stopwatch.ElapsedMilliseconds / (double)iterations;
        Console.WriteLine($"ED25519 Key Generation: {iterations} iterations in {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Average: {avgTimeMs:F2}ms per key pair");

        // Performance assertion (adjust based on your requirements)
        avgTimeMs.Should().BeLessThan(10.0, "Key generation should be fast");
    }

    [Fact]
    public async Task Signing_ED25519_PerformanceTest()
    {
        // Arrange
        var cryptoModule = new CryptoModule();
        var keySetResult = await cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
        var keySet = keySetResult.Value!;
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("test data"));

        const int iterations = 10000;
        var stopwatch = Stopwatch.StartNew();

        // Act
        for (int i = 0; i < iterations; i++)
        {
            var signResult = await cryptoModule.SignAsync(
                hash,
                (byte)WalletNetworks.ED25519,
                keySet.PrivateKey.Key!);
            signResult.IsSuccess.Should().BeTrue();
        }

        stopwatch.Stop();

        // Assert
        var avgTimeMs = stopwatch.ElapsedMilliseconds / (double)iterations;
        Console.WriteLine($"ED25519 Signing: {iterations} iterations in {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Average: {avgTimeMs:F2}ms per signature");

        // Performance assertion
        avgTimeMs.Should().BeLessThan(1.0, "Signing should be very fast");
    }
}
```

#### Example: Algorithm Comparison Test

```csharp
[Theory]
[InlineData(WalletNetworks.ED25519, "ED25519")]
[InlineData(WalletNetworks.NISTP256, "NISTP256")]
[InlineData(WalletNetworks.RSA4096, "RSA4096")]
public async Task CompareKeyGenerationPerformance(WalletNetworks network, string name)
{
    // Arrange
    var cryptoModule = new CryptoModule();
    const int iterations = 100;
    var stopwatch = Stopwatch.StartNew();

    // Act
    for (int i = 0; i < iterations; i++)
    {
        var result = await cryptoModule.GenerateKeySetAsync(network);
        result.IsSuccess.Should().BeTrue();
    }

    stopwatch.Stop();

    // Assert & Report
    var avgTimeMs = stopwatch.ElapsedMilliseconds / (double)iterations;
    Console.WriteLine($"{name}: {avgTimeMs:F2}ms per key pair");
}
```

#### Example: Load Testing Signature Verification

```csharp
[Fact]
public async Task VerifySignature_UnderLoad_ShouldSucceed()
{
    // Arrange
    var cryptoModule = new CryptoModule();
    var keySetResult = await cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
    var keySet = keySetResult.Value!;

    // Create multiple test signatures
    var testData = new List<(byte[] hash, byte[] signature)>();
    for (int i = 0; i < 100; i++)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"test data {i}"));
        var signResult = await cryptoModule.SignAsync(
            hash,
            (byte)WalletNetworks.ED25519,
            keySet.PrivateKey.Key!);
        testData.Add((hash, signResult.Value!));
    }

    // Act - Verify all signatures under load
    var stopwatch = Stopwatch.StartNew();
    var tasks = testData.Select(async data =>
    {
        for (int i = 0; i < 100; i++)
        {
            var result = await cryptoModule.VerifyAsync(
                data.signature,
                data.hash,
                (byte)WalletNetworks.ED25519,
                keySet.PublicKey.Key!);
            result.IsSuccess.Should().BeTrue();
        }
    });

    await Task.WhenAll(tasks);
    stopwatch.Stop();

    // Assert
    var totalVerifications = testData.Count * 100;
    var avgTimeMs = stopwatch.ElapsedMilliseconds / (double)totalVerifications;
    Console.WriteLine($"Verified {totalVerifications} signatures in {stopwatch.ElapsedMilliseconds}ms");
    Console.WriteLine($"Average: {avgTimeMs:F4}ms per verification");
}
```

### Memory and Resource Testing

```csharp
[Fact]
public async Task KeyGeneration_MultipleIterations_ShouldNotLeak()
{
    // Arrange
    var cryptoModule = new CryptoModule();
    var initialMemory = GC.GetTotalMemory(true);

    // Act - Generate many keys
    for (int i = 0; i < 1000; i++)
    {
        var result = await cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
        result.IsSuccess.Should().BeTrue();

        // Force cleanup periodically
        if (i % 100 == 0)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    // Force final cleanup
    GC.Collect();
    GC.WaitForPendingFinalizers();
    var finalMemory = GC.GetTotalMemory(true);

    // Assert - Memory should not grow significantly
    var memoryGrowthMB = (finalMemory - initialMemory) / (1024.0 * 1024.0);
    Console.WriteLine($"Memory growth: {memoryGrowthMB:F2} MB");

    memoryGrowthMB.Should().BeLessThan(50.0, "Memory growth should be minimal");
}
```

### Integration with NBomber (Load Testing)

For comprehensive load testing, integrate with the performance test project:

```csharp
// In tests/Sorcha.Performance.Tests/CryptoLoadTests.cs
using NBomber.CSharp;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;

public static class CryptoLoadTests
{
    public static void RunCryptoLoadTest()
    {
        var cryptoModule = new CryptoModule();

        // Key generation scenario
        var keyGenScenario = Scenario.Create("crypto_key_generation", async context =>
        {
            var result = await cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
            return result.IsSuccess ? Response.Ok() : Response.Fail();
        })
        .WithLoadSimulations(
            Simulation.Inject(rate: 50,
                            interval: TimeSpan.FromSeconds(1),
                            during: TimeSpan.FromSeconds(30))
        );

        // Run the test
        NBomberRunner
            .RegisterScenarios(keyGenScenario)
            .Run();
    }
}
```

### Crypto Test Organization

```
tests/Sorcha.Cryptography.Tests/
├── Unit/
│   ├── CryptoModuleTests.cs        # Core crypto operations
│   ├── KeyManagerTests.cs          # Key management
│   ├── HashProviderTests.cs        # Hashing functions
│   ├── SymmetricCryptoTests.cs     # Encryption/decryption
│   └── EncodingUtilitiesTests.cs   # Encoding utilities
├── Performance/
│   ├── KeyGenerationBenchmarks.cs  # Key generation perf
│   ├── SigningBenchmarks.cs        # Signing/verify perf
│   └── EncryptionBenchmarks.cs     # Encryption perf
└── Integration/
    └── CryptoEndToEndTests.cs      # Full workflow tests
```

## Best Practices

### DO

✓ Write tests first (TDD) or immediately after implementing features
✓ Keep tests simple and focused on one aspect
✓ Use descriptive test names that explain the scenario
✓ Follow the AAA (Arrange-Act-Assert) pattern
✓ Use FluentAssertions for readable assertions
✓ Mock external dependencies in unit tests
✓ Test both happy paths and error cases
✓ Test edge cases and boundary conditions
✓ Keep test data minimal and relevant
✓ Use test helpers for common setup code

### DON'T

✗ Don't write tests that depend on other tests
✗ Don't use Thread.Sleep() - use proper async patterns
✗ Don't test implementation details - test behavior
✗ Don't copy-paste test code - extract helpers
✗ Don't ignore failing tests - fix them immediately
✗ Don't skip writing tests for "simple" code
✗ Don't test private methods directly - test through public API
✗ Don't use magic numbers - use named constants
✗ Don't write tests that require manual setup
✗ Don't commit tests that are skipped

### Test Data Builders

For complex objects, use builder patterns:

```csharp
public class BlueprintTestBuilder
{
    private string _title = "Default Title";
    private string _description = "Default Description";
    private List<Participant> _participants = new();

    public BlueprintTestBuilder WithTitle(string title)
    {
        _title = title;
        return this;
    }

    public BlueprintTestBuilder AddParticipant(Participant participant)
    {
        _participants.Add(participant);
        return this;
    }

    public Blueprint Build()
    {
        return new Blueprint
        {
            Title = _title,
            Description = _description,
            Participants = _participants
        };
    }
}

// Usage
var blueprint = new BlueprintTestBuilder()
    .WithTitle("Test Blueprint")
    .AddParticipant(new Participant { Id = "p1", Name = "P1" })
    .Build();
```

## Troubleshooting

### Common Issues

**Issue**: Tests pass locally but fail in CI
- **Solution**: Ensure tests don't depend on local file system paths or environment variables

**Issue**: Flaky tests that sometimes pass, sometimes fail
- **Solution**: Look for race conditions, shared state, or time-dependent code

**Issue**: Tests are slow
- **Solution**: Profile tests, reduce database calls, use in-memory implementations

**Issue**: Coverage report not generating
- **Solution**: Ensure coverlet.collector package is installed and --collect flag is used

## Resources

- [xUnit Documentation](https://xunit.net/)
- [FluentAssertions Documentation](https://fluentassertions.com/)
- [bUnit Documentation](https://bunit.dev/)
- [Moq Documentation](https://github.com/moq/moq4)
- [.NET Testing Best Practices](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices)

## Contributing

When adding new features to Sorcha:

1. Write tests first (TDD approach preferred)
2. Ensure all tests pass before submitting PR
3. Maintain or improve code coverage
4. Follow the naming and structure conventions in this guide
5. Add integration tests for new workflows
6. Update this documentation if introducing new testing patterns

---

**Questions or Issues?**
- Review existing tests in the `tests/` directory for examples
- Ask in GitHub Discussions
- Open an issue for test infrastructure problems
