# Phase 0 Research — AssuredIdentity on the PWA

**Feature**: 124-assured-identity-pwa
**Date**: 2026-05-14
**Status**: Complete

This document resolves all open-question items from the validated design and surfaces patterns the implementation plan should follow.

## R-001 — Storage for the pending-application notice

**Decision**: `IDistributedCache`-backed store keyed by `sorcha:wallet:pending-app:{platformUserId:N}`, 24-hour absolute TTL. Production resolves to Redis via the existing SorchaConnections cascade; in-memory in tests via the existing `IDistributedCache` default registration.

**Rationale**:
- Notice is by definition ephemeral — it represents an in-flight application that ends in either a credential issuance (cleared by the walkthrough script) or expiry. TTL-based expiry is the right primitive.
- Avoids an EF migration in the Wallet Service entirely, which keeps the change off the storage-audit-gated path (Feature 113). New entities on `WalletDbContext` would require migration squashing per the pre-release rule (the project has just done #687 squash recently) — out of proportion for a label.
- Redis already wired into Sorcha.Wallet.Service via existing `IDistributedCache`. Zero new infrastructure.
- Single-writer semantics with TTL match the spec's edge case "pending-application notice is set but no credential ever arrives" — eventually self-clearing.

**Alternatives considered**:
- *EF entity on `WalletDbContext`*: rejected. Migration overhead, audit-list inclusion question, persistence outliving the application's lifetime is undesirable.
- *In-memory dictionary on a singleton service*: rejected. Wallet Service can run multi-replica; in-memory state per replica would produce ghost-notices when traffic lands on the "wrong" replica.
- *Atomic Distributed Cache (`IAtomicDistributedCache`, Feature 113)*: not needed. The notice is set/read/cleared with no CAS semantics required.

## R-002 — PWA-side persistence for the welcomedAt flag

**Decision**: New `IWalletFlagsStore` interface mirroring the existing `IDeviceMetaStore` pattern, persisting to the `device` IndexedDB store under the key `flags`. In-memory variant for tests. Single record (`WalletFlagsRecord { WelcomedAt: DateTimeOffset? }`).

**Rationale**:
- Mirrors the established Feature 114 pattern documented in CLAUDE.md → "PWA service tests" memory: "extract IJSRuntime-touching state behind small interfaces ... each interface ships with both impls in the same file."
- IndexedDB store `device` already exists and is the correct semantic home (device-local data) for a per-device flag.
- Single record keeps the surface tiny — no schema concerns, no key proliferation.

**Alternatives considered**:
- *Reuse `IDeviceMetaStore`* with an added field: rejected. The existing `DeviceMetaRecord` is record-immutable and shape-locked by the enrolment ceremony; extending it forces all callers to handle a new field. Separation of concerns wins.
- *LocalStorage*: rejected. PWA convention is IndexedDB for any persisted state; LocalStorage isn't used elsewhere in the wallet.
- *Cookie*: rejected. Per-device-per-app data shouldn't ride on HTTP.

## R-003 — Hooking into the existing CredentialAvailable push

**Decision**: Subscribe inside `Index.razor`'s existing `OnHubCredentialAvailable` callback. Add a check: if `_credentials` transitions from zero to non-zero and `WalletFlags.WelcomedAt` is null, render the takeover component. The existing callback already invokes `SyncNowAsync` which refreshes `_credentials`; the takeover branch lives in the `SyncNowAsync` completion handler.

**Rationale**:
- Zero new push infrastructure. The hub is already attached in `OnInitializedAsync` and detached in `DisposeAsync` (per the existing Index.razor implementation).
- Foreground and cold-open paths converge through the same code path: on first paint, after `SyncNowAsync`, check the transition condition. Foreground = sync triggered by hub; cold-open = sync triggered by `OnInitializedAsync`.

**Alternatives considered**:
- *Service-worker push notification*: out of scope. The PWA already has SignalR for the foreground/in-app case; the wallet's pull-on-open path is the cold-open primitive.
- *Polling*: rejected. Existing push + on-init sync covers both cases.

## R-004 — The blueprint's targetAudience

**Decision**: Change `walkthroughs/AssuredIdentity/blueprints/assured-identity.json` action 3's `credentialIssuanceConfig.targetAudience` from `"HaipExternalWallet"` to `"SorchaLocalWallet"`. Remove the references to HAIP-compatible external wallets from the action's `description` and `claimUI` copy.

**Rationale**:
- The spec assumed the blueprint was already at `SorchaLocalWallet`. Research surfaced it is `HaipExternalWallet`. This is the load-bearing change that makes the credential land in the PWA rather than the filesystem wallet.
- The blueprint enum value `SorchaLocalWallet` is the existing target-audience constant for citizen-PWA delivery (`Sorcha.Architecture` skill, "SorchaLocalWallet citizen-PWA worked example").

**Alternatives considered**:
- *Keep both audiences via a dual-target value*: not supported by the issuance config. One audience per action.
- *Issue twice*: rejected. Adds complexity for a demo path that's now PWA-only.

## R-005 — Files and references to remove (HAIP filesystem wallet)

**Decision**: Delete the directory `walkthroughs/AssuredIdentity/wallet/` (contains `credentials/`, `holder-key.jwk.json`, `holder-key.pem`). Remove references from `setup.ps1`, `run-phase1-identity.ps1`, `run-phase2-licence.ps1`, `run-agents.ps1`, and the `README.md` (currently lists `wallet/` in the file tree).

**Rationale**:
- Grep across `walkthroughs/AssuredIdentity/` confirms the references are concentrated in five files. Surgical removal.
- `run-multi-peer.ps1` is independent (register-native delivery path) and does not reference the filesystem wallet — verified by inspection of the script's header comment.

**Alternatives considered**:
- *Move to a `legacy/` subfolder*: rejected per Q3d in the design phase — confirmed no consumer depends on it.

## R-006 — Walkthrough script seam for setting/clearing the notice

**Decision**: Add a tiny helper to the existing `SorchaWalkthrough` PowerShell module (`walkthroughs/modules/SorchaWalkthrough/`) — `Set-CitizenPendingApplication` / `Clear-CitizenPendingApplication` — that calls the new Wallet Service endpoint with the demonstration citizen's JWT (already in module state via the existing `New-CitizenSession` helper). Phase 1 script calls `Set` after submitting action 1 and `Clear` after action 3 completes; Phase 2 reuses the same helpers.

**Rationale**:
- Module-based helpers follow the existing Sorcha walkthrough convention (the module is imported by every walkthrough script).
- Setting/clearing from the script, not inside `ActionExecutionService`, keeps Spec 1's wallet-server change small (no new submission-side coupling) and leaves the production integration question for Spec 2's follow-up.

**Alternatives considered**:
- *Have Blueprint Service auto-set the notice on submission*: rejected as out of scope for Spec 1 (Spec 2 follow-up).
- *Have the AI verification analyst's agent set the notice*: rejected — the agent runs alongside, not before, the submission; timing would be unreliable.

## R-007 — Test project for the PWA

**Decision**: Create `tests/Sorcha.Citizen.Wallet.Tests` if absent. Mirror the structure of `tests/Sorcha.Wallet.Service.Tests/Services/` (which is referenced in memory as the in-memory-DbContext pattern home). PWA service unit tests follow the "small-interface, in-memory variant in tests" pattern documented in CLAUDE.md memory.

**Rationale**:
- Existing PWA tests live in this project per Feature 114's US4 work (test patterns in CLAUDE.md memory). New tests slot in naturally.
- E2E coverage extension uses the existing Playwright suite — no new test project for E2E.

**Alternatives considered**:
- *Tests inside Sorcha.Citizen.Wallet itself*: rejected. Sorcha convention places tests in a sibling `tests/` project.

## R-008 — Welcomed-flag persistence: per-device confirmed

**Decision**: Per-device (lives in IndexedDB on the wallet device, not on the server). Confirms the spec's Assumption #3.

**Rationale**:
- A citizen enrolling a second device on the same account has never seen the welcome on that device. Suppressing it because they were welcomed on a different device would defeat the point of the welcome — recognising the act of giving Sarah a new wallet on this device.
- Server-side persistence would create a question about what "the same wallet" means across re-installs, account-method changes, and the like. Per-device sidesteps all of that.
- Clearing local data → welcome refires is a reasonable equivalence-class behaviour (treat the wallet as new).

**Alternatives considered**:
- *Per-PlatformUser server-side flag*: rejected per above.

## R-009 — Welcome takeover animation discipline

**Decision**: Pure CSS animation (no JS-driven). Two keyframes: fade-in (200 ms, opacity 0→1) and dismiss-out (180 ms, opacity 1→0, slight downward translate). Skeleton pulse for the waiting card: 1.4 s ease-in-out, opacity 0.4→0.8→0.4. All in `wwwroot/css/welcome-takeover.css`.

**Rationale**:
- The wallet runs on mobile browsers where main-thread cost matters; CSS keyframes are GPU-composited.
- MudBlazor has its own dialog component but it's chrome-heavy for what's needed; a plain overlay with the id-card and a single MudButton is simpler and matches the design doc's "less ceremony than a modal" feel.

**Alternatives considered**:
- *MudDialog*: rejected as too heavy and visually inconsistent with the bespoke welcome moment.
- *JS animation library*: rejected — bundle weight, no benefit over CSS for these simple transitions.

## R-010 — Demo's pending-application label

**Decision**: Label string `"Assured Identity"` exactly, set by `run-phase1-identity.ps1`. Wallet UI hard-codes the prefix copy ("Your `<label>` application is being reviewed.") so the label appears inline without server-side templating.

**Rationale**:
- Spec FR-002's exact copy ("Your Assured Identity application is being reviewed. You'll see it here when it's ready.") interpolates the label.
- Keeping the label as plain text (no claim names, no IDs) preserves the "no credential content in the notice" invariant from FR-008.

**Alternatives considered**:
- *Server-side templated message string*: rejected. Over-engineering for one label in one demo flow.

## R-011 — Re-evaluation triggers for the takeover after open

**Decision**: The takeover-eligibility check runs in three places: after every `SyncNowAsync` completion, on `OnHubCredentialAvailable` (after its internal sync), and once at the end of `OnInitializedAsync`. The first place dominates; the latter two are belt-and-braces for race ordering.

**Rationale**:
- Cold open: sync runs from `OnInitializedAsync` → completes → eligibility check fires → takeover renders.
- Foreground: hub event → invokes sync → completes → eligibility check fires → takeover renders.
- Avoids any timing window where the credential is present but the takeover hasn't fired.
- Idempotent because the check guards on `WelcomedAt is null` and immediately persists `WelcomedAt = now` when it renders.

**Alternatives considered**:
- *Single eligibility check in `OnInitializedAsync`*: rejected. Misses the foreground case where sync happens after init.

## Open items for Phase 1 design

- Exact OpenAPI shape for the new endpoint (set vs. clear vs. read; one resource or three?). Resolved in `contracts/pending-application-notice.openapi.yaml`.
- Exact wire shape for `WalletFlagsRecord`. Resolved in `data-model.md`.
- Demo runbook ordering. Resolved in `quickstart.md`.

All [NEEDS CLARIFICATION] from Technical Context: zero. Plan is unblocked for Phase 1.
