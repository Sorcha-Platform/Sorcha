# Transaction Test Harness

A C# test harness for validating Sorcha transaction submission with correct JSON serialization.

## Purpose

This tool demonstrates and tests the complete transaction submission flow:
1. Authenticate with the service
2. Create a wallet
3. Create a register with governance attestations
4. Submit a transaction with proper payload hash validation

## The Problem It Solves

**PowerShell's `ConvertTo-Json` produces different JSON than .NET's `System.Text.Json`**, causing payload hash mismatches. This C# harness uses the same JSON serialization as the Sorcha services, ensuring consistent hashing.

## Usage

```bash
# Basic transaction test (default URL: http://localhost)
dotnet run

# Basic transaction test with custom gateway URL
dotnet run -- http://custom-gateway:8080

# Full performance test suite (30+ seconds, comprehensive metrics)
dotnet run -- --performance

# Performance test with custom gateway URL
dotnet run -- --performance http://custom-gateway:8080
```

## What It Tests

- ✅ OAuth2 authentication (password grant)
- ✅ Wallet creation (ED25519 with BIP39)
- ✅ Register initiation with owner attestations
- ✅ Attestation signing (ED25519 signatures)
- ✅ Register finalization
- ✅ Transaction submission to validator service
- ✅ Payload hash validation (the critical test!)

## Output

The tool provides a step-by-step display showing:

```
✓ Authenticated
✓ Wallet created: ws11qq...
✓ Register created: abc123...

┌─────────────────┬──────────┐
│ Step            │ Status   │
├─────────────────┼──────────┤
│ Payload created │ 92 bytes │
│ Payload hash    │ aa8cfc...│
│ Transaction ID  │ cf07ae...│
│ Signature       │ ✓ Generated │
│ Validation      │ ✓ Success (HTTP 200) │
└─────────────────┴──────────┘

✓ All tests passed!
```

## Key Implementation Details

### Payload Serialization (CRITICAL)

```csharp
// ❌ WRONG - Objects serialize inconsistently
var payload = new Dictionary<string, object>
{
    ["timestamp"] = DateTimeOffset.UtcNow  // Object!
};

// ✅ CORRECT - Strings serialize consistently
var payload = new Dictionary<string, object>
{
    ["timestamp"] = DateTimeOffset.UtcNow.ToString("o")  // String!
};
```

### Hash Computation

```csharp
// 1. Serialize payload to JSON
var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
{
    WriteIndented = false
});

// 2. Compute hash of JSON bytes
var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
var hashBytes = SHA256.HashData(payloadBytes);
var payloadHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

// 3. Deserialize to JsonElement for transmission
var payloadElement = JsonSerializer.Deserialize<JsonElement>(payloadJson);
```

**Why this works:** The validator service uses `transaction.Payload.GetRawText()` to get the exact JSON that was sent, then computes the hash on those bytes. By preserving the JSON through JsonElement, the hashes match!

### Register Creation with Attestations

```csharp
// Initiate request must include owners
var initiateRequest = new
{
    name = "Register Name",
    owners = new[]
    {
        new
        {
            userId = "user-id",
            walletId = walletAddress
        }
    }
};

// Response includes attestations to sign
// Sign using the dataToSign field (hex-encoded hash)
var dataToSignHex = att.GetProperty("dataToSign").GetString();
var hashBytes = Convert.FromHexString(dataToSignHex);
var dataToSignBase64 = Convert.ToBase64String(hashBytes);

// Sign with isPreHashed: true
var signRequest = new
{
    transactionData = dataToSignBase64,
    isPreHashed = true
};
```

## Dependencies

- .NET 10.0
- Sorcha.ServiceClients
- Sorcha.Register.Models
- Spectre.Console (for pretty output)

## Exit Codes

- `0` - All tests passed
- `1` - Test failed (authentication, wallet creation, register creation, or transaction validation)

## See Also

- [PERFORMANCE-RESULTS.md](../PERFORMANCE-RESULTS.md) - **Complete test results and analysis** ⭐
- [PAYLOAD-SERIALIZATION-GUIDE.md](../../../docs/guides/PAYLOAD-SERIALIZATION-GUIDE.md) - API documentation with examples
- [SOLUTION-SUMMARY.md](../SOLUTION-SUMMARY.md) - Full explanation of the JSON serialization issue
- [STATUS-FINAL.md](../STATUS-FINAL.md) - Final status and deliverables
- `test-single-transaction.ps1` - PowerShell version (shows the problem)
- `SYSTEM-SPECS.md` - System specifications for reproducibility

## Performance Baseline

Typical execution times (on Intel i5-10310U, 16GB RAM, Docker Desktop):

| Operation | Time |
|-----------|------|
| Authentication | 50-100ms |
| Wallet creation | 100-200ms |
| Register initiation | 7-8ms |
| Attestation signing | 50-100ms |
| Register finalization | 5-6ms |
| Transaction validation | 10-15ms |
| **Total** | **~350-500ms** |

## Performance Testing ✅

The harness includes a comprehensive performance test suite (`--performance` flag):

**Completed:**
- ✅ Throughput testing (65.16 TPS sustained)
- ✅ Latency percentiles (P50: 32ms, P95: 55ms, P99: 71ms)
- ✅ Payload size variations (100B - 100KB tested)
- ✅ Concurrency testing (1-32 parallel, optimal at 16 concurrent)
- ✅ Formatted report generation

**Results:** See [PERFORMANCE-RESULTS.md](../PERFORMANCE-RESULTS.md) for complete analysis

**Future Enhancements:**
- [ ] Long-running soak tests (24+ hours)
- [ ] Integration with NBomber for advanced load patterns
- [ ] Database connection pool tuning tests
- [ ] Network latency simulation

## License

SPDX-License-Identifier: MIT
Copyright (c) 2026 Sorcha Contributors
