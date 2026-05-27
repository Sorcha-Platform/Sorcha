# Phase 1 Data Model — Sorcha Wallet (Full User-Agent v1)

**Feature**: 125-sorcha-wallet-user-agent
**Date**: 2026-05-14

This feature adds five new client-side data shapes (PWA IndexedDB), one schema column on an existing server-side table (`PlatformUserPersona.ContextOrgId`), and one abstraction interface (`IUserSigner`) backed by a single v1 implementation. No new server-side tables.

## Server-side: `PlatformUserPersona.ContextOrgId` (additive column)

**Storage**: Existing `PlatformUserPersona` EF entity in Tenant Service (Feature 092). The encryption envelope (XChaCha20-Poly1305, 24-byte nonce, content key derived under `sorcha:persona-vault` slot 104) is unchanged. The only change is row discrimination.

**Schema delta**:

| Column | Today | After Spec 2 | Notes |
|---|---|---|---|
| `PlatformUserId` | PK | PK part 1 | unchanged |
| `ContextOrgId` | — | **NEW**, nullable `Guid?` | null = Personal context; non-null = scoped to that org |
| Composite key | `PlatformUserId` | `(PlatformUserId, ContextOrgId)` | unique index across the pair |
| Encryption columns | unchanged | unchanged | nonce, ciphertext, wrappedKeyRef, schemaVersion all preserved |

**Migration**: Follows the pre-release migration-squash rule from `feedback_migration_squash` memory — the column is folded into the InitialCreate migration of the Tenant Service rather than added as a separate migration. Data preservation: existing rows get `ContextOrgId = NULL` (Personal context), so existing personas remain valid and visible under the Personal context.

