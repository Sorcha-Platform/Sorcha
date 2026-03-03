# Sorcha.Cryptography Tests

Comprehensive test suite for the Sorcha.Cryptography library including unit tests, performance tests, and load tests.

## Overview

This test project covers:
- **Key Generation**: ED25519, NISTP256, RSA4096
- **Digital Signatures**: Sign and verify operations
- **Encryption/Decryption**: Symmetric and asymmetric
- **Hashing**: SHA-256, SHA-512
- **Encoding**: Base64, Hex utilities
- **Performance**: Benchmarking and load testing

## Running Tests

### Run All Tests

```bash
dotnet test tests/Sorcha.Cryptography.Tests
```

### Run Specific Algorithm Tests

```bash
# ED25519 tests only
dotnet test tests/Sorcha.Cryptography.Tests --filter "FullyQualifiedName~ED25519"

# NISTP256 tests only
dotnet test tests/Sorcha.Cryptography.Tests --filter "FullyQualifiedName~NISTP256"

# RSA4096 tests only
dotnet test tests/Sorcha.Cryptography.Tests --filter "FullyQualifiedName~RSA4096"
```

### Run with Code Coverage

```bash
dotnet test tests/Sorcha.Cryptography.Tests --collect:"XPlat Code Coverage"
```

### Run with Detailed Output

```bash
dotnet test tests/Sorcha.Cryptography.Tests --logger "console;verbosity=detailed"
```

### Watch Mode (Auto-rerun on changes)

```bash
dotnet watch test --project tests/Sorcha.Cryptography.Tests
```

## Test Organization

```
tests/Sorcha.Cryptography.Tests/
├── Unit/
│   ├── CryptoModuleTests.cs        # Core crypto operations
│   ├── KeyManagerTests.cs          # Key management
│   ├── HashProviderTests.cs        # Hashing functions
│   ├── SymmetricCryptoTests.cs     # Encryption/decryption
│   └── EncodingUtilitiesTests.cs   # Encoding utilities
└── README.md                       # This file
```

## Performance Testing

### Key Generation Performance

Test key generation speed across different algorithms:

```bash
# Compare performance of all key types
dotnet test tests/Sorcha.Cryptography.Tests \
  --filter "FullyQualifiedName~KeyGeneration" \
  --logger "console;verbosity=detailed"
```

**Example output:**
```
ED25519 Key Generation: 1000 iterations in 245ms
Average: 0.25ms per key pair

NISTP256 Key Generation: 1000 iterations in 1250ms
Average: 1.25ms per key pair

RSA4096 Key Generation: 100 iterations in 15000ms
Average: 150ms per key pair
```

### Signing Performance

Test signing speed for each algorithm:

```bash
dotnet test tests/Sorcha.Cryptography.Tests \
  --filter "FullyQualifiedName~Signing" \
  --logger "console;verbosity=detailed"
```

**Expected performance (approximate):**
- **ED25519**: ~0.05ms per signature
- **NISTP256**: ~0.5ms per signature
- **RSA4096**: ~5ms per signature

### Verification Performance

Test signature verification speed:

```bash
dotnet test tests/Sorcha.Cryptography.Tests \
  --filter "FullyQualifiedName~Verify" \
  --logger "console;verbosity=detailed"
```

## Load Testing Examples

### Example 1: Key Generation Load Test

```csharp
[Fact]
public async Task LoadTest_KeyGeneration_1000Keys()
{
    // Arrange
    var cryptoModule = new CryptoModule();
    const int iterations = 1000;
    var stopwatch = Stopwatch.StartNew();
    var results = new List<bool>();

    // Act
    for (int i = 0; i < iterations; i++)
    {
        var result = await cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
        results.Add(result.IsSuccess);
    }

    stopwatch.Stop();

    // Assert
    results.Should().AllSatisfy(r => r.Should().BeTrue());
    var avgTimeMs = stopwatch.ElapsedMilliseconds / (double)iterations;
    Console.WriteLine($"Generated {iterations} key pairs in {stopwatch.ElapsedMilliseconds}ms");
    Console.WriteLine($"Average: {avgTimeMs:F2}ms per key pair");
    Console.WriteLine($"Throughput: {iterations / (stopwatch.ElapsedMilliseconds / 1000.0):F0} keys/second");
}
```

### Example 2: Parallel Signing Load Test

```csharp
[Fact]
public async Task LoadTest_ParallelSigning_MultipleThreads()
{
    // Arrange
    var cryptoModule = new CryptoModule();
    var keySetResult = await cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
    var keySet = keySetResult.Value!;

    const int threadsCount = 10;
    const int signaturesPerThread = 1000;

    byte[] hash = System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes("test data"));

    var stopwatch = Stopwatch.StartNew();

    // Act - Run signing operations in parallel
    var tasks = Enumerable.Range(0, threadsCount).Select(async _ =>
    {
        for (int i = 0; i < signaturesPerThread; i++)
        {
            var signResult = await cryptoModule.SignAsync(
                hash,
                (byte)WalletNetworks.ED25519,
                keySet.PrivateKey.Key!);
            signResult.IsSuccess.Should().BeTrue();
        }
    });

    await Task.WhenAll(tasks);
    stopwatch.Stop();

    // Assert
    var totalSignatures = threadsCount * signaturesPerThread;
    Console.WriteLine($"Completed {totalSignatures} signatures in {stopwatch.ElapsedMilliseconds}ms");
    Console.WriteLine($"Throughput: {totalSignatures / (stopwatch.ElapsedMilliseconds / 1000.0):F0} signatures/second");
}
```

