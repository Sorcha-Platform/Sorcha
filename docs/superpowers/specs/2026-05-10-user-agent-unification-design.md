# User-Agent Unification — Design Note (Draft)

**Status:** Exploratory. Captures directional alignment from the 2026-05-10 brainstorm. Not yet a spec; a future design conversation should expand it before any plan-phase work begins.

**Audience:** The next Claude session (or human) picking up the cross-app UI design work.

## Context — what we have today

Two user-facing applications:

- **Sorcha.UI** (Blazor WASM at `/app`) — carries three audiences in one bundle: tenant admins, blueprint authors, and end-user workflow participants. Org-custodial wallets via the Wallet Service.
- **Sorcha.Citizen.Wallet** (Blazor WASM PWA at `/wallet`) — single citizen, single device, credential hold + present only. Self-custody via per-citizen holder key (slot 108).

Already correctly shared between them: `PlatformUser` identity, Tenant Service auth, `IPlatformUserDeviceClient` bridge, `WalletHub` group convention, `IIssuerKeyResolver` trust path.

## Direction agreed in the brainstorm

1. **The PWA grows into the full end-user agent.** Not just credentials — also workflow action submission, data entry, photo upload, pending-credential acceptance, persona management, device + auth settings, receipts/history. Mobile data collection is a first-class use case.
2. **Sorcha.UI retains the admin/designer surface.** Tenant configuration, blueprint authoring, participant registry, multi-org operations stay in the web app. Org/corporate users use web or internal systems as today.
3. **Custody is an axis, not a fork.** Three modes in v1, one deferred to v2:
   - **Self-custody / local** *(v1)* — keys on device, user owns recovery (BIP39 phrase). Today's Citizen Wallet model.
   - **Managed with recovery** *(v1, default)* — Wallet Service holds keys, app is a remote control, recovery = "log into your account on a new device." Lowest friction, matches consumer-app expectations.
   - **Web-only** *(v1)* — same managed mode, accessed via the main web app without installing the PWA.
   - **Co-signed dual-key (2-of-2 multisig)** *(v2, backlog)* — collector + employing-org dual signature, targeted at field data collection. Significant design effort in its own right (policy engine + offline outbox + multi-sig crypto + validator-side aggregation). Details captured in the "Co-signed data collection (v2)" section below for backlog recall.
4. **Two delivery shells, role-driven not user-class-driven.** The PWA and the web shell aren't "citizen vs. corporate" — they're **field/mobile vs. desk/desktop**, and the same person can use both depending on what they're doing right now. See "Field vs. desk" framing below.
5. **Shared components first.** Before deciding any of the harder questions, extract `Sorcha.UI.Components.User` (working title) as a Razor class library carrying the user-facing flows. Both shells consume it.

## First concrete move

Extract `Sorcha.UI.Components.User` (or similar name — bikeshed at design time). Initial contents:

- Credential id-card renderer (already informally shared via `ReviewSummaryRenderer` / `IdCardLayout` patterns)
- Consent sheet
- Action-submission form host (around `SorchaFormRenderer`)
- Persona panel (Feature 092 surface)
- Device list / auth-methods management (Feature 116)
- Presentation picker / credential picker dialog
- File / photo upload component (Feature 085 + Feature 107 `x-file.capture`/`embedAs`)

Both `Sorcha.UI.Web.Client` and `Sorcha.Citizen.Wallet` reference the library. The custody question becomes "what `IUserSigner` implementation gets injected" rather than "do we have to rewrite the UI."

## What needs to migrate into the PWA (from Sorcha.UI's participant surface)

- `SorchaFormRenderer` and its supporting validators / autofill (Feature 092 persona resolver)
- File chunked upload (Feature 085) with mobile-camera capture (Feature 107)
- Action submission (today routed through service clients with server-side signing)
- Pending credential acceptance UX (the `PendingAcceptance` accept-before-use pattern)
- Transaction receipts + verification bundle viewer (Feature 079)
- Persona / profile management (Feature 092 — already has `MyProfile.razor`)
- Device + auth settings (Feature 116 — passkeys, social links, password lifecycle)

Out of scope for the PWA: blueprint designer, tenant configuration, participant registry, register administration. These remain in Sorcha.UI.

## Mobile-only capabilities the PWA can leverage that the web client cannot

True PWA differentiators — not just better UX, but capability gaps:

