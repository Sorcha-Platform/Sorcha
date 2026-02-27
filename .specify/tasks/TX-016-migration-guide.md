# Task: Write Migration Guide

**ID:** TX-016
**Status:** Not Started
**Priority:** Medium
**Estimate:** 4 hours
**Created:** 2025-11-12

## Objective

Create migration guide for transitioning from embedded transaction classes to Sorcha.TransactionHandler.

## Guide Structure

### 1. Overview
- Benefits of standalone library
- Breaking changes summary
- Migration timeline

### 2. Dependency Changes
```xml
<!-- Remove embedded transaction classes -->
<!-- They were in Sorcha.Cryptography (legacy) -->

<!-- Add new packages -->
<PackageReference Include="Sorcha.Cryptography" Version="2.0.0" />
<PackageReference Include="Sorcha.TransactionHandler" Version="2.0.0" />
```

### 3. API Changes

**Old (v1.x):**
```csharp
var tx = TransactionBuilder.Build(TransactionVersion.TX_VERSION_4);
tx.SetTxRecipients(recipients);
tx.SetTxMetaData(metadata);
var payloadMgr = tx.GetTxPayloadManager();
payloadMgr.AddPayload(data, wallets, options);
tx.SignTx(wifKey);
```

**New (v2.0):**
```csharp
var result = await new TransactionBuilder()
    .Create(TransactionVersion.V4)
    .WithRecipients(recipients)
    .WithMetadata(metadata)
    .AddPayload(data, wallets, options)
    .SignAsync(wifKey)
    .Build();

var tx = result.Value;
```

### 4. Return Type Changes
- Old: `(Status, T?)` tuple
- New: `TransactionResult<T>` with IsSuccess pattern

### 5. Async Changes
- All operations now async
- Use await throughout
- CancellationToken support

### 6. Common Patterns
- Transaction builder pattern
- Result type handling
- Error handling changes

## Acceptance Criteria

- [ ] Complete migration guide written
- [ ] Code examples for old and new APIs
- [ ] Breaking changes documented
- [ ] Common patterns provided

---

**Dependencies:** TX-001 through TX-014
