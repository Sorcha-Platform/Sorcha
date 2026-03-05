# Feature 050: Identity & Credentials Admin — Design

**Date:** 2026-03-05
**Status:** Approved
**Branch:** TBD (050-identity-credentials-admin)

---

## Overview

Feature 050 adds admin UI components and CLI commands for identity and credential management. All backend endpoints and most UI service interfaces already exist — this feature builds the user-facing layer on top.

## Scope

| Item | Backend | UI Service | New Work |
|------|:-------:|:----------:|----------|
| Credential Lifecycle (revoke/suspend/reinstate/refresh) | 4 endpoints | Partial | Action buttons + dialogs, 3 new service methods, 3 CLI commands |
| Participant Publishing (publish/update/revoke on register) | 3 endpoints | Full | Publish button + dialog, 2 CLI commands |
| Participant Suspend/Reactivate | 2 endpoints | Full | Status buttons + dialogs, 2 CLI commands |
| Status List Management (W3C Bitstring) | 3 endpoints | None | Read-only viewer page, new service, 1 CLI command |
| Verifiable Presentations (OID4VP) | 5 endpoints | Partial | Holder flow + verifier page, 2 new service methods |

## Design Decisions

1. **All 5 items in one feature** — service layer exists, so implementation is thin (UI + CLI)
2. **Explicit wallet picker** in credential lifecycle dialogs — auditable, no auto-resolve
3. **Status list viewer is read-only** — allocate/set-bit are internal automated operations
4. **Both holder and verifier presentation UIs** — full OID4VP coverage
5. **Publish button on ParticipantDetail** — pre-fills from loaded context, no standalone page

---

## 1. Credential Lifecycle UI

### Location
`CredentialDetailView.razor` — add action button bar below existing detail display.

### Components
- **Action button bar** — visibility based on credential status:
  - Active: Suspend, Revoke
  - Suspended: Reinstate, Revoke
  - Expired: Refresh
  - Revoked: no actions
