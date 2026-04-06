# Data Model: Validator Key Roster

**Feature**: 086-validator-key-roster  
**Date**: 2026-04-06

## New Entities

### ValidatorRosterEntry

An authorized validator's signing key declaration.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| ValidatorId | string | Yes | Unique identifier for the validator (wallet address or DID) |
| PublicKey | string (Base64) | Yes | Purpose-derived public key (not the system wallet master key) |
| Algorithm | SignatureAlgorithm (enum) | Yes | ED25519, NISTP256, RSA4096, ML_DSA_65, SLH_DSA_128s |
| DerivationContext | string | Yes | Derivation path used to derive this key (e.g., "sorcha:docket-signing") |
| Status | ValidatorKeyStatus (enum) | Yes | Active, Rotated, Revoked |
| AuthorizedAt | DateTimeOffset | Yes | When this key was authorized |
| RevokedAt | DateTimeOffset? | No | When this key was rotated/revoked (null if Active) |

**Validation rules**:
- PublicKey must be valid Base64, non-empty
- ValidatorId must be non-empty, max 255 characters
- DerivationContext must be non-empty
- Status transitions: Active → Rotated, Active → Revoked (no reverse)

### ValidatorRoster

The collection of authorized validator keys plus threshold parameters.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Validators | List\<ValidatorRosterEntry\> | Yes | Authorized validator entries (1-10 items) |
| RequiredSignatures | int | Yes | Minimum valid signatures per docket (default: 1) |
| Version | int | Yes | Roster version, incremented on each governance update |

**Validation rules**:
- Validators list must contain at least 1 entry (FR-010)
- Validators list must contain at most 10 entries (SC-005)
- RequiredSignatures must be >= 1 and <= count of Active validators
- At least one validator must have Status = Active
- No duplicate ValidatorId values

### ValidatorKeyStatus (enum)

| Value | Meaning |
|-------|---------|
| Active | Key is authorized for signing new dockets |
| Rotated | Key replaced by a newer key; can still verify historical dockets |
| Revoked | Key permanently revoked; rejected for all purposes |

## Modified Entities

### RegisterControlRecord (extended)

**Existing fields** (unchanged): RegisterId, Name, Description, CreatedAt, Attestations, CryptoPolicy, RegisterPolicy, Metadata

**New field**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Validators | ValidatorRoster | Yes | Authorized validator signing keys and threshold parameters |

**Impact**: This field is serialized into the genesis control transaction payload (Base64Url-encoded JSON). All code that reads/writes the RegisterControlRecord must handle the new field.

### ValidatorKeyCache (peer service, modified)

**Current**: `ConcurrentDictionary<string, ValidatorKeyEntry>` — single key per register  
**New**: `ConcurrentDictionary<string, ValidatorRosterCache>` — full roster per register

**ValidatorRosterCache** (new internal type):

| Field | Type | Description |
|-------|------|-------------|
| RegisterId | string | Register identifier |
| AuthorizedKeys | List\<ValidatorKeyEntry\> | All Active + Rotated keys (for verification) |
| RequiredSignatures | int | Threshold from roster |
| ResolvedFrom | string | Source: "genesis-control-record" |
| ResolvedAt | DateTimeOffset | When resolved |

**Lookup change**: `IsAuthorizedSigner(registerId, publicKey)` replaces `TryGetKey(registerId)`. Returns true if publicKey matches any Active entry.

## State Transitions

### ValidatorRosterEntry Lifecycle

```
                ┌──────────┐
   Created ──▶  │  Active   │
                └─────┬─────┘
                      │
            ┌─────────┴──────────┐
            ▼                    ▼
      ┌──────────┐        ┌──────────┐
      │ Rotated  │        │ Revoked  │
      └──────────┘        └──────────┘
```

- **Active → Rotated**: Key replaced by a newer key. Old key remains for historical verification.
- **Active → Revoked**: Key permanently invalidated (e.g., compromise, validator decommission).
- No transitions from Rotated or Revoked.

### ValidatorRoster Version History

Each governance update that modifies the validator roster increments the `Version` field. The full roster snapshot is stored in each control transaction (not diffs), matching the existing pattern for admin attestations.