### Example 3: Algorithm Comparison

```csharp
[Theory]
[InlineData(WalletNetworks.ED25519, "ED25519")]
[InlineData(WalletNetworks.NISTP256, "NISTP256")]
[InlineData(WalletNetworks.RSA4096, "RSA4096")]
public async Task LoadTest_CompareAlgorithms(WalletNetworks network, string name)
{
    // Arrange
    var cryptoModule = new CryptoModule();
    const int iterations = 100;

    // Test key generation
    var keyGenStopwatch = Stopwatch.StartNew();
    var keySet = default(KeySet);

    for (int i = 0; i < iterations; i++)
    {
        var result = await cryptoModule.GenerateKeySetAsync(network);
        if (i == 0) keySet = result.Value;
    }

    keyGenStopwatch.Stop();
    var keyGenAvg = keyGenStopwatch.ElapsedMilliseconds / (double)iterations;

    // Test signing
    byte[] hash = System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes("test data"));

    var signStopwatch = Stopwatch.StartNew();
    byte[]? signature = null;

    for (int i = 0; i < iterations; i++)
    {
        var signResult = await cryptoModule.SignAsync(
            hash,
            (byte)network,
            keySet!.PrivateKey.Key!);
        if (i == 0) signature = signResult.Value;
    }

    signStopwatch.Stop();
    var signAvg = signStopwatch.ElapsedMilliseconds / (double)iterations;

    // Test verification
    var verifyStopwatch = Stopwatch.StartNew();

    for (int i = 0; i < iterations; i++)
    {
        await cryptoModule.VerifyAsync(
            signature!,
            hash,
            (byte)network,
            keySet!.PublicKey.Key!);
    }

    verifyStopwatch.Stop();
    var verifyAvg = verifyStopwatch.ElapsedMilliseconds / (double)iterations;

    // Report
    Console.WriteLine($"\n{name} Performance ({iterations} iterations):");
    Console.WriteLine($"  Key Generation: {keyGenAvg:F2}ms avg");
    Console.WriteLine($"  Signing:        {signAvg:F2}ms avg");
    Console.WriteLine($"  Verification:   {verifyAvg:F2}ms avg");
}
```

### Example 4: Memory Leak Test

```csharp
[Fact]
public async Task LoadTest_NoMemoryLeak_LongRunning()
{
    // Arrange
    var cryptoModule = new CryptoModule();
    var initialMemory = GC.GetTotalMemory(true);
    const int iterations = 5000;

    // Act - Generate many keys
    for (int i = 0; i < iterations; i++)
    {
        var result = await cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
        result.IsSuccess.Should().BeTrue();

        // Periodic cleanup
        if (i % 500 == 0)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();

            var currentMemory = GC.GetTotalMemory(false);
            var memoryMB = (currentMemory - initialMemory) / (1024.0 * 1024.0);
            Console.WriteLine($"Iteration {i}: Memory delta = {memoryMB:F2} MB");
        }
    }

    // Force final cleanup
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var finalMemory = GC.GetTotalMemory(true);
    var memoryGrowthMB = (finalMemory - initialMemory) / (1024.0 * 1024.0);

    // Assert
    Console.WriteLine($"\nTotal iterations: {iterations}");
    Console.WriteLine($"Final memory growth: {memoryGrowthMB:F2} MB");

    memoryGrowthMB.Should().BeLessThan(50.0, "Memory should not leak significantly");
}
```

## Integration with NBomber

For advanced load testing scenarios, use NBomber in the Sorcha.Performance.Tests project:

```csharp
// tests/Sorcha.Performance.Tests/CryptoLoadTests.cs
using NBomber.CSharp;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;

public static class CryptoLoadTests
{
    public static void RunKeyGenerationLoadTest()
    {
        var cryptoModule = new CryptoModule();

        var scenario = Scenario.Create("crypto_key_generation", async context =>
        {
            var result = await cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
            return result.IsSuccess ? Response.Ok() : Response.Fail();
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(5))
        .WithLoadSimulations(
            Simulation.Inject(
                rate: 100,
                interval: TimeSpan.FromSeconds(1),
                during: TimeSpan.FromSeconds(60)
            )
        );

        NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("performance-reports")
            .Run();
    }

    public static void RunSigningLoadTest()
    {
        var cryptoModule = new CryptoModule();
        var keySetTask = cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
        keySetTask.Wait();
        var keySet = keySetTask.Result.Value!;

        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("test data"));

        var scenario = Scenario.Create("crypto_signing", async context =>
        {
            var result = await cryptoModule.SignAsync(
                hash,
                (byte)WalletNetworks.ED25519,
                keySet.PrivateKey.Key!);
            return result.IsSuccess ? Response.Ok() : Response.Fail();
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(5))
        .WithLoadSimulations(
            Simulation.RampingInject(
                rate: 500,
                interval: TimeSpan.FromSeconds(1),
                during: TimeSpan.FromMinutes(2)
            )
        );

        NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("performance-reports")
            .Run();
    }
}
```