**Invariants**:
- A `PlatformUser` may have at most one persona per `(PlatformUserId, ContextOrgId)` pair.
- `ContextOrgId = NULL` is the Personal-context row, always present (lazily created on first read by today's GET-with-empty-default behaviour).
- Setting `ContextOrgId` to a non-null value requires the caller's JWT to carry an OrgMembership for that org.

**Lifecycle**:
- **Created** on first `PUT /me/persona?context=<orgId>` for that pair.
- **Read** via `GET /me/persona?context=<orgId>` (default `?context=` omitted → Personal context).
- **Deleted** via `DELETE /me/persona?context=<orgId>`. Idempotent.

## PWA-side: `VerificationRecord` (new IndexedDB store)

**Storage**: New IndexedDB store `verifications` in the wallet's existing database. One row per verification performed by the user.

**Wire shape**:

```csharp
public sealed record VerificationRecord(
    Guid Id,                              // client-generated GUID for primary key
    DateTimeOffset VerifiedAt,            // UTC
    Guid? ContextOrgId,                   // null = Personal context
    string HolderDisplayName,             // from the verified credential's holder display
    string IssuerOrgName,                 // from the verified credential's issuer org
    string CredentialType,                // e.g. "WaterEngineerCredential/v1"
    VerifyOutcome Outcome,                // Pass / Warn / Fail
    string TrustPanelJson                 // serialised VerificationTrustView state for replay
);

public enum VerifyOutcome
{
    Pass,
    Warn,
    Fail
}
```

**Fields**:

| Field | Type | Validation | Notes |
|-------|------|------------|-------|
| `Id` | `Guid` | non-empty | client-generated |
| `VerifiedAt` | `DateTimeOffset` | UTC, ≤ now | informational only |
| `ContextOrgId` | `Guid?` | matches an OrgMembership the user holds, or null | filters Activity view |
| `HolderDisplayName` | `string` | non-empty, ≤ 120 chars | extracted from the credential's holder display |
| `IssuerOrgName` | `string` | non-empty, ≤ 120 chars | extracted from the credential's issuer org |
| `CredentialType` | `string` | non-empty, VCT-shape | the credential's `vct` |
| `Outcome` | enum | one of Pass / Warn / Fail | mapped from the verification pipeline's trust result |
| `TrustPanelJson` | `string` | valid JSON, ≤ 16 KB | serialised state to re-render `VerificationTrustView` on tap |

**Lifecycle**:
- **Created** at the end of every verification flow, regardless of outcome.
- **Read** by the Activity page when listing history and by the detail drawer when re-displaying a past verification.
- **Cleared** only by IndexedDB wipe (clear-site-data, uninstall+reinstall). No expiry / TTL in v1 (verification history is the user's private notebook).

**Invariants**:
- `Outcome` mirrors the trust panel's final verdict at the time of the verification. Re-running the verification later may produce a different verdict (e.g., a now-revoked credential); the historical record preserves the *original* verdict.
- `TrustPanelJson` is forward-compatible — readers tolerate unknown fields. Older records render with fewer details, never crash.

## PWA-side: `WalletFlagsRecord.TourDismissedAt` (extends F124 store)

**Storage**: Existing IndexedDB store `device`, key `flags` (introduced by F124). One record per device.

**Wire shape delta**:

```csharp
public sealed record WalletFlagsRecord(
    DateTimeOffset? WelcomedAt,       // F124 — first-credential welcome takeover dismissal
    DateTimeOffset? TourDismissedAt   // NEW — guided-tour dismissal
);
```

**Lifecycle**:
- **Created lazily** at first wallet flags read; defaults to both fields null.
- **`TourDismissedAt` set once** when the user completes or dismisses the tour. Tour does not re-fire on subsequent opens.
- **`TourDismissedAt` reset to null** when the user taps "Replay tour" in Settings.

**Invariants**:
- `TourDismissedAt` is per-device. Sarah's tour completion on her phone doesn't suppress the tour on her tablet — each device has its own first-time experience.
- Replay reset to null is the ONLY way to re-fire the tour besides clearing site data.

## PWA-side: `ActiveContextRecord` (new IndexedDB store)

**Storage**: New IndexedDB store `context`, key `active`. One record per device.

**Wire shape**:

```csharp
public sealed record ActiveContextRecord(
    Guid? ContextOrgId,                  // null = Personal context (the default)
    DateTimeOffset SwitchedAt            // UTC; for diagnostics
);
```

**Lifecycle**:
- **Created lazily** on first wallet open after enrolment; defaults to Personal (`ContextOrgId = null`).
- **Updated** on every context switch. Persists across reloads so reopening the wallet preserves the last-active context.
- **Cleared** only by IndexedDB wipe.

**Invariants**:
- `ContextOrgId` must reference an OrgMembership the user currently holds. If a context becomes invalid (user removed from the org), the wallet falls back to Personal on next open.
- The active context is persisted per device, NOT synced across devices. A user can have different active contexts on phone vs. tablet — a feature, not a bug (work context on the work tablet, personal on the phone).

## PWA-side: `PerContextPersonaCache` (new IndexedDB store)

**Storage**: New IndexedDB store `personas`, keyed by `(ContextOrgId ?? "personal")`. One row per loaded context's persona, cached client-side to reduce round trips during context switching.

**Wire shape**: Same shape as today's `PersonaReadModelV1` from the Tenant Service `/me/persona` endpoint. Each row is the encrypted-and-then-decrypted-client-side persona for a single context.

**Lifecycle**:
- **Created** on first read of a context's persona.
- **Refreshed** on every explicit user-driven persona edit, every context switch (the new context's persona is fetched), and at most every 15 minutes for the active context (stale-while-revalidate).
- **Cleared** on sign-out, IndexedDB wipe.

**Invariants**:
- Cache entries never outlive a sign-out. On sign-in, all persona rows are flushed.
- Cache MUST NOT be the source of truth for sensitive operations — form auto-fill is fine, but signing a persona update always goes server-side first.

## PWA-side: `IUserSigner` abstraction (interface; managed-mode implementation only in v1)

**Location**: `Sorcha.UI.Components.User.Services.Signing` (in the shared library, per R-002).

**Interface**:

```csharp
public interface IUserSigner
{
    /// <summary>The custody mode this signer implements.</summary>
    UserCustodyMode CustodyMode { get; }

    /// <summary>The user-visible label for the active signing identity (e.g. the active context name).</summary>
    string DisplayLabel { get; }

    /// <summary>
    /// Signs a payload under the current user / context identity. Implementations may
    /// require user consent; consumers SHOULD invoke this from a UI surface that has
    /// already presented a ConsentSheet or equivalent confirmation.
    /// </summary>
    Task<SigningResult> SignAsync(SigningRequest request, CancellationToken ct);
}

public enum UserCustodyMode
{
    Managed,         // v1 — server-anchored holder key, browser-local device key, delegation
    SelfCustody,     // v2 — BIP39 on device, no server custody
    CoSigned         // v2 backlog — collector + org dual signature
}

public sealed record SigningRequest(
    SigningOperation Operation,    // Presentation / ActionSubmission / DelegationRenewal / Generic
    byte[] PayloadToSign,           // raw bytes to sign
    string? AudienceClientId,       // for OID4VP presentations
    Guid? ActiveContextOrgId        // for context-scoped signing
);

public sealed record SigningResult(
    bool Success,
    byte[]? Signature,
    string? Algorithm,              // e.g. "ES256"
    string? ErrorCode,              // non-null on failure
    string? ErrorDetail             // non-null on failure
);

public enum SigningOperation
{
    Presentation,
    ActionSubmission,
    DelegationRenewal,
    Generic
}
```

**v1 implementation**: `ManagedUserSigner` in `Sorcha.Wallet.Pwa.Services`.

**Future v2 implementations** (carved out, NOT in this spec): `SelfCustodyUserSigner`, `CoSignedUserSigner`.

**Invariants**:
- Consuming components (ConsentSheet, PresentationSubmitDialog, action-submission flows) MUST NOT switch behaviour on `CustodyMode`. They invoke `SignAsync` and react to `SigningResult`. The user-visible UX for the "consent moment" is the same regardless of mode.
- `DisplayLabel` is what's surfaced in the consent moment — e.g., *"Sign as Ben (Personal)"* or *"Sign as Ben (Caledonian Builders Ltd)"*. Implementations choose their own labels.

## PWA-side: `EphemeralVerifierIdentity` (transient, no persistence)

**Location**: `Sorcha.UI.Components.User.Services.Signing.IEphemeralVerifierIdentityService`.

**Shape**:

```csharp
public interface IEphemeralVerifierIdentityService
{
    /// <summary>
    /// Generates a fresh EC P-256 key for a single verification session.
    /// Returns the client_id (public-key JWK thumbprint) to use in the OID4VP
    /// presentation request. Dispose disposes the key material.
    /// </summary>
    Task<EphemeralVerifierIdentity> BeginSessionAsync(CancellationToken ct);
}

public sealed class EphemeralVerifierIdentity : IDisposable
{
    public string ClientId { get; init; }   // RFC 7638 thumbprint of the public JWK
    public string PublicJwk { get; init; }  // serialised JSON

    public void Dispose() { /* zeroise key material */ }
}
```

**Lifecycle**:
- **Created** at the start of each verification session.
- **Lives** in memory for the duration of one verification (≤30 seconds typically).
- **Disposed** at the end — private key zeroised, no persistence.

**Invariants**:
- Each verification session uses a fresh identity.
- Private key NEVER persists to IndexedDB or any local store.
- The public JWK / `ClientId` may be temporarily included in audit log entries on the presenter's side — that's fine, it's intentionally throwaway.

## Entity-relationship view

```
┌────────────────────────────────────────────────────────────────────────────┐
│ Server (Tenant Service)                                                    │
│                                                                            │
│  PlatformUser                                                              │
│   ├── OrgMembership[]    (existing)                                        │
│   ├── PlatformUserPersona[]      ← NEW composite key: (PlatformUserId,     │
│   │     - PlatformUserId           ContextOrgId). null ContextOrgId =      │
│   │     - ContextOrgId? (NEW)      Personal context.                       │
│   │     - <ciphertext+nonce>                                               │
│   │     - WrappedKeyRef = walletAddress                                    │
│   │                                                                        │
│   └── PlatformUserDevice[]   (existing — Feature 114)                      │
└────────────────────────────────────────────────────────────────────────────┘
        ↑                                              ↑
        │ HTTP                                         │ HTTP
        │                                              │
┌────────────────────────────────────────────────────────────────────────────┐
│ PWA (Sorcha.Wallet.Pwa) — IndexedDB                                        │
│                                                                            │
│  device store:                                                             │
│   ├── enrolment       (existing — DeviceMetaRecord)                        │
│   └── flags           (extended — WalletFlagsRecord.TourDismissedAt NEW)   │
│                                                                            │
│  context store:                                                            │
│   └── active          NEW — ActiveContextRecord                            │
│                                                                            │
│  verifications store:                                                      │
│   └── [GUID]          NEW — VerificationRecord[]                           │
│                                                                            │
│  personas store:                                                           │
│   └── [contextKey]    NEW — PerContextPersonaCache                         │
│                                                                            │
│  credentials store:   (existing — CachedCredential[])                      │
│  delegation store:    (existing)                                           │
│  sync-cursor store:   (existing)                                           │
│  access-token store:  (existing)                                           │
└────────────────────────────────────────────────────────────────────────────┘
```

## State machines

### Active context

```
   (uninitialised on fresh wallet)
              │
              │  open wallet (post-enrolment)
              ▼
   (Personal, ContextOrgId = null) ◄────┐
              │                          │
              │  user taps context chip  │  user switches back to Personal
              │  + picks Caledonian      │
              ▼                          │
   (Caledonian Builders, ContextOrgId =  │
    <BuildersOrgId>)                     │
              │                          │
              └──────────────────────────┘
```

Switching context fires:
- New `/auth/switch-org` JWT acquisition
- `IAccessTokenStore` update
- Persona cache lookup (background; non-blocking if cache miss)
- Home content refresh

### Guided tour completion

```
   (TourDismissedAt = null on fresh device)
              │
              │  user opens wallet first time post-enrolment
              ▼
   (tour running)
              │
              │  user completes OR dismisses
              ▼
   (TourDismissedAt = now, frozen)
              │
              │  user taps "Replay tour" in Settings
              ▼
   (tour running again)
              │
              │  completes / dismisses
              ▼
   (TourDismissedAt = now, frozen)
```

### Verification record outcome

Each verification produces exactly one VerificationRecord. The record's `Outcome` is set when the trust panel finalises (Pass/Warn/Fail) and is never mutated thereafter. Re-running verification on the same credential produces a *new* record, not an update to the existing one.

## Cross-cutting invariants

- **Per-context scoping is enforced server-side, not in IndexedDB.** Client-side stores cache for performance and offline-readability; the security boundary lives at the JWT.
- **Verification records never reference holder PII beyond display name and issuer.** No PII storage in the wallet beyond what the citizen has chosen to verify and what the verified credential surfaces.
- **All client-side persistent stores survive PWA refresh + browser restart, not clear-site-data.** Acceptable v1 behaviour per the F124 precedent.
