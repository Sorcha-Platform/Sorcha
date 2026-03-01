# Research: Encrypted Payload Integration

**Feature**: 045-encrypted-payload-integration
**Date**: 2026-03-01
**Status**: Complete

## R1: Encryption Pipeline Architecture

**Decision**: Wire encryption into ActionExecutionService AFTER disclosure filtering, BEFORE transaction building. Use existing PayloadManager with its RecipientKeyInfo[] overload.

**Rationale**: PayloadManager already implements proper envelope encryption (XChaCha20-Poly1305 default, per-recipient key wrapping via CryptoModule.EncryptAsync). The only gap is that ActionExecutionService calls the ITransactionBuilderService extension method (ITransactionBuilderService.cs:105-156) which serializes plaintext JSON, bypassing PayloadManager entirely. The fix is to insert a new encryption step between DisclosureProcessor output (line 249) and transaction building (line 267).

**Alternatives considered**:
- Encrypt inside TransactionBuilderService: Rejected — TransactionBuilderService.cs already uses PayloadManager but calls the legacy string[] overload (line 106-110) which stores unencrypted. Fixing it there would require restructuring the class AND the extension method. Cleaner to add encryption as an explicit pipeline step.
- Encrypt inside DisclosureProcessor: Rejected — violates single responsibility. DisclosureProcessor is field filtering (JSON Pointers), not crypto.

## R2: In-Process Symmetric Encryption (Option A)

**Decision**: Symmetric key generation and payload encryption happen in-process within Blueprint Service. Wallet Service is only invoked for operations requiring stored private keys (signing, decryption at read time).

**Rationale**:
- SymmetricCrypto.cs is a stateless library — no private key material needed for encryption
- Avoids HTTP round-trips to Wallet Service for each symmetric operation
- Wallet Service rate limiting: default "Api" policy = 100 req/min per IP, "Strict" = 5 req/min. A 50-recipient action would exhaust limits.
- Key wrapping (asymmetric encrypt of 32-byte symmetric key) uses public keys only — can be done in-process via CryptoModule.EncryptAsync without Wallet Service

**What still requires Wallet Service**: Decryption (read path) — recipients need their private key to unwrap the symmetric key. This goes through existing `POST /api/v1/wallets/{address}/decrypt` endpoint.

## R3: Disclosure Group Optimization

**Decision**: Group recipients by identical disclosure field sets. Encrypt once per unique field set, wrap the symmetric key individually for each group member.

**Rationale**:
- Without grouping: O(N) encryptions where N = number of recipients
- With grouping: O(M) encryptions + O(N) key wraps where M = unique field sets, typically M << N
- Key wrapping is ~100x faster than data encryption (wrapping a 32-byte key vs encrypting KB/MB of payload)
- Direct impact on 4MB transaction size limit: M ciphertexts instead of N ciphertexts
- Example: 10 recipients, 3 disclosure groups → 3 ciphertexts + 10 wrapped keys (32 bytes each) = ~30KB overhead vs 10 ciphertexts = ~100KB+ overhead

**Algorithm**:
1. For each recipient, compute a deterministic hash of their sorted disclosed field names
2. Group recipients by hash → DisclosureGroup[]
3. For each group: encrypt the filtered payload once, wrap the symmetric key for each member
4. Assemble EncryptedPayloadGroup[] for transaction

## R4: P-256 ECIES Implementation

**Decision**: Implement ECIES (Elliptic Curve Integrated Encryption Scheme) using ECDH key agreement + HKDF-SHA256 + AES-256-GCM in CryptoModule.cs lines 627-641.

**Rationale**:
- CryptoModule already stubs P-256 encrypt/decrypt but returns "not yet implemented"
- ECIES is the standard hybrid encryption scheme for elliptic curves (SEC 1 v2, ISO 18033-2)
- .NET 10 provides native `ECDiffieHellman` with P-256 support
- No external library needed — use System.Security.Cryptography

