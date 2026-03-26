# Verifiable Credentials UX Design

**Date:** 2026-03-25
**Status:** Approved
**Scope:** Issuer, Holder, and Verifier UI/UX for the full VC lifecycle

---

## Overview

Design the UI/UX for Verifiable Credentials (VCs) across all three roles in the credential triangle: Issuer, Holder, and Verifier. Sorcha's backend VC infrastructure is mature (SD-JWT issuance, selective disclosure, credential verification, presentation requests, lifecycle management) but the UI layer has significant gaps — navigation stubs exist ("My Credentials", "Presentations") without fleshed-out pages.

This design covers the complete credential lifecycle: Issue → Hold → Verify, plus admin management pages for credential governance.

## Key Architectural Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Credential signer** | Organization wallet | Institutional authority — org's DID signs the VC. The participant who completed the action appears as a claim inside the VC, not as the signer. Mirrors real-world credentialing (university signs degree, not professor). |
| **Issuance trigger** | Blueprint-only | VCs are always the outcome of a blueprint action completing. The blueprint process *is* the approval — no manual admin issuance. Keeps issuance tightly governed. |
| **Visual style** | Material / list-friendly | Clean, structured cards that integrate with MudBlazor's Material Design. Metadata footer surfaces SD-JWT info (claim count, disclosure info). Scales for users with many credentials. Theming backlogged. |
| **UI integration** | Hybrid | Issuance and verification stay inline with blueprint actions (contextual). Holder-facing pages consolidated into enhanced "My Credentials" hub. Admin features under existing Administration section. Minimal nav changes. |
| **Holder acceptance** | Explicit accept/review gate | Holder must review claims and explicitly accept or decline. Consistent with DAD disclosure model — control starts at receipt. Provides audit point (when accepted) and chain of custody. |
| **Selective disclosure** | Request-matched auto-select | Verifier's request drives defaults. Required claims locked, optional claims toggleable (default OFF — privacy-preserving), unrequested claims greyed out. |
| **Verifier trust display** | Contextual depth | Green banner when all checks pass (details collapsed). Auto-expands verification checklist when warnings or failures detected. Only surfaces complexity when it matters. |
| **Status notifications** | Targeted SignalR push | Holder always notified. Active verifiers (in-progress action with CredentialRequirement) notified on status changes. Issuer admin dashboard refreshes on load. |

## Scope

### In Scope

- Issuance summary panel (issuer experience)
- Credential acceptance flow (holder experience)
- "My Credentials" holder hub with lifecycle tabs
- Selective disclosure presentation picker (holder experience)
- Verifier trust view with contextual depth (verifier experience)
- Issued Credentials admin page (org admin experience)
- SignalR notification integration for credential events
- Credential status change propagation

### Out of Scope (Backlogged)

| Item | Rationale |
|------|-----------|
| Credential card theming (certificate, identity card styles) | Material is the default. Branded cards are a future `CredentialDisplayConfig` → CSS mapping. |
| Register publication of credential status | Backend `StatusListEndpoints` exist. UI for publishing/querying status on shared registers deferred. |
| OID4VC issuance endpoint | Out-of-band issuance outside blueprints not in scope. |
| Cross-org trusted issuer registry | No centralized issuer reputation system yet. |
| QR code presentation flow | `PresentationEndpoints` generates `qrCodeUrl` but no QR rendering for in-person presentation. |
| Manual admin issuance | VCs are blueprint-only. |
| Credential holder consent preferences | Auto-accept rules (e.g., "always accept from Riverside Council") deferred. |

## Design

### 1. Issuance Flow (Issuer Experience)

**Trigger:** A blueprint action completes that has a `CredentialIssuanceConfig`.

**Backend flow:**
1. `CredentialIssuer` fires, creating an SD-JWT VC
2. Signed by the organization wallet (`did:sorcha:org:{address}`)
3. Acting participant's identity captured as a claim (not as signer)
4. Credential stored, holder notified via SignalR

**UI — Issuance Summary Panel:**

After the participant completes the action, an inline summary panel appears (modal or expansion within the action view):

| Field | Example |
|-------|---------|
| Credential Type | Building Permit |
| Issued To | Meridian Construction (`did:sorcha:org:...`) |
| Signed By | Riverside Council (org wallet) |
| Processed By | Jane Smith, Planning Officer |
| Claims Included | 6 total, 3 disclosable |
| Usage Policy | Reusable |
| Expires | 20 Mar 2027 |

**Behaviour:**
- Informational, not a blocking approval gate — the credential is already issued
- The blueprint *is* the approval process; this panel provides awareness
- "Done" button dismisses the panel and continues the workflow
- SignalR notification pushes to the holder with a pending credential

### 2. Holder Acceptance Flow

**Trigger:** A credential has been issued to a participant. They receive a SignalR notification.

**Notification:**
- Badge on "My Credentials" nav item (red dot)
- Toast notification: "New credential from Riverside Council"

**"My Credentials" page — Pending tab:**

The page uses tabs for lifecycle states: **Pending** (with badge count) | **Active** | **Expired** | **Revoked**

