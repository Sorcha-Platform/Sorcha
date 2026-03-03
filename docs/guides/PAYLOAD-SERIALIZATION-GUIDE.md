# Payload Serialization Guide

**Version:** 1.0
**Last Updated:** 2026-02-13
**Applies To:** Validator Service, Transaction Submission

---

## Overview

When submitting transactions to the Sorcha platform, **payload serialization consistency is critical** for hash validation. This guide explains how to properly construct and serialize transaction payloads to ensure successful validation.

## The Problem

Transaction payloads are validated using cryptographic hashing. The validator computes a hash of the received payload and compares it to the hash you provide. **If the hashes don't match, the transaction is rejected.**

### Why Hashes Mismatch

Common causes of hash mismatches:

1. **Different JSON serialization libraries** - PowerShell's `ConvertTo-Json` ≠ .NET's `System.Text.Json`
2. **Object type serialization** - `DateTimeOffset` objects serialize differently than strings
3. **Key ordering** - Dictionary key order may vary between serializations
4. **Whitespace differences** - Indentation, spacing, newlines
5. **Encoding differences** - Character escaping (`+` vs `\u002B`)

### Example Error

```json
{
  "code": "TX_012",
  "message": "Payload hash mismatch. Expected: abc123..., Computed: def456...",
  "field": "expectedHash"
}
```

---

## The Solution

### Rule 1: Use Primitive Types

**✅ CORRECT - Use strings, numbers, booleans:**

```csharp
var payload = new Dictionary<string, object>
{
    ["testData"] = "HELLO WORLD",
    ["timestamp"] = DateTimeOffset.UtcNow.ToString("o"),  // String!
    ["sequence"] = 1,                                      // Number
    ["enabled"] = true                                     // Boolean
};
```

**❌ WRONG - Avoid complex objects:**

```csharp
var payload = new Dictionary<string, object>
{
    ["timestamp"] = DateTimeOffset.UtcNow,  // Object serialization varies!
    ["metadata"] = new { foo = "bar" }      // Nested objects can vary!
};
```

### Rule 2: Use the Same JSON Library as the Service

The Sorcha services use **System.Text.Json** with these settings:

```csharp
var options = new JsonSerializerOptions
{
    WriteIndented = false  // No whitespace
};
```

If testing from:
- **C#** - Use `System.Text.Json`
- **Python** - Use `json` module with `separators=(',', ':')`
- **JavaScript** - Use `JSON.stringify()`
- **PowerShell** - ⚠️ **DO NOT USE** for transaction testing

### Rule 3: Hash Before Converting to JsonElement

**Correct Hash Computation Flow:**

```csharp
// 1. Create payload with primitive types
var payload = new Dictionary<string, object>
{
    ["testData"] = "HELLO WORLD",
    ["timestamp"] = DateTimeOffset.UtcNow.ToString("o"),
    ["sequence"] = 1
};

// 2. Serialize to JSON string
var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
{
    WriteIndented = false
});

// 3. Compute hash of the JSON bytes
var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
var hashBytes = SHA256.HashData(payloadBytes);
var payloadHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

// 4. Deserialize to JsonElement for transmission
var payloadElement = JsonSerializer.Deserialize<JsonElement>(payloadJson);

// 5. Send both to validator
var request = new
{
    payload = payloadElement,   // Preserves exact JSON
    payloadHash = payloadHash   // Matches what service computes
};
```

**Why this works:**

The validator uses `transaction.Payload.GetRawText()` to get the exact JSON received, then computes the hash on those bytes. By preserving the JSON through `JsonElement`, the hashes match!

---

## Complete Example

### C# Transaction Submission