- **Background sync + push notifications** via service worker. Receive credential-available pushes when the app is closed (already wired for Feature 114 US4 via `CitizenWalletHubConnection`).
- **NFC reads** (Web NFC, Chromium Android only). For eMRTD passport reads, smartcard credentials, contactless evidence. Desktop web has zero capability here.
- **Device motion / orientation sensors.** Liveness challenges in identity verification (head-turn / blink prompts). Meaningless on desktop.
- **Native camera capture** — full-screen capture, retry flow, OS-mediated. Web can do `getUserMedia` but the UX is materially worse on phones.
- **QR scanning** for OID4VP cross-device presentation flows (Feature 114 T057 parked work).
- **Share Target API.** PWA receives shared files / credentials from other apps via the OS share sheet — a wallet-to-wallet credential import path that doesn't exist on the web.
- **Geolocation with GPS accuracy** — for field data collection where device GPS matters versus IP geolocation.
- **Offline data collection.** Fill a form in a remote area, sync when reconnected. Architecturally a PWA strength; the web client isn't built for it.
- **OS-level biometric prompts** via WebAuthn platform authenticator — tighter integration when installed than in a tab.
- **Scheduled local notifications** for expiring credentials, follow-up tasks. Web tab can only notify when open.

## Field vs. desk framing

The PWA's unique capability set — NFC, Share Target, motion sensors, native camera, offline-first, GPS, OS biometrics, scheduled notifications — is precisely the **evidence-capture / data-collection / in-pocket** surface. Which means the PWA isn't just "the consumer app" and the web isn't just "the corporate app." The split is closer to:

- **Web shell** — desk-bound work: blueprint authoring, tenant administration, bulk review, large-table inspection, side-by-side comparison, typing-heavy form filling, drag-and-drop bulk upload. Same person opens this when sitting at a workstation.
- **PWA shell** — mobile / field / in-pocket work: data collection in the field, photo and document capture, NFC reads, QR scanning, offline form filling, presenting credentials in person, receiving pushes when the app is closed. Same person opens this when on the move.

A district nurse doing home visits, a building inspector on site, a supply-chain checkpoint scanner, a citizen presenting an identity credential at a counter — these are different roles but they share a delivery surface (the PWA) because they share a *situation* (physically somewhere, capturing or presenting evidence). The same district nurse uses the web shell at the clinic for case review and the PWA at the home visit for collection. Same identity, same backend, same components — different shell because different ergonomics.

This reframes "who is the PWA for" from a *user-class* answer ("citizens / consumers") to a *role-and-situation* answer ("anyone capturing or presenting evidence on the move"). Worth confirming before deeper investment because it changes the v1 feature set — field data collection (forms + photos + GPS + offline) moves up in priority and the "credential hold + present" framing of Feature 114 becomes one of several first-class use cases rather than the headline.

## Co-signed data collection (v2 — backlog)

**Scope status:** Not in v1. Deferred to a follow-up phase once the user-agent unification + managed/self-custody seam is in flight. Captured here in full so the backlog entry can recall the details.

Selected scenarios — regulated evidence collection, two-person integrity flows, organisationally-bonded fieldwork — benefit from a custody model where **neither the collector alone nor the org alone can submit on the collector's behalf**. Both signatures required.

**Shape.**