Pending credential card displays:
- Orange left border indicating attention needed
- Credential type, issuer, and originating blueprint
- Claims grid with disclosure indicators:
  - 🔒 Always disclosed (locked)
  - 🔓 Holder controls disclosure when presenting
- Metadata: issued date, expiry, usage policy, SD-JWT details
- **Accept Credential** / **Decline** action buttons

**Acceptance behaviour:**
- **Accept** → credential moves to Active tab, stored in wallet
- **Decline** → credential rejected, issuer notified, recorded in audit log
- Expired while pending → auto-moved to Expired tab, cannot be accepted
- No wallet linked → Accept button disabled with prompt to link wallet first (existing wallet-link challenge flow). Pending tab and credential review remain accessible.
- Multiple pending → sorted newest first, first card expanded, rest collapsed

### 3. Selective Disclosure Presentation

**Trigger:** A blueprint action requires the holder to present a credential (`CredentialRequirement`).

**Notification:**
- SignalR push: "Riverside Council requests your Building Permit"
- Accessible via notification or "My Credentials" → presentation requests

**Presentation picker UI (max-width 540px, compact layout):**

**Header:** Requesting party identity, blueprint context

**Matched credential:** Auto-matched from wallet by type + issuer. Shows credential card with "Matched" badge. If multiple credentials match, a dropdown selector lists matching credentials with issuer and date to disambiguate.

**Claims disclosure — three sections:**

1. **Required by verifier** — pre-selected, locked checkmarks. These are what the verifier needs and cannot be withheld.
2. **Optional — you choose** — toggle switches, defaulting to OFF (privacy-preserving). Holder opts in to sharing more. Shows "Sharing" (green) or "Withheld" (grey) state.
3. **Not requested** — greyed out, will not be shared regardless. Shown for transparency.

**Usage warnings:**
- SingleUse: "This credential will be consumed after presentation"
- LimitedUse: "4 of 5 presentations remaining after this"

**Summary bar:** "Sharing 3 of 4 claims" with **Present Credential** / **Deny Request** buttons.

**Behaviour:**
- Present → disclosed claims sent to verifier, presentation count incremented
- Deny → verifier notified that presentation was denied
- SingleUse credentials consumed after presentation (status → Consumed)

### 4. Verifier Trust View

**Trigger:** A verifier is completing a blueprint action that received a credential presentation.

**Display:** Inline within the blueprint action view (not a separate page).

**Contextual depth — three states:**

**Green (all checks pass):**
- Green banner: "Verified credential from Riverside Council"
- Disclosed claims shown as structured read-only data
- Collapsed accordion: "Verification details — 5 of 5 checks passed"
- Expandable to show: signature ✓, issuer trusted ✓, not revoked ✓, not expired ✓, required claims present ✓

**Amber (warning — e.g., FailOpen policy applied):**
- Amber banner: "Verification Warning — 1 check needs attention"
- Verification checklist auto-expanded
- Warning row highlighted (e.g., "Revocation check unavailable — FailOpen applied")
- Disclosed claims still shown below
- Action may proceed (soft gate) depending on blueprint configuration

**Red (failure — e.g., expired, revoked):**
- Red banner: "Verification Failed"
- Verification checklist auto-expanded with failure highlighted
- Action blocked — verifier cannot proceed until issue is resolved

**Verification checks displayed:**
1. Signature valid
2. Issuer trusted (with issuer name)
3. Revocation status (with policy if unavailable)
4. Not expired (with expiry date)
5. Required claims present (with count)

### 5. Issued Credentials Admin Page

**Location:** Administration → Issued Credentials (`/admin/credentials`)

**Purpose:** Org admins view and manage credentials their organization has issued. Scoped to the user's current organization (consistent with multi-org architecture — org context from JWT `org_id` claim).

**Data grid columns:**

| Column | Detail |
|--------|--------|
| Type | Credential type (e.g., Building Permit) |
| Issued To | Recipient org/participant name |
| Via Blueprint | Originating workflow |
| Issued Date | When issued |
| Expires | Expiry date (amber <30 days, red if expired) |
| Status | Active / Suspended / Revoked / Expired / Consumed |
| Presentations | Count of times presented |
| Actions | Suspend / Revoke / Reinstate / Refresh |

**Filtering:** By status, credential type, date range, recipient.

**Status transitions with confirmation dialogs:**

- **Suspend** → "This will temporarily invalidate the credential. Holders and active verifiers will be notified. You can reinstate later." Requires reason text.
- **Revoke** → "This will permanently invalidate the credential. This cannot be undone." Requires reason text. Red warning styling.
- **Reinstate** → Only from Suspended. "This will reactivate the credential."
- **Refresh** → Only from Expired. Issues a new credential with the same claims, consumes the old one. Uses existing `CredentialEndpoints.Refresh` which handles the reissue-and-consume cycle server-side.

**Detail view:** Click row to see full claim data, presentation history, audit log of status changes.

**Backend:** Uses existing `CredentialEndpoints` in Blueprint Service (revoke/suspend/reinstate/refresh operations).

