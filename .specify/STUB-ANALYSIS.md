# Sorcha Codebase — Stub & TODO Analysis

**Generated:** 2026-02-06
**Scope:** All source projects under `src/` (test files excluded)
**Total TODO comments:** 461+ | **NotImplementedException:** 5 | **Stub returns:** 43+

---

## CRITICAL (Blocks Core Functionality)

### 1. PayloadManager — All Encryption is No-Op
**File:** `src/Common/Sorcha.TransactionHandler/Payload/PayloadManager.cs`
**Lines:** 58-70, 89-91, 118-119, 141-142, 152
- `AddPayloadAsync`: Data stored unencrypted, IV is `new byte[12]`, hash is `new byte[32]`, encrypted keys are `new byte[32]`
- `DecryptPayloadAsync`: Returns raw data without decryption
- `AddRecipientAsync`: Key encryption is `new byte[32]`
- `VerifyPayloadAsync`: Always returns true
- **Impact:** Entire DAD security model compromised — all ledger data visible in plaintext

### 2. Transaction Binary Serialization — NotImplementedException
**Files:**
- `src/Common/Sorcha.TransactionHandler/Core/Transaction.cs:161` — `SerializeToBinary()` throws
- `src/Common/Sorcha.TransactionHandler/Serialization/JsonTransactionSerializer.cs:108, 115` — `SerializeToBinary()` and `DeserializeFromBinary()` throw
- **Impact:** System can only use JSON serialization (inefficient for production network transmission)

### 3. Wallet Address Generation — NotImplementedException
**File:** `src/Core/Sorcha.Wallet.Core/Services/Implementation/WalletManager.cs:409-411`
- Server cannot generate HD wallet addresses; requires mnemonic not stored server-side
- **Impact:** Client-side derivation only, limits server-initiated operations

### 4. Validator Schema Validation — Stub
**File:** `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs:317-318`
- Comment: "TODO: Implement actual schema validation against action.Schema"
- Only verifies action exists, doesn't validate data against schema
- **Impact:** Invalid data can be committed to the ledger

### 5. Validator Chain Validation — Stub
**File:** `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs:437-440`
- `await Task.CompletedTask` placeholder for Register Service call
- **Impact:** Blockchain integrity validation non-functional

---

## HIGH (Degraded Functionality)

### 6. Delegation Service — NotImplementedException
**File:** `src/Core/Sorcha.Wallet.Core/Services/Implementation/DelegationService.cs:210`
- Cannot update existing wallet access delegations; must delete and recreate

### 7. Transaction Version Adapters — Not Implemented
**File:** `src/Common/Sorcha.TransactionHandler/Versioning/TransactionFactory.cs:88, 96, 104, 113`
- V1/V2/V3 adapters not implemented — breaks upgrade path and network compatibility

### 8. Keychain Import/Export — Not Implemented
**File:** `src/Common/Sorcha.Cryptography/Models/KeyChain.cs:112, 130`
- Cannot backup or restore keychains — user lock-in and data loss risk

### 9. Validator Registry — In-Memory Only
**File:** `src/Services/Sorcha.Validator.Service/Services/ValidatorRegistry.cs:285, 316, 436`
- Comments: "TODO: In production, create registration/approval transaction on chain"
- Loses state on restart

### 10. Peer Heartbeat Sync — Placeholder Version Tracking
**File:** `src/Services/Sorcha.Peer.Service/Services/HeartbeatService.cs:74-76, 163-164`
- Always reports "in sync" — placeholder `currentSystemRegisterVersion = request.LastSyncVersion`

---

## MEDIUM (Missing Features)

### 11. Control Blueprint Historical Reconstruction
**File:** `src/Services/Sorcha.Validator.Service/Services/ControlBlueprintVersionResolver.cs:362, 373`
- Cannot validate transactions against historical control blueprint states

### 12. Genesis Config Change Detection
**File:** `src/Services/Sorcha.Validator.Service/Services/GenesisConfigService.cs:687`
- Stub comparison logic for config changes

### 13. Service Auth Secret Storage — Insecure
**File:** `src/Services/Sorcha.Tenant.Service/Services/ServiceAuthService.cs:340-346`
- Secrets hashed not encrypted; should use Azure Key Vault

### 14. RIPEMD-160 Hash — Using SHA-256 Placeholder
**File:** `src/Common/Sorcha.Cryptography/Core/HashProvider.cs:173-174`
- May break compatibility with systems expecting RIPEMD-160

### 15. SignalR Redis Backplane — Not Configured
**File:** `src/Services/Sorcha.Blueprint.Service/Program.cs:84`
- Single-server only; won't scale to multi-instance

---

## LOW (Cleanup / Improvements)

### 16. Transaction Placeholder Sender Wallet
**File:** `src/Common/Sorcha.TransactionHandler/Core/Transaction.cs:112`
- `SenderWallet = "ws1temp"` placeholder

### 17. JWT Token Extraction — TODO
**Files:** `src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs:1040,1046`, `DelegationEndpoints.cs:245`
- Hardcoded user IDs instead of extracting from JWT claims

### 18. Action Routing Rules — Stub
**File:** `src/Services/Sorcha.Blueprint.Service/Program.cs:492`
- `IsAvailable = true` — no routing logic applied

### 19. Participant Wallet Resolution — MVP
**File:** `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionResolverService.cs:131-133`
- Simple metadata resolution; should integrate with Participant Service API

### 20. Peer Transaction Processing — Placeholder
**File:** `src/Services/Sorcha.Peer.Service/PeerService.cs:297-301, 307`
- "placeholder for Sprint 4" — not implemented

### 21. Heartbeat Status Tracking — Placeholder
**File:** `src/Services/Sorcha.Peer.Service/Services/HeartbeatService.cs:228-229`
- Returns dummy status data

### 22. Fork Detection — Not Implemented
**File:** `src/Services/Sorcha.Validator.Service/GrpcServices/ValidatorGrpcService.cs:147`
- `IsFork = false` — planned for User Story 5

### 23. Consensus Phase Trigger — Placeholder
**File:** `src/Services/Sorcha.Validator.Service/Services/DocketBuildTriggerService.cs:113`
- "TODO Phase 5: Trigger consensus process here"

### 24. Admin Bootstrap Token Placeholders
**File:** `src/Services/Sorcha.Tenant.Service/Endpoints/BootstrapEndpoints.cs:161-162`
- Returns `"USE_LOGIN_ENDPOINT"` placeholder tokens

### 25. Organization User Count — Stub
**File:** `src/Services/Sorcha.Tenant.Service/Services/OrganizationService.cs:373`
- Returns 0 or placeholder

---

## Recommended Priority Order

1. **PayloadManager encryption** (Critical #1) — foundation of DAD model
2. **Validator schema + chain validation** (Critical #4-5) — ledger integrity
3. **Transaction binary serialization** (Critical #2) — production efficiency
4. **Validator registry persistence** (High #9) — state durability
5. **Transaction version adapters** (High #7) — upgrade path
6. **Keychain import/export** (High #8) — user data safety
7. **SignalR Redis backplane** (Medium #15) — horizontal scaling
8. **Service auth Key Vault** (Medium #13) — production security
