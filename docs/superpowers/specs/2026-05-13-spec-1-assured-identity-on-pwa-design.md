# Spec 1 — AssuredIdentity on the PWA

**Date:** 2026-05-13
**Status:** Design locked. Implementation plan pending.
**Umbrella:** [`2026-05-13-strathcarron-citizen-arc.md`](2026-05-13-strathcarron-citizen-arc.md)
**Scope tier:** B (Minimum swap + arrival moment) — chosen over A (austere) and C (foundations-bleed) during brainstorm.

## Purpose

Replace the HAIP filesystem wallet target in the existing AssuredIdentity walkthrough (Feature 107) with a real `SorchaLocalWallet` (Citizen Wallet PWA, Feature 114) target, and add the one piece of net-new UX that turns a working architecture into a demo that lands: the **first-credential takeover** moment.

This is the smallest piece of net-new design in the citizen arc. It proves the whole stack on a real citizen-facing surface and produces the credential every later spec gates on.

## What Spec 1 ships

A demoable end-to-end flow:

1. Sarah signs in to Sorcha.UI.Web (existing, untouched).
2. Sarah opens the Citizen Wallet PWA at `/wallet/` and runs the existing `/enrol` wizard. The "Done" step's copy now reads helpfully when zero credentials loaded (Q6 B).
3. Sarah opens the AssuredIdentity application on Sorcha.UI.Web, completes the 5-page wizard (existing, untouched).
4. The PWA's Home page enters a **waiting state** — copy and a pulsing skeleton card indicate her application is in review (Q4 C).
5. The verification analyst (AI agent via `run-agents.ps1`) approves.
6. The PWA receives the `CredentialAvailable` SignalR push and fires the **first-credential takeover** — a full-screen ceremony with the id-card, "Welcome to your wallet" copy, and a single Open action (Q2 D, exactly once per wallet, persisted).
7. Sarah dismisses the takeover; the id-card settles into Home as her first credential.
8. The walkthrough script's HAIP filesystem wallet target is gone (Q3d).

## Locked decisions

| # | Question | Answer | Rationale |
|---|----------|--------|-----------|
| 1 | Scope tier | **B — Minimum swap + arrival moment** | A is too austere for a demo; C smudges into Spec 2's foundations territory. |
| 2 | Arrival style (foreground or first-cold-open) | **D — First-credential takeover** (once per wallet, persisted) | Highest demo punch; explicitly welcoming; cleanly distinct from steady-state. |
| 2b | Steady-state arrival for credentials 2..N | **C — Card slides in with glow** | Calm but unmistakable. Out of Spec 1's *demo* path (Sarah only receives one credential here) but design-locked so Spec 4's second-credential arrival has nothing left to debate. |
| 3a | Enrolment timing relative to application wizard | **Sequential** — enrol first, then submit application | Spec 3 owns the enrol-during-wizard seam; Spec 1 stays clean. |
| 3b | Where Sarah fills out the application form | **Sorcha.UI.Web** (status quo) | Co-equal surfaces; web is a legitimate form host. PWA-embedded form is its own design conversation in Spec 5. |
| 3c | Demo running mode | **Sarah live, analyst scripted** | Existing `run-agents.ps1` pattern. Audience sees real wallet UX; takeover timing predictable. |
| 3d | HAIP filesystem wallet mode | **Delete** | `run-multi-peer.ps1` tests register-native delivery and doesn't depend on it. Dual-mode would just confuse the walkthrough surface. |
| 4 | Wallet state between submit and arrival | **C — Copy change + pulsing skeleton card** | Cheap, anchors the takeover as the completion of a visible in-progress state. Walkthrough-script-driven flag for the demo; production-grade "list pending applications" is Spec 2. |
| 5 | Cold-open behaviour (Sarah missed the live push) | **A — Takeover fires on first open after issuance**, persisted | Takeover *is* the welcome; missing it would be a regression. One IndexedDB key (`welcomedAt`) guards single-fire. |
| 6 | Enrol wizard "Done" copy | **B — Conditional**: zero credentials loaded → "Enrolled. Your wallet is ready — submit your council application to receive your first credential." Non-zero → existing copy. | Tiny conditional; big clarity win for the Spec 1 demo flow. |

## Affected surfaces

### Wallet PWA (`src/Apps/Sorcha.Citizen.Wallet/`)

- **`Pages/Index.razor`** — three changes:
  - Waiting-state branch: when a `PendingApplicationFlag` is set, replace the existing empty-state alert with the new copy and render a pulsing skeleton card (CSS animation, no MudBlazor dependency).
  - First-credential takeover: when the credential list transitions from 0 → 1 *and* `welcomedAt` is unset, render the takeover overlay (full-screen, id-card, "Welcome to your wallet", Open button); persist `welcomedAt` on dismiss.
  - Cold-open path: on `OnInitializedAsync`, if credentials exist and `welcomedAt` is unset, render the takeover before the user sees Home.
- **`Pages/Enrol.razor`** — Done-step copy conditional on `EnrolmentResult.CredentialsLoaded == 0`.
- **New IndexedDB store** for the takeover flag and the pending-application flag. Single small entity (`WalletFlags { welcomedAt: DateTime?, pendingApplicationLabel: string? }`).
- **Reuse `ReviewSummaryRenderer` + `IdCardLayout`** for the takeover's id-card (umbrella invariant — one component, three contexts, state-driven watermark = `Issued`).