**Implementation pattern (ECIES)**:
1. Generate ephemeral P-256 key pair
2. ECDH with ephemeral private + recipient public → shared secret
3. HKDF-SHA256(shared secret) → 32-byte AES key
4. AES-256-GCM encrypt plaintext
5. Output: [ephemeral public key (65 bytes)] + [nonce (12 bytes)] + [ciphertext] + [tag (16 bytes)]
6. Zeroize ephemeral private key and shared secret

**Alternatives considered**:
- libsodium `crypto_box_seal` (X25519): Only works with Curve25519, not P-256
- BouncyCastle ECIES: Adds dependency, .NET native is sufficient

## R5: ML-KEM-768 Decapsulate Fix

**Decision**: Fix WalletEndpoints.cs DecapsulateKey (line 1321) to call `PqcEncapsulationProvider.DecryptWithKemAsync` instead of `WalletManager.DecryptPayloadAsync`.

**Rationale**:
- Current flow: `DecapsulateKey` → `WalletManager.DecryptPayloadAsync` → `CryptoModule.DecryptAsync` → `Decapsulate()` which returns only the 32-byte shared secret, NOT the original plaintext
- `EncapsulateKey` packs `[KEM ciphertext (1088 bytes)][nonce (24 bytes)][symmetric ciphertext]` via `EncryptWithKemAsync`
- Corresponding `DecryptWithKemAsync` exists but is never called by the decapsulate endpoint
- Fix: call `DecryptWithKemAsync(ciphertext, privateKey)` directly, which extracts shared secret via KEM decapsulation then uses it as the symmetric key to decrypt the payload

## R6: Batch Public Key Resolution

**Decision**: Add `POST /api/registers/{registerId}/participants/resolve-public-keys` batch endpoint to Register Service.

**Rationale**:
- Current endpoint resolves ONE address at a time: `GET .../by-address/{walletAddress}/public-key`
- For a 50-recipient action, this means 50 sequential HTTP calls (or parallel but still 50 requests)
- Rate limiting on Register Service: 100 req/min default — a single large action could exhaust limits
- Batch endpoint accepts array of wallet addresses, returns map of address → PublicKeyResolution
- Handles mixed results: some found, some not found, some revoked (410)

**Wire format**:
- Request: `{ "walletAddresses": ["addr1", "addr2", ...], "algorithm": "optional-filter" }`
- Response: `{ "resolved": { "addr1": { publicKey, algorithm, status }, ... }, "notFound": ["addr3"], "revoked": ["addr4"] }`

## R7: Async Encryption Pipeline with Progress

**Decision**: Use `Channel<T>` (System.Threading.Channels) + BackgroundService for async encryption. Send progress via existing ActionsHub SignalR.

**Rationale**:
- Channel<T> is the idiomatic .NET producer-consumer pattern — not yet used in the codebase but cleaner than Redis-based queueing for in-process work
- BackgroundService pattern is well-established (12 existing implementations in the codebase)
- ActionsHub already supports `wallet:{address}` groups — add new events for encryption progress
- Existing client-side reconnection with `WithAutomaticReconnect` handles transient disconnections

**New SignalR events on ActionsHub**:
- `EncryptionProgress` → `{ operationId, step, stepName, totalSteps, percentComplete, timestamp }`
- `EncryptionComplete` → `{ operationId, transactionHash, timestamp }`
- `EncryptionFailed` → `{ operationId, error, failedRecipient, timestamp }`

**Pipeline flow**:
1. ActionExecutionService validates, calculates, routes, discloses synchronously
2. Returns HTTP 202 Accepted with `{ operationId }` immediately
3. Writes encryption work item to `Channel<EncryptionWorkItem>`
4. `EncryptionBackgroundService` reads from channel, encrypts, builds transaction, submits to validator
5. Progress notifications sent via IHubContext<ActionsHub> at each step

## R8: Transaction Size Enforcement

**Decision**: Raise default to 4MB and actually enforce it in TransactionReceiver. Add pre-flight estimation in encryption pipeline.

**Rationale**:
- Current 1MB limit in TransactionReceiverConfiguration.cs:32 is NEVER enforced — a ghost config
- TransactionReceiver.ReceiveTransactionAsync logs size but never checks it
- Actual enforced limits: HTTP 10MB, gRPC 16MB, MongoDB 16MB BSON
- 4MB is generous for typical multi-recipient encrypted actions (<50 recipients) while staying well under all downstream limits
- Pre-flight estimation prevents wasting CPU on encryption that will be rejected

