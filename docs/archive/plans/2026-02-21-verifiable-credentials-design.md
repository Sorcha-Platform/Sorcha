# Verifiable Credentials & Presentations — System Design

**Date:** 2026-02-21
**Status:** Approved
**Branch:** TBD (038-verifiable-credentials or similar)

---

## Overview

Extend Sorcha's existing credential foundation (SD-JWT, credential models, wallet storage, blueprint engine integration) into a complete verifiable credential system with:

- **Multi-method DID resolution** (`did:sorcha`, `did:web`, `did:key`)
- **W3C Bitstring Status List** for privacy-preserving revocation/suspension
- **OID4VP presentation protocol** with QR code support for in-person verification
- **Card-based wallet UI** for credential display and management
- **Full lifecycle management** (Active → Suspended → Revoked → Expired → Consumed)
- **Cross-blueprint credential flows** (Blueprint A issues, Blueprint B requires)

### Architectural Approach: Wallet-Centric

The wallet is the primary credential manager — it stores credentials, creates presentations, manages selective disclosure, tracks usage, and renders the card UI. The register serves as the immutable audit trail and hosts the canonical Bitstring Status List. This aligns with the W3C/EUDI direction the industry has converged on.

---

## 1. DID Resolution Layer

### Problem

Sorcha currently has `did:sorcha:w:{walletAddress}` and `did:sorcha:r:{registerId}:t:{txId}`. To verify credentials issued by external systems or to allow external verifiers to check Sorcha-issued credentials, we need multi-method DID resolution.

### Design

Pluggable resolver registry with method-specific implementations.

```csharp
public interface IDidResolver
{
    Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default);
    bool CanResolve(string didMethod);
}

public interface IDidResolverRegistry
{
    Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default);
    void Register(IDidResolver resolver);
}
```

### Shipped Methods

| Method | Resolution Strategy | Key Material |
|--------|-------------------|--------------|
| `did:sorcha` | Query register for TX, extract public key from wallet service | ED25519, P-256, RSA-4096 |
| `did:web` | HTTPS GET `https://{domain}/.well-known/did.json` | Whatever the document declares |
| `did:key` | Decode multicodec-encoded public key from DID string itself | ED25519, P-256 |

### DidDocument Model

Follows W3C DID Core: `id`, `verificationMethod[]`, `authentication[]`, `assertionMethod[]`, `service[]`.

### Location

`src/Common/Sorcha.ServiceClients/Did/` — shared across all services. Each service opts into methods via DI.

---

## 2. Credential Lifecycle & Bitstring Status List

### Lifecycle State Machine

```
                    ┌──────────┐
   Issue ──────────▶│  Active   │◀──── Reinstate
                    └─────┬────┘
                          │
              ┌───────────┼───────────┐
              ▼           ▼           ▼
        ┌──────────┐ ┌──────────┐ ┌──────────┐
        │Suspended │ │ Expired  │ │ Revoked  │
        └──────────┘ └─────┬────┘ └──────────┘
              │             │         (terminal)
              │ Reinstate   │ Refresh
              └──▶ Active ◀─┘
```

**States:**
- **Active** — valid, usable. Default on issuance.
- **Suspended** — temporarily disabled. Reversible by issuer.
- **Revoked** — permanently withdrawn. Terminal.
- **Expired** — past `expiresAt`. Refreshable via OID4VCI re-issuance if credential type supports it.
- **Consumed** — single-use/limited-use credential exhausted. Terminal (equivalent to revoked on status list).

### Bitstring Status List (W3C Standard)

Each issuer maintains a status list per register. Compressed bitstring where each credential occupies a fixed position.

- Two separate bitstrings per list: `revocation` purpose and `suspension` purpose
- Default list size: 131,072 entries (16KB compressed, W3C recommended minimum for privacy)
- Each credential embeds a `credentialStatus` claim pointing to list URL + index

### Storage

**Register (canonical):** Control TX with payload type `StatusList` — compressed bitstring, metadata, version.

**Blueprint Service (cached HTTP):** `GET /api/v1/credentials/status-lists/{listId}` — returns W3C Bitstring Status List VC signed by issuer. `Cache-Control: max-age=300` (5 min default, configurable).

### Credential Status Claim (embedded in every VC)