- **Confirmation dialog** for each action:
  - Wallet picker dropdown (user's linked wallets)
  - Optional reason text field
  - Warning banner for irreversible actions (Revoke)

### New UI Service Methods
Add to `ICredentialApiService` / `CredentialApiService`:
- `SuspendCredentialAsync(credentialId, issuerWallet, reason?)` — POST `/api/v1/credentials/{id}/suspend`
- `ReinstateCredentialAsync(credentialId, issuerWallet, reason?)` — POST `/api/v1/credentials/{id}/reinstate`
- `RefreshCredentialAsync(credentialId, issuerWallet, newExpiryDuration?)` — POST `/api/v1/credentials/{id}/refresh`

### API Gateway Routes
Credential endpoints route through YARP at `/api/v1/credentials/**` to Blueprint Service. Verify existing routes cover suspend/reinstate/refresh paths.

---

## 2. Participant Publishing UI

### Location
`ParticipantDetail.razor` — add "Publish to Register" button.

### Components
- **Publish button** — visible when participant is Active and user has Administrator role
- **Publish dialog** fields:
  - Register ID (dropdown of available registers)
  - Participant name (pre-filled from context)
  - Organization name (pre-filled from context)
  - Wallet addresses to publish (checkboxes from linked wallets, with public key + algorithm)
  - Signer wallet address (dropdown)
- **Published status indicator** showing registers where participant is published
- **Update/Revoke actions** for already-published records

### Service
`IParticipantPublishingService` already has `PublishAsync`, `UpdatePublishedAsync`, `RevokeAsync`.

---

## 3. Participant Suspend/Reactivate UI

### Location
`ParticipantDetail.razor` and `ParticipantList.razor`

### Components
- **Suspend/Reactivate buttons** on detail page:
  - Active: Suspend button
  - Suspended: Reactivate button
  - Inactive: no actions
- **Confirmation dialogs** — simple "Are you sure?" pattern
- **Status chip** on list items — color-coded (green=Active, amber=Suspended, grey=Inactive)

### Service
`IParticipantApiService` already has `SuspendParticipantAsync` and `ReactivateParticipantAsync`.

---

## 4. Status List Viewer

### Location
New admin page at `/admin/status-lists`

### Components
- **Status list table** — columns: List ID, Purpose (revocation/suspension), Issuer, Last Updated
- **Detail view** — click to see full W3C BitstringStatusListCredential JSON
- **Navigation entry** under Admin > Credentials section

### New UI Service
`IStatusListService` / `StatusListService`:
- `GetStatusListAsync(listId)` — GET `/api/v1/credentials/status-lists/{listId}` (public endpoint, no auth)

---

## 5. Verifiable Presentations UI

### Holder Side (Main UI)
Replace "planned for future release" placeholder in credentials section.

**Components:**
- **Presentation requests list** — pending requests targeting user's wallet
- **Request detail view** — verifier identity, required claims, matching credentials, expiry
- **Approve flow** — select credential, choose claims to disclose (checkboxes), confirm
- **Deny button** with confirmation

UI service methods exist: `GetPresentationRequestsAsync`, `SubmitPresentationAsync`, `DenyPresentationAsync`.

### Verifier Side (Admin UI)
New page at `/admin/presentations`

**Components:**
- **Create request form** — credential type, accepted issuers, required claims, target wallet, callback URL, TTL
- **Request status table** — pending/completed/denied/expired
- **Result view** — verification result with disclosed claims
- **QR code display** for `openid4vp://` authorize URL

### New UI Service Methods
Add to `ICredentialApiService` or new `IPresentationAdminService`:
- `CreatePresentationRequestAsync(request)` — POST `/api/v1/presentations/request`
- `GetPresentationResultAsync(requestId)` — GET `/api/v1/presentations/{id}/result`

---

## 6. CLI Commands

8 new commands following existing CLI patterns (Refit client, `BaseCommand.OutputOption`, auth handling):

| Command | Method | Endpoint |
|---------|--------|----------|
| `credential suspend --id <id> --wallet <addr> [--reason <text>]` | POST | `/api/v1/credentials/{id}/suspend` |
| `credential reinstate --id <id> --wallet <addr> [--reason <text>]` | POST | `/api/v1/credentials/{id}/reinstate` |
| `credential refresh --id <id> --wallet <addr> [--expires-in-days <n>]` | POST | `/api/v1/credentials/{id}/refresh` |
| `credential status-list get --id <listId>` | GET | `/api/v1/credentials/status-lists/{listId}` |
| `participant suspend --org-id <id> --id <id>` | POST | `/api/organizations/{orgId}/participants/{id}/suspend` |
| `participant reactivate --org-id <id> --id <id>` | POST | `/api/organizations/{orgId}/participants/{id}/reactivate` |
| `participant publish --org-id <id> --register-id <id> --name <n> --org-name <n> --wallet <addr> --signer <addr>` | POST | `/api/organizations/{orgId}/participants/publish` |
| `participant unpublish --org-id <id> --id <id> --register-id <id> --signer <addr>` | DELETE | `/api/organizations/{orgId}/participants/publish/{id}` |

### New Refit Interfaces
- Extend `ICredentialServiceClient` with suspend/reinstate/refresh/status-list methods
- Extend `IParticipantServiceClient` with suspend/reactivate/publish/unpublish methods

---

## Testing Strategy

### UI Service Tests (~40 tests)
Mock `HttpMessageHandler`, verify HTTP calls and response deserialization. Same pattern as Feature 049.
- CredentialApiService: suspend, reinstate, refresh (success + error cases)
- StatusListService: get (success + not found)
- PresentationAdminService: create request, get result

### CLI Command Tests (~35 tests)
Verify command structure, options, arguments, required flags.
- 8 new commands x ~4 tests each (structure + options + required args + aliases)

### Playwright E2E Tests (~15 tests)
- Credential lifecycle flow: suspend -> reinstate -> revoke
- Participant publish flow: publish -> view published -> revoke
- Participant suspend/reactivate flow
- Presentation holder flow: view request -> approve with selective disclosure
- Presentation verifier flow: create request -> view result

### Estimated Total: ~90 tests

---

## Architecture

```
Existing (no changes needed):     New work:
  Backend endpoints (17)            UI: ~10 Blazor components/dialogs
  UI service interfaces (most)      UI: ~5 new service methods
  Participant services              UI: 1 new service (StatusList)
  Credential services               UI: 1 new admin page (status-lists)
  YARP routes (verify only)         UI: 1 new admin page (presentations)
                                    CLI: 8 new commands
                                    CLI: Refit interface extensions
                                    Tests: ~90 tests
```

---

## Dependencies

- Feature 049 (merged) — admin infrastructure, service patterns
- Existing `IParticipantPublishingService` and `ICredentialApiService`
- YARP routes for credential and presentation endpoints (verify coverage)

## Risks

- **Credential lifecycle requires issuer wallet** — user must have linked wallets; handle gracefully if none linked
- **Presentation QR code** — may need a JS interop library for QR generation in Blazor
- **Status list data** — public endpoint, but lists may not exist in dev environment; handle empty state