### AssuredIdentity walkthrough (`walkthroughs/AssuredIdentity/`)

- **Delete** the `wallet/` filesystem-wallet directory and all references in setup/run scripts.
- **`blueprints/assured-identity.json`** — confirm `credentialIssuanceConfig.targetAudience` is `"SorchaLocalWallet"` (already the case for new credentials; verify and document).
- **`setup.ps1`** — pre-creates Sarah's platform account, signs her in to the PWA host once so the demo can start from "she taps the wallet icon."
- **`run-phase1-identity.ps1`** — adapted to set the wallet's pending-application flag (one HTTP call to a small new wallet endpoint, see below) immediately after Sarah submits Action 1, and clear it on Action 3 completion. Existing flow otherwise unchanged.
- **`actors/verification-analyst.json`** — unchanged; the AI agent already plays this role.
- **`README.md`** — updated to describe the PWA-target demo as the default; reference Spec 1.

### Wallet Service (`src/Services/Sorcha.Wallet.Service/`)

- **One new endpoint** (citizen JWT, scoped to the authenticated PlatformUser): `POST /api/v1/wallet/pending-applications` (set/clear a label-only flag), `GET /api/v1/wallet/pending-applications` (read). Pure metadata, no credential content. This is the seam between the walkthrough-script and the PWA's waiting-state UI.
- **No changes to the issuance, sync, or push paths.** The takeover fires off the existing `CredentialAvailable` SignalR event.

### Sorcha.UI.Web

Untouched. The application form already exists; Spec 3 will design the embedded-enrol seam later.

## Out of scope (deliberate)

Pulled out of Spec 1 to keep it focused; each lives in a downstream spec:

- **PWA install prompt UX.** Browser default behaviour for the demo. Spec 2 owns the polished install-first experience.
- **Empty-Home onboarding before any enrolment.** Spec 2 owns the first-launch flow.
- **`/enrol` wizard redesign.** Existing MudStepper is fit for purpose; cosmetic improvements deferred to Spec 2.
- **PWA-hosted application form.** Spec 5 (or later) owns the question of whether the wallet hosts forms or only receives credentials.
- **Enrol-inside-wizard seam UX.** Spec 3 owns this entirely.
- **Production-grade pending-applications listing.** Spec 1 uses a single walkthrough-driven flag. Spec 2 (or a small follow-up) can replace it with a real Wallet-Service-backed listing once we have a second credential to track.
- **Steady-state arrival for credentials 2..N in production use.** Locked here (C) so Spec 4's second-credential demo has nothing to debate, but not implemented in Spec 1.

## Success criteria

Spec 1 has succeeded when:

1. A presenter can run `walkthroughs/AssuredIdentity/setup.ps1` followed by `run-phase1-identity.ps1 -UseAgents` (or equivalent), watch Sarah's PWA on a phone screen, and see: empty wallet → enrol → pulsing-skeleton waiting state → first-credential takeover → settled Home with the id-card. End-to-end under 60 seconds.
2. The takeover fires exactly once per wallet — confirmed by closing and reopening the PWA after dismissing; the id-card sits quietly on Home, no second takeover.
3. The cold-open path is verified — kill the wallet between submit and analyst approval; reopen after approval; takeover fires on cold open.
4. The HAIP filesystem wallet directory and its references are gone; `run-multi-peer.ps1` still passes (independent path).
5. Enrol wizard "Done" copy reads correctly for both the Spec 1 demo path (0 credentials) and the future case where enrolment happens on a device with credentials already issued (non-zero).
6. No regression in the existing Feature 114 test suite (citizen wallet endpoints, hub, sync, renewal).

## Open items for the implementation plan

Carried forward to the plan, not litigated here:

- Exact CSS animation parameters for the skeleton-pulse (duration, ease, opacity stops) — design-time call during implementation.
- Exact full-screen takeover layout values (padding, id-card max-width on tall phones, dismiss-button placement) — same.
- Whether to use a `RoutePrefix` query parameter or an IndexedDB-based flag for the pending-application label coming from the walkthrough script — likely the new wallet endpoint is simplest, but worth a brief implementation check.
- Test coverage strategy for the takeover persistence (Playwright? unit test against `WalletFlags` store?) — likely both.
- Whether `welcomedAt` is per-device or per-account — leaning per-device (lives in IndexedDB) because the welcome is about *this device on this account*, not a one-shot-per-account thing.

## References

- Umbrella: `docs/superpowers/specs/2026-05-13-strathcarron-citizen-arc.md`.
- Citizen Wallet PWA architecture: `.claude/skills/sorcha-architecture/SKILL.md` → "Citizen Wallet PWA (Feature 114)".
- AssuredIdentity walkthrough: `walkthroughs/AssuredIdentity/README.md`.
- AssuredIdentity feature: `specs/107-assured-identity-v1/`.
- Existing wallet Home: `src/Apps/Sorcha.Citizen.Wallet/Pages/Index.razor`.
- Existing enrol wizard: `src/Apps/Sorcha.Citizen.Wallet/Pages/Enrol.razor`.
- Shared id-card renderer: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/ReviewSummaryRenderer.razor`, `IdCardLayout.razor` (umbrella invariant: reused via watermark state).
