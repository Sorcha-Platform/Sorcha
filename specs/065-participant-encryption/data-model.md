# Data Model: 065 Participant Resolution, Starting Action Binding & Field-Level Encryption

## Entity Changes

### Modified: Participant (Blueprint.Models)

| Field | Type | Change | Notes |
|-------|------|--------|-------|
| Id | string | Unchanged | Required |
| Name | string | Unchanged | Required |
| Organisation | string | Unchanged | Organisation name for resolution |
| WalletAddress | string | **Make optional** | Remove `[Required]` — only used as hint, not enforcement |
| Description | string | Unchanged | Optional |
| Instructions | string | Unchanged | Optional |

**Validation change**: `WalletAddress` becomes optional. When present, it's treated as a pre-bound hint (backwards compatible). When absent, resolution happens at execution time.

### Modified: Instance (Blueprint.Service)

| Field | Type | Change | Notes |
|-------|------|--------|-------|
| ParticipantWallets | Dictionary&lt;string, string&gt; | **Extended usage** | Now populated at action execution for starting actions, not just at publish |

**Behaviour change**: When a starting action executes, the sender's wallet is written to `ParticipantWallets[senderParticipantId]` if not already present. This binding is immutable for the instance lifetime.

### Modified: Register (Register.Models)

| Field | Type | Change | Notes |
|-------|------|--------|-------|
| DevMode | bool | **New field** | Defaults to `false`. When `true`, payloads stored as plaintext |

**Storage**: Persisted in MongoDB register document. Set at register creation, toggleable by register owner.

### Existing (No Changes Required)

| Entity | Location | Why Unchanged |
|--------|----------|---------------|
| CryptoPolicy | Register.Models | Governs algorithm choice, not encryption toggle |
| PublishedParticipantRecord | ServiceClients.Register | Already supports multiple addresses + delegation |
| EncryptedPayloadGroup | TransactionHandler | Encryption output format unchanged |
| DisclosureGroup | TransactionHandler | Grouping logic unchanged |
| EncryptionPipelineService | TransactionHandler | Encryption implementation unchanged |

## State Transitions

### Instance Participant Binding

```
Starting Action Submitted
  → Check ParticipantWallets[senderRole]
    → Empty? → Bind sender wallet → ParticipantWallets[senderRole] = senderWallet
    → Already bound? → Verify sender == bound wallet → Accept or Reject (VAL_BP_002)
```

### Register DevMode Transition

```
DevMode: true (creation default for dev registers)
  → All payloads stored plaintext
  → Disclosure filtering at read time

Admin disables DevMode → DevMode: false
  → New payloads encrypted (envelope encryption)
  → Existing plaintext payloads remain readable (flagged as unencrypted)
  → No retroactive encryption
```

## Validation Rule Changes

### VAL_BP_002 (Modified)

**Current**: Match signer wallet against `participant.WalletAddress` (hardcoded in blueprint).

**New**: Three-tier resolution:
1. **Starting action** (`isStartingAction: true`): Accept any wallet. Bind to participant role.
2. **Instance-bound participant**: Match against `instance.ParticipantWallets[participantId]`.
3. **Register participant**: Query `ParticipantIndexService.GetByParticipantName(registerId, participantName, orgName)` → check if signer wallet is in `Addresses[]`.