### 6. Notifications & Status Propagation

Uses existing SignalR infrastructure. No new notification system required.

**Notification triggers:**

| Event | Recipients | Method |
|-------|-----------|--------|
| Credential issued | Holder | SignalR push + persistent badge on "My Credentials" |
| Credential accepted | Issuer admin | SignalR push (toast only) |
| Credential declined | Issuer admin | SignalR push + entry in issued credentials log |
| Presentation requested | Holder | SignalR push + persistent badge |
| Presentation submitted | Verifier (in active action) | SignalR push — updates action view inline |
| Presentation denied | Verifier (in active action) | SignalR push — shows denial in action view |
| Credential suspended | Holder + active verifiers | SignalR push — holder card goes amber, verifier view invalidated |
| Credential revoked | Holder + active verifiers | SignalR push — holder card goes red, verifier action blocked |
| Credential expired | Holder only | SignalR push (on expiry day) |

**"Active verifier" definition:** A verifier has an in-progress blueprint action with a `CredentialRequirement` referencing this specific credential. Once the action completes, they stop receiving updates.

**Badge behaviour:**
- Red dot on "My Credentials" nav item for unresolved items (pending acceptance, unviewed status changes)
- Badges clear when the user views the item

## Navigation Changes

Minimal changes to existing structure:

| Current | Change |
|---------|--------|
| My Activity → My Credentials | Enhanced: add Pending/Active/Expired/Revoked tabs, acceptance flow, presentation requests |
| Administration | Add: "Issued Credentials" page (`/admin/credentials`) |
| Blueprint action views | Enhanced: inline issuance summary panel, inline verifier trust view |
| System → Presentations | Unchanged (system-level presentation management remains) |

## Existing Backend Services Used

| Service | Usage |
|---------|-------|
| `CredentialIssuer` (Blueprint Engine) | SD-JWT VC issuance with selective disclosure |
| `CredentialVerifier` (Blueprint Engine) | Presentation validation against requirements |
| `CredentialStore` (Wallet Service) | Credential storage, status transitions, wallet queries |
| `PresentationRequestService` (Wallet Service) | OID4VP presentation request lifecycle |
| `CredentialEndpoints` (Blueprint Service) | Revoke/suspend/reinstate/refresh operations |
| `CredentialEndpoints` (Wallet Service) | Credential CRUD, matching, export |
| `W3cVcProvider` (Blueprint Schemas) | Schema validation for VC structures |
| SignalR Hub (Blueprint Service) | Real-time notification push |
| `SorchaDidIdentifier` / `SorchaDidResolver` | DID resolution for issuer/holder identity |

## New UI Components Required

| Component | Location | Purpose |
|-----------|----------|---------|
| `IssuanceSummaryPanel.razor` | UI.Core/Components/Credentials | Post-action issuance awareness panel |
| `CredentialCard.razor` | UI.Core/Components/Credentials | Material-style credential display card (may extend existing) |
| `CredentialAcceptDialog.razor` | UI.Core/Components/Credentials | Accept/decline pending credential |
| `DisclosurePicker.razor` | UI.Core/Components/Credentials | Selective disclosure toggle UI for presentations |
| `VerificationTrustView.razor` | UI.Core/Components/Credentials | Contextual verification display (green/amber/red) |
| `IssuedCredentialsGrid.razor` | UI.Core/Components/Admin | Admin data grid for issued credentials |
| `CredentialStatusDialog.razor` | UI.Core/Components/Admin | Suspend/revoke/reinstate confirmation dialog |

## New Pages Required

| Page | Route | Access |
|------|-------|--------|
| My Credentials (enhanced) | `/my-credentials` | All authenticated users |
| Issued Credentials | `/admin/credentials` | Administrator, SystemAdmin |
| Credential Detail | `/admin/credentials/{credentialId}` | Administrator, SystemAdmin |

## Models / ViewModels Required

| Model | Purpose |
|-------|---------|
| `CredentialCardViewModel` | Display model for credential cards (type, issuer, status, claims, disclosure info) |
| `PendingCredentialViewModel` | Extends card with accept/decline actions, originating blueprint |
| `PresentationRequestViewModel` | Verifier request details, matched credential, claim sections |
| `DisclosureClaimViewModel` | Individual claim with disclosure state (Required/Optional/NotRequested) and toggle |
| `VerificationResultViewModel` | Trust view model with check results, escalation level (green/amber/red) |
| `IssuedCredentialListItem` | Admin grid row (type, recipient, status, presentation count, actions) |
| `CredentialStatusChangeRequest` | Suspend/revoke/reinstate with reason text |

## Testing Strategy

- **Unit tests:** ViewModels, disclosure logic (which claims required/optional/excluded), verification escalation rules
- **Component tests:** Each new Blazor component with mock data for all states (pending, active, expired, revoked, amber warning, red failure)
- **Integration tests:** Issuance → acceptance → presentation → verification end-to-end via service clients
- **E2E (Playwright):** Multi-org Construction Permit walkthrough extended to cover VC acceptance and presentation UI