```csharp
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

public async Task<bool> SubmitTransactionAsync(
    HttpClient httpClient,
    string baseUrl,
    string registerId,
    string walletAddress,
    string token)
{
    // 1. Create payload with primitive types only
    var payload = new Dictionary<string, object>
    {
        ["testData"] = "HELLO WORLD",
        ["timestamp"] = DateTimeOffset.UtcNow.ToString("o"),  // ✅ String
        ["sequence"] = 1,
        ["userId"] = "user-123"
    };

    // 2. Serialize payload (once!)
    var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
    {
        WriteIndented = false
    });

    // 3. Compute payload hash
    var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
    var hashBytes = SHA256.HashData(payloadBytes);
    var payloadHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

    // 4. Generate transaction ID
    var txIdSource = $"{registerId}-{DateTimeOffset.UtcNow:o}-{Guid.NewGuid()}";
    var txIdBytes = SHA256.HashData(Encoding.UTF8.GetBytes(txIdSource));
    var transactionId = Convert.ToHexString(txIdBytes).ToLowerInvariant();

    // 5. Sign transaction ID
    var signRequest = new
    {
        transactionData = Convert.ToBase64String(txIdBytes),
        isPreHashed = true
    };

    var signJson = JsonSerializer.Serialize(signRequest);
    var signResponse = await httpClient.PostAsync(
        $"{baseUrl}/api/v1/wallets/{walletAddress}/sign",
        new StringContent(signJson, Encoding.UTF8, "application/json"),
        new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }
    );

    signResponse.EnsureSuccessStatusCode();
    var signDoc = JsonSerializer.Deserialize<JsonDocument>(
        await signResponse.Content.ReadAsStringAsync());

    // 6. Build validator request
    var payloadElement = JsonSerializer.Deserialize<JsonElement>(payloadJson);

    var validateRequest = new
    {
        transactionId,
        registerId,
        blueprintId = "your-blueprint-id",
        actionId = "action-1",
        payload = payloadElement,      // Exact JSON preserved
        payloadHash,                    // Matches validator computation
        signatures = new[]
        {
            new
            {
                publicKey = signDoc.RootElement.GetProperty("publicKey").GetString(),
                signatureValue = signDoc.RootElement.GetProperty("signature").GetString(),
                algorithm = "ED25519"
            }
        },
        createdAt = DateTimeOffset.UtcNow,
        expiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
        priority = 1
    };

    // 7. Submit to validator
    var validateJson = JsonSerializer.Serialize(validateRequest);
    var validateResponse = await httpClient.PostAsync(
        $"{baseUrl}/api/validator/transactions/validate",
        new StringContent(validateJson, Encoding.UTF8, "application/json"),
        new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }
    );

    return validateResponse.IsSuccessStatusCode;
}
```

### JavaScript Example

```javascript
const crypto = require('crypto');

async function submitTransaction(baseUrl, registerId, walletAddress, token) {
    // 1. Create payload with primitive types
    const payload = {
        testData: "HELLO WORLD",
        timestamp: new Date().toISOString(),  // ✅ String
        sequence: 1,
        userId: "user-123"
    };

    // 2. Serialize payload
    const payloadJson = JSON.stringify(payload);

    // 3. Compute payload hash
    const payloadHash = crypto.createHash('sha256')
        .update(payloadJson, 'utf8')
        .digest('hex')
        .toLowerCase();

    // 4. Generate transaction ID
    const txIdSource = `${registerId}-${new Date().toISOString()}-${crypto.randomUUID()}`;
    const transactionId = crypto.createHash('sha256')
        .update(txIdSource, 'utf8')
        .digest('hex')
        .toLowerCase();

    // 5. Sign transaction ID (call wallet API)
    // ... signing code ...

    // 6. Submit to validator
    const validateRequest = {
        transactionId,
        registerId,
        blueprintId: "your-blueprint-id",
        actionId: "action-1",
        payload: JSON.parse(payloadJson),  // Parse back to object
        payloadHash,
        signatures: [/* ... */],
        createdAt: new Date().toISOString(),
        expiresAt: new Date(Date.now() + 5 * 60 * 1000).toISOString(),
        priority: 1
    };

    const response = await fetch(`${baseUrl}/api/validator/transactions/validate`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${token}`
        },
        body: JSON.stringify(validateRequest)
    });

    return response.ok;
}
```

### Python Example

```python
import json
import hashlib
from datetime import datetime, timezone

def submit_transaction(base_url, register_id, wallet_address, token):
    # 1. Create payload with primitive types
    payload = {
        "testData": "HELLO WORLD",
        "timestamp": datetime.now(timezone.utc).isoformat(),  # ✅ String
        "sequence": 1,
        "userId": "user-123"
    }

    # 2. Serialize payload (no whitespace!)
    payload_json = json.dumps(payload, separators=(',', ':'), sort_keys=False)

    # 3. Compute payload hash
    payload_hash = hashlib.sha256(payload_json.encode('utf-8')).hexdigest().lower()

    # 4. Generate transaction ID
    tx_id_source = f"{register_id}-{datetime.now(timezone.utc).isoformat()}-{uuid.uuid4()}"
    transaction_id = hashlib.sha256(tx_id_source.encode('utf-8')).hexdigest().lower()

    # 5. Sign transaction ID
    # ... signing code ...

    # 6. Submit to validator
    validate_request = {
        "transactionId": transaction_id,
        "registerId": register_id,
        "blueprintId": "your-blueprint-id",
        "actionId": "action-1",
        "payload": json.loads(payload_json),  # Parse back to dict
        "payloadHash": payload_hash,
        "signatures": [/* ... */],
        "createdAt": datetime.now(timezone.utc).isoformat(),
        "expiresAt": (datetime.now(timezone.utc) + timedelta(minutes=5)).isoformat(),
        "priority": 1
    }

    response = requests.post(
        f"{base_url}/api/validator/transactions/validate",
        json=validate_request,
        headers={"Authorization": f"Bearer {token}"}
    )

    return response.ok