```json
{
  "credentialStatus": {
    "id": "https://sorcha.example/api/v1/credentials/status-lists/abc123#42",
    "type": "BitstringStatusListEntry",
    "statusPurpose": "revocation",
    "statusListIndex": "42",
    "statusListCredential": "https://sorcha.example/api/v1/credentials/status-lists/abc123"
  }
}
```

### Usage Policy

Added to `CredentialIssuanceConfig`:

```csharp
public enum UsagePolicy { Reusable, SingleUse, LimitedUse }

public UsagePolicy UsagePolicy { get; init; } = UsagePolicy.Reusable;
public int? MaxPresentations { get; init; } // Only for LimitedUse
```

SingleUse: verifier callback triggers issuer to flip revocation bit → consumed.
LimitedUse: wallet tracks presentation count, bit flipped when limit reached.

---

## 3. OID4VP Presentation & QR Code Flow

### Remote Flow (Web-Based)

1. Verifier creates presentation request (type, issuer constraints, nonce, callback URL)
2. Wallet service notifies holder (SignalR / push)
3. User selects credential + approves disclosure scope
4. Wallet creates SD-JWT presentation bound to nonce
5. Wallet POSTs `vp_token` to verifier's callback URL (`response_mode=direct_post`)
6. Verifier validates: signature, status list, claims, nonce

### QR Code / In-Person Flow

Same protocol, QR-initiated:

1. Verifier terminal displays QR code (encoded: request URL + nonce)
2. User scans QR with phone camera
3. Wallet PWA fetches full request from URL in QR
4. User approves disclosure
5. Wallet POSTs `vp_token` via `direct_post` to verifier
6. Terminal receives verification result

**Key properties:**
- QR encodes a URL, not the credential (small QR, any credential size)
- Nonce prevents replay
- Selective disclosure UI shows user exactly what will be shared
- `response_mode=direct_post` — wallet POSTs directly to verifier over HTTPS

### Wallet Service Endpoints

```
POST /api/v1/presentations/request     → Create presentation request (verifier)
GET  /api/v1/presentations/{id}        → Get request details (wallet UI)
POST /api/v1/presentations/{id}/submit → Submit presentation (wallet)
GET  /api/v1/presentations/{id}/result → Get verification result (verifier)
```

### Blueprint Integration

When a blueprint action has `CredentialRequirement`:
1. Engine creates presentation request via wallet service
2. UI shows `CredentialGatePanel` (existing component)
3. User selects credential + approves disclosure
4. OID4VP presentation verified
5. Action proceeds if valid

---

## 4. Wallet UI — Credential Cards

### Credentials Tab

New tab in wallet UI. Displays credentials as visual cards (Apple Wallet style).

### Card Visual Template

`CredentialDisplayConfig` on `CredentialIssuanceConfig`:

```csharp
public class CredentialDisplayConfig
{
    public string BackgroundColor { get; init; } = "#1976D2";
    public string TextColor { get; init; } = "#FFFFFF";
    public string Icon { get; init; } = "Certificate"; // MudBlazor icon name
    public string CardLayout { get; init; } = "Standard"; // Standard, Compact, Ticket
    public Dictionary<string, string> HighlightClaims { get; init; } = new();
}
```

Issuer defines how their credential looks. Default generated from credential type name if not specified.

### Card States

