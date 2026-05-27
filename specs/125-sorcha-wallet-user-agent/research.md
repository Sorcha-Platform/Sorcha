# Phase 0 Research — Sorcha Wallet (Full User-Agent v1)

**Feature**: 125-sorcha-wallet-user-agent
**Date**: 2026-05-14
**Status**: Complete — no NEEDS CLARIFICATION items remain.

This document resolves the open questions Phase 0 surfaces. Most decisions were settled during the 2026-05-14 brainstorm (captured in `docs/superpowers/specs/2026-05-14-spec-2-sorcha-wallet-user-agent-design.md`) and the upstream 2026-05-10 user-agent-unification design. The items below are the ones that genuinely need a "here's what we chose, here's why, here's what we considered" treatment for plan-phase.

## R-001 — Custody mode default for v1

**Decision**: Managed mode is the v1 default; self-custody opt-in deferred to v2.

**Rationale**:
- Today's Citizen Wallet is already managed-mode-shaped — the holder key derives server-side under `sorcha:citizen-holder` slot 108, the device key is browser-local, and a delegation credential binds them. Sarah never sees a BIP39 phrase.
- The umbrella's locked Decision #2 ("Email/password is the durable anchor … no mnemonic for Sarah") aligns directly with managed mode.
- Self-custody adds an enrolment-time mode-choice question, two recovery flows, two signing UX paths through ConsentSheet — a meaningful surface complication. Deferring isolates the v1 scope.

**Alternatives considered**:
- *Ship both modes in v1*: rejected; doubles the visible UX surface and the test matrix for marginal gain (no real user has demanded self-custody yet).
- *Defer self-custody indefinitely*: rejected; regulated holders / power users genuinely benefit. v2 is the right home.

## R-002 — `IUserSigner` abstraction location

**Decision**: `IUserSigner` lives in `Sorcha.Wallet.Pwa.Services` (PWA-side abstraction). The shared `Sorcha.UI.Components.User` library depends on the interface but resolves it via DI from the consuming shell.

**Rationale**:
- The signing operation is shell-specific: the PWA has device keys + WebCrypto, the desk web shell signs server-side via Wallet Service HTTP. They share the contract but not the implementation.
- Placing the interface in the PWA project means the desk shell would have to depend on the PWA project — wrong direction. Placing it in `Sorcha.UI.Components.User` keeps dependencies upward-flowing from both shells.

