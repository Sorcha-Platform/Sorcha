# Phase 0 Research: Assured Identity Demo Environment

All Technical-Context unknowns resolved. Most decisions were pre-settled in the approved design note and the live-state memory; this consolidates them with the integration signals grounded during planning.

---

## R1 — Provisioning toolkit shape (promote, don't rebuild)

**Decision**: Promote the gitignored `deploy/twoinstall-issuer.ps1` + `deploy/twoinstall-citizen-n1.ps1` into one PowerShell module (`AssuredIdentityDemo.psm1`) exporting four commands: `New-IssuingAuthority`, `Connect-Subscriber`, `Reset-Demo`, `Get-DemoStatus`. Internal logic factored into non-exported `lib/*.ps1` units so they are Pester-testable in isolation.

**Rationale**: The two scratch scripts already perform the proven flow end-to-end; the work is parameterisation, idempotency, readiness-gating, and a node inventory — not new behaviour. Reusing `walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1` keeps the API surface (auth, wallet, register, blueprint, participant helpers) consistent.

**Alternatives considered**: (a) a first-class `Sorcha.Cli` command — heavier, pulls demo orchestration into shipped product surface, slower iteration; rejected for a demo toolkit. (b) Aspire/compose automation — wrong layer (per-installation lifecycle, not app-host); rejected.

---

## R2 — Node-agnostic configuration (inventory file)

**Decision**: A `demo-nodes.json` inventory lists installations; each entry = `{ id, role, gateway, installationName, rendezvousCapable }`. `-IssuerNode` / `-SubscriberNode` select by `id`. A committed `demo-nodes.example.json` documents the shape; the real file is gitignored. Secrets stay in the existing `deploy/keys.env`, never in the inventory.

**Rationale**: Satisfies FR-006/FR-007 (swap/rename installations without touching the toolkit) and FR-009 (each node keeps its own trust material). `tiny`/`n1` become default entries, not assumptions.

**Alternatives considered**: hard-coded gateway URLs (the current scratch state) — fails FR-006; env-var-per-node — unwieldy beyond two nodes. Rejected.

---

## R3 — Agency-name coherence (single source → token injection)

**Decision**: `-AgencyName` is written at provision time into: org name, register name, published-participant org name, and the blueprint template's `x-review.header.issuerName` via a `{{issuerName}}` token replaced before publish. The credential-issuer DID is **not** rewritten — it derives from the issuer wallet address, which is stable across renames, so the DID stays valid.

**Rationale**: FR-002/FR-004/FR-005, SC-004. One value, propagated by the provisioner, removes the rename-coherence footgun. The `twoinstall-issuer.ps1` already sets the same name in 3 of the 4 places (org, register, published participant); adding the blueprint token closes the loop.

**Alternatives considered**: F142 Designer amend loop as the rename path — only edits the blueprint display string, leaves org/register untouched; kept as the *deep workflow* customisation path (US3) but not the identity-rename mechanism. Rejected as the primary.

---

## R4 — Readiness gate (the anti-`blueprint_not_available` predicate)

**Decision**: `Connect-Subscriber` does not return "ready" until **all three** hold for the target register on the subscriber: subscription `status == Active`; `GET /api/registers/{id}/sync-state` → `state == CaughtUp`; and the target blueprint appears in `GET /api/registers/{id}/blueprints/published`. Poll with bounded backoff (cap ~120s, comfortably over the observed ≤60s recovery window); on timeout, report a clear "not ready — recovery still in progress" status rather than a hard failure.

**Rationale**: FR-004, SC-002, SC-007. The third condition is the one that absorbs the BlueprintRecoveryService ~60s window; without it a tester hits `POST /api/instances` → `409 blueprint_not_available`. There is no dedicated recovery-status endpoint — the published-blueprints listing IS the signal (confirmed: `BlueprintRecoveryService` exposes no REST status; recovery is background/event-driven).

**Alternatives considered**: poll `POST /api/instances` itself — wasteful, creates junk instances; rejected. Fixed `Start-Sleep 60` — fragile, violates SC-002's "no transient error" under load; rejected.

---

## R5 — Idempotency & reconciliation

**Decision**: Before creating anything, `New-IssuingAuthority` probes for an existing authority (org by name/subdomain, advertised register, published blueprint) and reuses it. The specific footgun from live state — a `OrganizationRegisterSubscriptions` row pointing at a register absent from Mongo — is detected (subscription exists but register read 404/empty) and reconciled (drop the stale subscription, or recreate the register) rather than blindly reused. A per-run `state.json` records provisioned artefact IDs to make reuse and `Reset-Demo` deterministic.

**Rationale**: FR-003, SC-003, plus the explicit edge case. The memory records this exact desync biting before; making detection a first-class step is the fix.

**Alternatives considered**: "always reset then provision" — destroys a standing demo on every run, fails the "standing" intent; rejected. Reuse-without-reconcile — reproduces the known footgun; rejected.