## Benchmarking with BenchmarkDotNet

For precise micro-benchmarks, consider adding BenchmarkDotNet tests:

```bash
# Add BenchmarkDotNet
dotnet add tests/Sorcha.Cryptography.Tests package BenchmarkDotNet
```

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class CryptoBenchmarks
{
    private CryptoModule _cryptoModule = null!;
    private KeySet _ed25519KeySet = null!;
    private byte[] _testHash = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _cryptoModule = new CryptoModule();
        var result = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
        _ed25519KeySet = result.Value!;
        _testHash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("test data"));
    }

    [Benchmark]
    public async Task<KeySet> KeyGeneration_ED25519()
    {
        var result = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
        return result.Value!;
    }

    [Benchmark]
    public async Task<byte[]> Signing_ED25519()
    {
        var result = await _cryptoModule.SignAsync(
            _testHash,
            (byte)WalletNetworks.ED25519,
            _ed25519KeySet.PrivateKey.Key!);
        return result.Value!;
    }

    [Benchmark]
    public async Task<bool> Verification_ED25519()
    {
        var signature = await _cryptoModule.SignAsync(
            _testHash,
            (byte)WalletNetworks.ED25519,
            _ed25519KeySet.PrivateKey.Key!);

        var result = await _cryptoModule.VerifyAsync(
            signature.Value!,
            _testHash,
            (byte)WalletNetworks.ED25519,
            _ed25519KeySet.PublicKey.Key!);

        return result.IsSuccess;
    }
}

// Run benchmarks:
// BenchmarkRunner.Run<CryptoBenchmarks>();
```

## Expected Performance Metrics

### Key Generation (per operation)

| Algorithm | Expected Time | Throughput |
|-----------|--------------|------------|
| ED25519   | < 1ms        | > 1000/s   |
| NISTP256  | < 5ms        | > 200/s    |
| RSA4096   | < 200ms      | > 5/s      |

### Signing (per operation)

| Algorithm | Expected Time | Throughput |
|-----------|--------------|------------|
| ED25519   | < 0.1ms      | > 10000/s  |
| NISTP256  | < 1ms        | > 1000/s   |
| RSA4096   | < 10ms       | > 100/s    |

### Verification (per operation)

| Algorithm | Expected Time | Throughput |
|-----------|--------------|------------|
| ED25519   | < 0.2ms      | > 5000/s   |
| NISTP256  | < 2ms        | > 500/s    |
| RSA4096   | < 1ms        | > 1000/s   |

*Note: Performance varies based on hardware. These are approximate values on modern hardware.*

## Continuous Performance Monitoring

Add performance regression tests to CI/CD:

```yaml
# .github/workflows/performance-tests.yml
name: Performance Tests

on:
  pull_request:
    paths:
      - 'src/Common/Sorcha.Cryptography/**'
      - 'tests/Sorcha.Cryptography.Tests/**'

jobs:
  crypto-performance:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Run performance tests
        run: |
          dotnet test tests/Sorcha.Cryptography.Tests \
            --filter "Category=Performance" \
            --logger "console;verbosity=detailed"

      - name: Upload results
        uses: actions/upload-artifact@v3
        with:
          name: crypto-performance-results
          path: TestResults/
```

## Troubleshooting

### Tests Running Slowly

If tests are slower than expected:
1. Check CPU throttling / power settings
2. Ensure running in Release mode: `dotnet test -c Release`
3. Verify no antivirus interfering with crypto operations
4. Check for memory pressure (insufficient RAM)

### Inconsistent Results

For stable benchmarks:
1. Close other applications
2. Run multiple times and average
3. Use BenchmarkDotNet for precise measurements
4. Consider thermal throttling on laptops

### Memory Test Failures

If memory tests fail:
1. Increase threshold if baseline grows
2. Check for actual leaks with memory profiler
3. Verify GC is running properly

## Resources

- [Sorcha.Cryptography Library](../../src/Common/Sorcha.Cryptography/)
- [Main Testing Guide](../../docs/guides/testing/testing.md)
- [Performance Tests](../Sorcha.Performance.Tests/)
- [xUnit Documentation](https://xunit.net/)
- [FluentAssertions](https://fluentassertions.com/)
- [NBomber](https://nbomber.com/)
- [BenchmarkDotNet](https://benchmarkdotnet.org/)