**Refinement (decided in plan-phase)**: The interface lives in `Sorcha.UI.Components.User.Services.Signing.IUserSigner`. The PWA registers `ManagedUserSigner` (today's managed-mode impl); a future web-shell implementation registers `WebShellUserSigner`. Both flow through the same library `ConsentSheet` and `PresentationSubmitDialog` without leaking custody-mode awareness.

**Alternatives considered**:
- *Define `IUserSigner` in `Sorcha.Wallet.Pwa`*: rejected (wrong dependency direction).
- *Define a separate `IUserSigner` per shell*: rejected (defeats the purpose — the whole point is one abstraction).

## R-003 — Multi-context content scoping enforcement

**Decision**: Server-side enforcement via existing JWT audience + OrgMembership claim. Client-side display filtering is an optimisation, not a security boundary.

**Rationale**:
- Today's Tenant Service issues a JWT with `org_id` claim representing the active organisation. Wallet Service endpoints check this claim. Switching context in the PWA means re-acquiring a JWT with the new `org_id` (via `/auth/switch-org`).
- The wallet must NOT trust client-side context filtering for credential visibility or signing operations — a malicious PWA could otherwise reveal cross-context credentials.

**Implementation**:
- Context switch in the PWA fires `/auth/switch-org`, gets a new JWT, all subsequent API calls use the new JWT.
- `ICitizenWalletClient` + related service clients pick up the new bearer token from `IAccessTokenStore`.
- Client-side filtering is a presentation-layer concern, not a trust boundary.

**Alternatives considered**:
- *Single multi-context JWT*: rejected; security boundary blurs and JWT size grows unboundedly with org count.
- *Client-side filtering only*: rejected; not a real security boundary.

## R-004 — Per-context persona storage

**Decision**: Extend the existing `PlatformUserPersona` table (Tenant Service, Feature 092) with an optional `ContextOrgId` column. Each unique `(PlatformUserId, ContextOrgId)` pair gets its own persona row. Null `ContextOrgId` means the "Personal" (cross-context) persona.

**Rationale**:
- Today's persona is single-shape per user. Adding a context column is a minimal additive migration that handles the multi-persona case while keeping the single-persona case unchanged (null `ContextOrgId`).
- The Feature 092 encryption envelope stays the same — content key per row, derived under `sorcha:persona-vault`. The new column is just a row discriminator.

**Implementation**:
- `PersonaEndpoints` gain an optional `?context=<orgId>` query parameter on GET/PUT/DELETE.
- The pre-release migration-squash rule applies: this column gets folded into the InitialCreate migration rather than added as a separate migration step.

**Alternatives considered**:
- *Separate `PlatformUserContextPersona` table*: rejected; doubles the encryption envelope code without semantic benefit.
- *JSON column with per-context personas inside a single row*: rejected; loses encryption-per-row property and complicates row-level access patterns.

## R-005 — Verification history storage

**Decision**: Verification history is stored **client-side only** in IndexedDB on the PWA. No server-side persistence in v1.

**Rationale**:
- Verifications performed by the citizen are private records ("I verified Liam Buchanan today"). They're not transactions on a Sorcha register — they're notes the citizen keeps for themselves.
- Server-side persistence would require: new tables, encryption decisions, sync logic, recovery story when device is lost. Significant scope without v1 user-visible benefit.
- If a user clears their browser storage, their verification history is lost. Acceptable v1 behaviour — same trade-off the welcome-takeover flag carries (per-device, not synced).

**Implementation**:
- New IndexedDB store `verifications` in the wallet's existing database.
- Entries: `{ id, verifiedAt, holderDisplayName, issuerOrgName, credentialType, outcome, fullTrustPanel }`.
- Accessed via `IVerificationHistoryStore` (interface + InMemory + IndexedDB impls following the existing pattern from F114 / F124).

**Alternatives considered**:
- *Server-side persistence in Wallet Service*: rejected for v1 (scope, encryption complexity). Future spec can revisit.
- *Cross-device sync of verification history*: rejected for v1; not a v1 requirement, would need server-side first.

## R-006 — Ephemeral verifier identity generation

**Decision**: Per-verification-session EC P-256 key generated client-side via WebCrypto. Used to construct the OID4VP `client_id` for audience-binding. Discarded after the verification completes.

**Rationale**:
- OID4VP requires a verifier `client_id` for the presentation request's audience binding.
- The citizen verifier isn't a registered platform consumer — no centralised identity makes sense at the protocol level.
- WebCrypto is already the device-key path; reusing it for the ephemeral verifier key keeps the bridge simple.

**Implementation**:
- `IEphemeralVerifierIdentityService` (in `Sorcha.UI.Components.User.Services.Signing` alongside `IUserSigner`): generate a fresh EC P-256 key, derive the public-JWK thumbprint, expose as `client_id` for the duration of a verification session, dispose afterwards.
- No server-side registration. No persistence beyond the session.

**Alternatives considered**:
- *Use the citizen's holder key as the verifier identity*: rejected; conflates roles, and the holder key shouldn't be exposed as a verifier audience.
- *Use a single per-wallet-install ephemeral identity (longer-lived)*: rejected; weaker isolation between verifications without meaningful benefit.

## R-007 — QR / NFC scanning in the wallet

**Decision**: QR scanning via the device camera (Web Camera API + a small JS QR-detection library); NFC via the Web NFC API on Chromium Android only (graceful degradation elsewhere).

**Rationale**:
- The Web Camera API is universally available in modern mobile browsers; QR scanning libraries are mature and small.
- NFC is Chromium-Android-only today. The wallet can offer it where available; the QR path is the universal fallback.
- No native code, no app-store dependencies — stays PWA-shaped.

**Implementation**:
- `IQrScannerService` (PWA-side, bridges to a JS QR library — likely `jsQR` or `qr-scanner` via npm-equivalent bundling).
- `INfcReaderService` (PWA-side, wraps the Web NFC API; reports unavailability gracefully).
- `VerifyFlow.razor` orchestrates: try NFC first if available, fall back to QR, surface both options in UI.

**Alternatives considered**:
- *NFC-only*: rejected; cuts out iOS and non-Chromium browsers entirely.
- *Server-side QR decode via image upload*: rejected; latency, photo-quality variation, and removes the offline-capable property.

## R-008 — Guided-tour persistence

**Decision**: Per-device IndexedDB flag, same pattern as F124's `WalletFlagsRecord.WelcomedAt`.

**Rationale**:
- Tour completion is a per-device concern — a citizen on a new device benefits from the tour, even if they've seen it before on another device.
- Reuses the F124 pattern; consistent surface.

**Implementation**:
- Extend `WalletFlagsRecord` with `TourDismissedAt: DateTimeOffset?`.
- `IGuidedTourStore` interface; in-memory + IndexedDB impls.

**Alternatives considered**:
- *Server-side tour-completion tracking*: rejected; per-device is the right scope, server roundtrip adds latency to first-paint.
- *Single boolean in MainLayout state*: rejected; doesn't persist across page reloads / app restarts.

## R-009 — Form factor adaptation mechanism

**Decision**: Components in `Sorcha.UI.Components.User` accept explicit `Variant` or `Layout` parameters; defaults inferred from the existing `MediaQueryService.IsMobile` MudBlazor helper.

**Rationale**:
- MudBlazor already provides responsive helpers. No new mechanism needed.
- Explicit parameters let callers override when context dictates (e.g., a tablet-kiosk verifier may want sheet-style dialogs even though the form factor is large).

**Implementation**:
- `CredentialCardList` adds `Layout="Layout.List \| Layout.Grid"`, defaulting to `Layout.List` on mobile and `Layout.Grid` on desktop.
- `PresentationRequestDialog` adds `Variant="DialogVariant.Sheet \| DialogVariant.Modal"`, defaulting similarly.
- `TransactionDetailDrawer` adds `Variant` parameter.

**Alternatives considered**:
- *Per-shell forks of each component*: rejected explicitly — this is the bug class PR #698 was about, and the design doc §10 carves it out.
- *CSS-media-query-only adaptation*: rejected; some adaptations are structural (sheet vs. modal), not just stylistic.

## R-010 — Reading-age measurement tool

**Decision**: Flesch-Kincaid Grade Level via the `Microsoft.Recognizers.Text` library or an equivalent .NET-friendly readability tool. Target: average ≤ 8.0 (US Grade 8, roughly UK Year 9, just above the SC-010 ≤ Year 8 target — leaving margin for the difference in scales).

**Rationale**:
- Flesch-Kincaid is a recognised baseline; tools are mature; integrates into a build-time check or a one-shot audit step.
- The SC-010 bar is "≤ Year 8 average" (UK reading age scale). Flesch-Kincaid uses US grade levels; UK Year 8 ≈ US Grade 7. Targeting average ≤ 8.0 on the F-K scale leaves a small buffer.

**Implementation**:
- Plan-phase: a one-shot CLI audit run during PR-F that walks all `.razor` files, extracts user-visible string literals, runs them through Flesch-Kincaid, reports the average and outliers.
- Per-PR enforcement: deferred; one-shot audit suffices for v1.

**Alternatives considered**:
- *Build-time gate via Roslyn analyzer*: rejected; complexity for marginal benefit. One-shot audit catches drift.
- *Manual reading-age review*: rejected; non-deterministic, doesn't scale to future changes.

## R-011 — Rename mechanics — single PR vs. distributed

**Decision**: Rename happens in PR-A (first PR of the implementation plan). Single atomic PR that renames projects, namespaces, container images, compose services, test projects, all `using` directives, and user-visible app name.

**Rationale**:
- Half-renamed state is the worst state — code split across "Sorcha.Citizen.Wallet" and "Sorcha.Wallet.Pwa" namespaces creates merge friction and confusion.
- Atomic rename via `git mv` + targeted `sed` (with explicit verification per PR #688 / `sed_rename_footgun` memory) gives one clean diff.
- PR-A's CI run validates the rename end-to-end before any new functionality lands on top.

**Implementation**:
- PowerShell script under `scripts/rename-wallet-projects.ps1` automates the bulk find-replace + project renames.
- Validation step: after rename, all existing F124 tests pass on master (SC-006).
- User-visible URL stays `/wallet/`; only the internal name changes.

**Alternatives considered**:
- *Distribute the rename across multiple PRs*: rejected; creates a long unstable interval.
- *Defer the rename to a later PR*: rejected; the spec's role-neutral copy bar (FR-001) requires the rename early so subsequent PRs can use the new name in copy.

## All NEEDS CLARIFICATION resolved

Zero `NEEDS CLARIFICATION` markers in `plan.md`'s Technical Context. The brainstorm + 2026-05-10 design + Spec 1 precedent answered all the load-bearing questions.

## Open items for Phase 1 design

Carried forward, not litigated here:

- Exact OpenAPI shape for the per-context persona endpoint extension. Resolved in `contracts/per-context-persona.openapi.yaml`.
- Exact shape of `VerificationRecord` entries in IndexedDB. Resolved in `data-model.md`.
- Exact shape of `GuidedTourScaffold` step-data contract. Resolved in `data-model.md`.
- Demo runbook ordering for the three beats. Resolved in `quickstart.md`.