- Key A — collector's on-device PWA key (self-custody, slot under e.g. `sorcha:field-collector`).
- Key B — org-held key (Wallet Service custodial, derived under the employing org's hierarchy per Feature 083).
- Submission flow: PWA signs locally with Key A → forwards partial-signed transaction to Wallet Service → Wallet Service applies Key B subject to org policy → fully-signed transaction submitted to Validator.

**Why this matters for field collection.**

- **Non-repudiation in both directions.** Collector can't fabricate submissions alone (org policy gate). Org can't forge submissions in the collector's name (no Key A).
- **Compliance fit.** Maps cleanly to two-person-integrity requirements in regulated domains (medical, legal/forensic, financial audit, custody-of-evidence chains).
- **Role accountability without sole control.** The org's co-sign is a *policy decision point* — auto-approve within scope, manual review for anomalies, decline if out-of-policy. Becomes a place to enforce time windows, geofences, action-type allow-lists, or supervisor escalation, without rebuilding the workflow engine.
- **Recovery.** Device-lost ceremony rebinds Key A via the existing device delegation pattern (Feature 114 model) without losing the historical chain — past submissions stay verifiable under the old Key A.

**Where it slots into existing Sorcha primitives.**

- `CustodyMode.CoSigned` (Feature 083) — schema-level placeholder already declared. This is its implementation.
- Validator `RequiredSignatures` ≥ 2 (Feature 086) — N-of-M signature aggregation precedent exists, just applied to the docket-signing roster rather than to participant wallets. The validation primitive can be generalised.
- `IAuthChallengeService` (Feature 116) — step-up auth on the org side when policy escalates to manual review.
- Transaction receipts (Feature 079) — each signature already timestamped + receipted; co-signed transactions naturally carry both signers' attestations.

**What needs designing (deferred to plan-phase).**

- **Cryptographic primitive.** Naive multi-signature (two ED25519s attached to the transaction, validator verifies both) vs. threshold signatures (FROST / MuSig2, true single signature requiring both parties). Naive wins on simplicity for v1; threshold could come later if signature size matters.
- **Wallet-address derivation.** A 2-of-2 wallet needs a stable address derived from both pubkeys. Probably deterministic hash; needs to play with the existing `ws1...` address format.
- **Org policy engine.** What decides auto-approve vs. manual review vs. decline? Probably starts as org-level config (roster + action allow-list + time window) and grows toward per-action policy as the need lands.
- **Offline collection.** The collector may be offline (no signal in the field). Partial-signed transactions queue on-device in IndexedDB outbox, sync when reconnected, org co-signs asynchronously, submission completes when both signatures present. Already aligned with PWA architecture but needs explicit design.
- **UX.** Worker submits → "awaiting org co-sign" state → submitted. Auto-approve invisible; manual review surfaces as pending. Component library design must accommodate the pending state without making auto-approve flows feel slow.

## What the web client still does better — for the same end user

- Long-table review (transaction history, large registers)
- Typing-heavy form filling with a real keyboard
- Drag-and-drop bulk file upload
- Side-by-side multi-window comparison

The web shell isn't going away for end users; it remains the right surface for the heavy-input / heavy-review tail of the user's needs.

## Open questions for v1 (need decisions before any plan-phase)

1. **Managed-mode default confirmation.** Managed-with-recovery is the proposed v1 default; self-custody is the opt-in for power users / regulated holders. Confirm before locking — this is a directional shift from today's Citizen Wallet which is self-custody-only.
2. **Managed-mode signing UX.** Self-custody signs on-device against a device key. Managed-mode needs the user to *consent* but the signature happens server-side — likely a step-up auth challenge (Feature 116 `IAuthChallengeService`) wrapped in the same consent-sheet UX. Component library design must accommodate both paths through a single seam.
3. **Recovery flows per mode.** Self-custody: BIP39. Managed: log-in-elsewhere. Hybrid combinations (managed primary + self-custody backup): out of scope for v1.
4. **Naming.** "Citizen Wallet" is now misleading — under the field-vs-desk framing it's the in-pocket Sorcha agent for anyone, not just citizens. Worth renaming before deeper investment, or leave the URL and component names alone and just shift positioning?
5. **PWA action-submission threat model per custody mode.** Self-custody: sign locally then submit. Managed: HTTP + step-up. Two failure-mode profiles; component library must abstract them behind a single `IUserSigner`-like seam without leaking the differences into the consuming UI.

## Open questions for v2 (co-signed scope — picked up when backlog item lands)

1. **Cryptographic primitive.** Naive two-ED25519-attached vs. threshold (FROST / MuSig2). Naive is the v2 candidate — simpler, validator-side check is a small extension to existing signature verification, no interactive signing protocol needed.
2. **Policy engine.** Org-level (roster + allow-list + time window) first; per-action policy later. Who authors (tenant admin? blueprint author?) and where it lives (Tenant Service config? blueprint extension?).
3. **Offline co-signed collection.** Partial-signed transaction outbox in IndexedDB → sync on reconnect → org async co-sign → submission completes. Retry policy, conflict resolution, stale-policy handling.
4. **Recovery for co-signed wallets.** Rebind Key A via device delegation while preserving past-Key-A signature verifiability.

## Related context

- Feature 114 (Citizen Wallet PWA) — current PWA scope is credentials only; this design expands it.
- Feature 116 (Account Linking & Auth-Method Management) — provides `IAuthChallengeService` which the managed-mode signing UX would consume for step-up.
- Feature 120 (Production Issuer Signature Verification) — DID resolver + cross-resolution; orthogonal but in the same neighbourhood of trust plumbing.
- `sorcha-architecture` skill carries the consolidated server-side surface for all three.

## Not in scope

- Merging Sorcha.UI and the PWA into a single app (option C in the brainstorm — explicitly rejected; the form-factor and threat-model differences are real).
- Splitting admin out of Sorcha.UI into its own app (option B — interesting but a separate, larger initiative; revisit after the user-agent work is in flight).
- Reworking the org wallet custody model. Org-custodial wallets via the Wallet Service stay as they are.