---

## R6 — Agent mode wiring (rules / ai / human)

**Decision**: `-AgentMode rules|ai|human` (default `rules`). The provisioner renders an actor config from a tokenised template (analyst wallet/register/org IDs substituted from `state.json` via the agent's existing `{{placeholder}}` resolution) and: for `rules`/`ai` launches `sorcha-agent run --config <rendered> --state <state.json>` as a tracked child process; for `human` skips the launch and prints "log into the issuer node as the analyst and approve Action 2". `ai` adds an `ANTHROPIC_API_KEY` precondition check and a persona file.

**Rationale**: FR-010/FR-011, SC-005. `sorcha-agent` already supports `rules` and `ai` modes and placeholder substitution — no agent code change.

**AI-mode guardrail (resolves the deferred assumption)**: For `ai`, set a bounded decision wait via the agent's existing polling/retry config and document an operator fallback: if no decision is observed within the bound (default 90s), `Get-DemoStatus` surfaces "agent idle / decision pending" and the operator can either retry or switch that run to `human`/`rules`. v1 does **not** auto-fallback the engine mid-run (keeps behaviour predictable); the guardrail is detection + clear status + a documented manual switch. This satisfies FR-012's "not left stranded" without adding agent complexity.

**Alternatives considered**: auto-degrade `ai`→`rules` on timeout — hidden behaviour change mid-demo, surprising; deferred. No guardrail — fails FR-012; rejected.

---

## R7 — Tester journey relies on existing surfaces (no scaffolding)

**Decision**: Document, don't build. Entry = web SPA `/new-submissions` (verified working: `[Authorize]`, `POST /api/instances` under `CanExecuteBlueprints` = any authenticated user). Wallet pairing = real F128 cold-start onboarding (`/setup/add-device` → PWA enrol). Delivery + display = F124 pending-application + first-credential takeover in the PWA.

**Rationale**: FR-014/FR-015/FR-016, the design's "real UIs, no scaffolding" decision. Verified during specify that the entry point exists and needs no change.

**Recorded off-path (not used, not built)**: PWA `/applications` page (placeholder), `samples/strathcarron-portal` Blue Badge / Driving-Licence pages (non-functional stubs awaiting an unlanded backend PR). Called out in spec Out-of-Scope.

---

## R8 — Testing strategy (proportionate to PowerShell tooling)

**Decision**: Pester unit tests for the four pure-logic units (inventory load/select, agency-name injection, readiness predicate, idempotency reconciliation) — deterministic, no live services. The integrated flow is gated by a live **green run** on the default node pair, asserting the SCs (ready ≤10 min, tester loop ≤5 min, no transient error, idempotent re-run, all three agent modes, multi-node, status accuracy). Graduation cleanup (FR-021) is gated on that green run.

**Rationale**: Constitution IV adapted — there is no .NET core lib to hit 80% on; the equivalent rigor is deterministic units for logic + an E2E gate for integration. Matches how walkthroughs are validated today (by running), but adds real unit coverage for the new logic.

**Alternatives considered**: E2E-only (current walkthrough practice) — leaves the new idempotency/readiness logic untested in isolation; rejected. Full mock-the-platform integration harness — disproportionate for a demo toolkit; rejected.

---

## R9 — Demo location & graduation sequencing

**Decision**: `demos/AssuredIdentity/` is the new home (top-level `demos/` tree). Graduation cleanup — removing `walkthroughs/AssuredIdentity/**` and `deploy/twoinstall-*` — is a **distinct final phase, gated on a green run** (FR-021). Shared `walkthroughs/modules/SorchaWalkthrough/` is preserved and imported. Skills + memory updated to reflect "a demo is a mature walkthrough" and the new location (FR-022).

**Rationale**: Avoids deleting the working path before its replacement is proven; resolves the design's open path question by confirming `demos/AssuredIdentity/`.

**Alternatives considered**: keep under `walkthroughs/` — blurs the demo-vs-walkthrough concept the operator explicitly wants; rejected. Delete legacy immediately — premature before a green run; rejected.

---

## Resolved-unknowns summary

| Unknown | Resolution |
|---|---|
| Testing approach for PowerShell toolkit | Pester units + E2E green-run gate (R8) |
| Demo location path | `demos/AssuredIdentity/` (R9) |
| AI-mode guardrail | bounded wait + status surfacing + documented manual switch; no auto-degrade in v1 (R6) |
| Idempotency reconciliation mechanics | probe-and-reuse + stale-subscription-vs-missing-register detection (R5) |
| Readiness predicate signals | subscribe Active ∧ sync-state CaughtUp ∧ blueprint in /blueprints/published (R4) |
| Agent identity wiring | tokenised actor template + `{{placeholder}}` from state.json (R6) |
| Multi-node connect | `Connect-Subscriber` repeatable per subscriber id, each readiness-gated (R2/R4) |