**Enforcement points**:
1. TransactionReceiver.ReceiveTransactionAsync — check `transactionData.Length > _config.MaxTransactionSizeBytes` before deserialization
2. Encryption pipeline — estimate `sum(ciphertext_sizes) + sum(wrapped_keys) + metadata_overhead` before encryption

## R9: Encrypted Transaction Wire Format

**Decision**: Extend existing PayloadModel structure. Use `ContentEncoding: "encrypted"` to distinguish from legacy plaintext.

**Rationale**:
- PayloadModel already has `Challenges` (wrapped keys), `IV` (nonce), `Hash` (integrity), `Data` (payload)
- Adding `ContentEncoding: "encrypted"` flag enables backward compatibility — legacy unencrypted transactions have `"identity"` or null
- No breaking changes to MongoDB schema — MongoPayloadDocument maps 1:1
- Existing `PayloadManager.IsLegacy()` (line 510) detects zeroed IV for backward compat

**Wire format per EncryptedPayloadGroup**:
```json
{
  "walletAccess": ["addr1", "addr2", "addr3"],
  "payloadSize": 1234,
  "hash": "<SHA-256 of plaintext, Base64url>",
  "data": "<encrypted payload, Base64url>",
  "iv": { "data": "<nonce, Base64url>", "address": null },
  "challenges": [
    { "data": "<wrapped-key-for-addr1, Base64url>", "address": "addr1" },
    { "data": "<wrapped-key-for-addr2, Base64url>", "address": "addr2" },
    { "data": "<wrapped-key-for-addr3, Base64url>", "address": "addr3" }
  ],
  "contentType": "application/json",
  "contentEncoding": "encrypted",
  "payloadFlags": "xchacha20-poly1305"
}
```

## R10: Algorithm Capability Matrix

| Algorithm | Key Wrap (Encrypt) | Key Unwrap (Decrypt) | Max Plaintext | Status |
|-----------|-------------------|---------------------|---------------|--------|
| ED25519 (Curve25519 SealedBox) | CryptoModule.cs:498 | CryptoModule.cs:515 | Unlimited (stream) | Working |
| NIST P-256 (ECIES) | CryptoModule.cs:627 | CryptoModule.cs:635 | Unlimited (hybrid) | NOT IMPLEMENTED |
| RSA-4096 (OAEP-SHA256) | CryptoModule.cs:720 | CryptoModule.cs:742 | 446 bytes | Working (32-byte key wrap OK) |
| ML-KEM-768 (KEM) | WalletEndpoints.cs:1245 | WalletEndpoints.cs:1302 | 32 bytes (shared secret) | Encapsulate works, Decapsulate BROKEN |

All algorithms can wrap a 32-byte symmetric key (XChaCha20-Poly1305 key size). RSA-4096's 446-byte limit is not a concern for key wrapping.

## R11: Existing Code Reuse Summary

| Component | Reuse Level | Notes |
|-----------|------------|-------|
| PayloadManager.AddPayloadAsync(data, RecipientKeyInfo[]) | Direct reuse | Already implements envelope encryption |
| SymmetricCrypto (XChaCha20-Poly1305) | Direct reuse | Default symmetric cipher, in-process |
| CryptoModule.EncryptAsync (ED25519, RSA-4096) | Direct reuse | Key wrapping works |
| DisclosureProcessor | Direct reuse | Field filtering unchanged, encryption added after |
| ActionsHub + NotificationService | Extend | Add new event types for progress |
| RegisterServiceClient.ResolvePublicKeyAsync | Extend | Add batch overload |
| TransactionReceiver | Fix | Add size enforcement |
| ITransactionBuilderService extension | Replace | New method that accepts encrypted payloads |
| CryptoModule P-256 | New implementation | ECIES from scratch using .NET crypto |
| WalletEndpoints Decapsulate | Fix | Call DecryptWithKemAsync |