```

---

## Validator Hash Computation

For reference, here's how the validator computes the payload hash:

```csharp
// From ValidationEngine.cs
var payloadJson = transaction.Payload.GetRawText();  // Gets exact JSON received
var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
var computedHash = _hashProvider.ComputeHash(payloadBytes, HashType.SHA256);
var computedHashHex = Convert.ToHexString(computedHash).ToLowerInvariant();

if (!string.Equals(computedHashHex, transaction.PayloadHash, StringComparison.OrdinalIgnoreCase))
{
    // Hash mismatch error
}
```

**Key insight:** `GetRawText()` returns the exact JSON string that was received. If you hash that same string before sending, the hashes will match!

---

## Testing Your Implementation

### Test Harness

Use the C# test harness at `walkthroughs/PerformanceBenchmark/TransactionTest/`:

```bash
# Basic transaction test
dotnet run

# Full performance suite
dotnet run -- --performance
```

### Manual Verification

To verify your hash computation:

1. Create a payload with known values
2. Serialize to JSON (no whitespace)
3. Compute SHA-256 hash
4. Compare with what the validator computes

Example verification:

```csharp
var payload = new { testData = "HELLO", timestamp = "2026-02-13T12:00:00.0000000+00:00", sequence = 1 };
var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
// json should be: {"testData":"HELLO","timestamp":"2026-02-13T12:00:00.0000000+00:00","sequence":1}

var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
// hash should be consistent every time with this exact JSON
```

---

## Common Pitfalls

### ❌ Using PowerShell for Testing

```powershell
# DON'T DO THIS - PowerShell serialization differs from .NET
$payload = @{ testData = "HELLO" }
$json = $payload | ConvertTo-Json
# This JSON will NOT match System.Text.Json output!
```

### ❌ Hashing After JsonElement Conversion

```csharp
// WRONG - Hashing the wrong thing
var payloadElement = JsonSerializer.Deserialize<JsonElement>(someJson);
var reserializedJson = JsonSerializer.Serialize(payloadElement);  // Might be different!
var hash = ComputeHash(reserializedJson);  // Hash mismatch!
```

### ❌ Using DateTime Instead of String

```csharp
// WRONG
var payload = new Dictionary<string, object>
{
    ["timestamp"] = DateTime.UtcNow  // Object serialization!
};

// CORRECT
var payload = new Dictionary<string, object>
{
    ["timestamp"] = DateTime.UtcNow.ToString("o")  // String serialization!
};
```

---

## Best Practices

1. **Use primitive types** - Strings, numbers, booleans only
2. **Use the same JSON library** - System.Text.Json for C#, `json` for Python, `JSON` for JavaScript
3. **No whitespace** - Set `WriteIndented = false`
4. **Hash before JsonElement** - Compute hash on original JSON string
5. **Test with C#** - For validating your implementation
6. **Document your schema** - Specify expected payload structure
7. **Validate locally** - Test hash computation before submitting

---

## See Also

- [Transaction Test Harness](../walkthroughs/PerformanceBenchmark/TransactionTest/README.md) - Working C# implementation
- [Solution Summary](../walkthroughs/PerformanceBenchmark/SOLUTION-SUMMARY.md) - Detailed problem analysis
- [Validator Service API](./VALIDATOR-API.md) - API documentation
- [Transaction Model](./TRANSACTION-MODEL.md) - Data structure reference

---

## Support

If you encounter payload hash mismatches:

1. Check that you're using primitive types (not objects)
2. Verify you're using System.Text.Json serialization settings
3. Test with the C# test harness to verify your approach
4. Compare your JSON output to the examples in this guide

For issues or questions, consult the test harness source code or open an issue on GitHub.

---

**Last Updated:** 2026-02-13
**Version:** 1.0
**Applies To:** Sorcha Platform v2.5+