| Status | Visual | Actions |
|--------|--------|---------|
| Active | Green dot, full color | View, Present, Export |
| Suspended | Amber dot, muted | View (can't present) |
| Revoked | Red dot, greyed out | View, Delete |
| Expired | Grey dot, "Expired" badge | View, Renew |
| Consumed | Strikethrough, "Used" badge | View, Delete |
| Expiring Soon | Amber warning, countdown | View, Present, Renew |

### Detail View

Tap a card to expand:
- All claims with disclosure scope indicators
- Full credential metadata (issuer DID, issuance TX, timestamps)
- Usage history (presentations made)
- Status check button (force-refresh from status list)
- QR code generation for in-person presentation
- Export as SD-JWT VC

### Presentation Request Inbox

Incoming presentation requests appear as notifications showing:
- Verifier identity
- Requested claims
- Matching credential
- What will be disclosed vs. hidden
- Approve / Deny

---

## 5. Cross-Blueprint Credential Flows

### Issuance Path

```
Blueprint action completes with CredentialIssuanceConfig
  → Engine CredentialIssuer creates SD-JWT VC
    → Claims mapped from action data
    → credentialStatus embedded (status list URL + index)
    → UsagePolicy and DisplayConfig attached
  → Credential stored in holder's wallet
  → Issuance TX written to register (audit trail)
  → Status list entry allocated (bit = 0 = active)
```

### Verification Path

```
User submits action with CredentialRequirement
  → Wallet searched for matching credentials
  → User selects + approves disclosure
  → OID4VP presentation created
  → Engine CredentialVerifier validates:
     1. Signature valid (issuer key via DID resolution)
     2. Type matches requirement
     3. Issuer in accepted list
     4. Required claims present with constraints
     5. Status list check (not revoked/suspended/consumed)
     6. Nonce matches (replay prevention)
     7. Usage policy check
  → If valid, action proceeds
  → If SingleUse, status bit flipped → consumed
```

### Cross-Blueprint Example

```
Blueprint A: "Chemical Handling License"
  Action 3 (final): Issues CredentialType="ChemicalHandlingLicense"

Blueprint B: "Chemical Purchase Order"
  Action 0 (initiation): Requires CredentialType="ChemicalHandlingLicense"
    → Worker presents credential from wallet
    → Verifier checks status list on Blueprint A's register
    → If HSE suspends the license → status bit flips → future purchases blocked
```

### Revocation/Suspension Operations

```
POST /api/v1/credentials/{credentialId}/revoke     → Permanent
POST /api/v1/credentials/{credentialId}/suspend     → Temporary
POST /api/v1/credentials/{credentialId}/reinstate   → Undo suspend
```

Each operation: updates wallet status → flips status list bit → writes Control TX to register → invalidates cache.

### Refresh/Reissuance

Expired credential with refresh support: User clicks Renew → Wallet calls issuer refresh endpoint (OID4VCI re-issuance) → Issuer validates original → New credential issued → Old consumed → New appears in wallet.

---

## 6. Component Summary

### New Components

| Component | Location |
|-----------|----------|
| `IDidResolver` + `IDidResolverRegistry` | `src/Common/Sorcha.ServiceClients/Did/` |
| `SorchaDidResolver` | Same |
| `WebDidResolver` | Same |
| `KeyDidResolver` | Same |
| `DidDocument` model | `src/Common/Sorcha.Register.Models/` |
| `BitstringStatusList` | `src/Common/Sorcha.Blueprint.Models/Credentials/` |
| `StatusListManager` | `src/Services/Sorcha.Blueprint.Service/Services/` |
| `StatusListEndpoints` | `src/Services/Sorcha.Blueprint.Service/Endpoints/` |
| `PresentationEndpoints` | `src/Services/Sorcha.Wallet.Service/Endpoints/` |
| `PresentationRequestService` | `src/Services/Sorcha.Wallet.Service/Services/` |
| `CredentialCards.razor` | `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/` |
| `CredentialDetailView.razor` | Same |
| `PresentationRequestDialog.razor` | Same |
| `QrPresentationService` | `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/` |

### Extended Components

| Component | Changes |
|-----------|---------|
| `CredentialIssuanceConfig` | Add `UsagePolicy`, `MaxPresentations`, `DisplayConfig` |
| `CredentialEntity` | Add Suspended/Consumed states, `StatusListIndex`, `PresentationCount` |
| `CredentialVerifier` | Add status list check, usage policy, nonce validation |
| `CredentialIssuer` | Add status list allocation, display config |
| `CredentialEndpoints` (Wallet) | Add suspend/reinstate/refresh endpoints |
| `CredentialEndpoints` (Blueprint) | Add suspend/reinstate endpoints, status list serving |
| Wallet YARP routes | Presentation + status list routes |

---

## 7. Standards Alignment

| Standard | Usage |
|----------|-------|
| W3C VC Data Model 2.0 | Credential structure |
| IETF SD-JWT VC (RFC 9901) | Credential securing (existing) |
| W3C Bitstring Status List v1.0 | Revocation/suspension |
| W3C DID Core | DID resolution |
| OID4VP (OpenID for Verifiable Presentations) | Presentation protocol |
| OID4VCI (OpenID for Verifiable Credential Issuance) | Credential refresh/reissuance |

---

## 8. Out of Scope (Future Phases)

- BLE/NFC proximity transfer (ISO 18013-5)
- DIF Presentation Exchange v2 format
- `did:ethr`, `did:ion`, `did:indy` resolvers (pluggable interface ready)
- Holder key binding JWT generation
- Credential versioning/migration
- Mobile native SDK
