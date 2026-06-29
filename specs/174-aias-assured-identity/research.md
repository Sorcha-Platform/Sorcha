# Phase 0 Research: AIAS Assured Identity (M1)

The unknowns for this milestone were resolved during the north-star brainstorming via three codebase
investigations (assurance/agent infra, photo path, and the credential-model root-solve). This file
consolidates the decisions; there were no open `NEEDS CLARIFICATION` items left for planning.

## R1 — Assurance workflow & issuance

**Decision**: Reuse the `demos/AssuredIdentity` 3-action blueprint (submit → verify/decide → claim)
and its `credentialIssuanceConfig` (HAIP `/api/v1/offers` → OpenID4VCI), adding a **reject route**.

**Rationale**: The full submit→decide→issue→claim path is proven end to end (`ActionExecutionService`
+ HAIP `CredentialOfferService` + `HaipCredentialMinter`). The only gap vs. the demo is that the
demo rule is always-approve with no reject branch.

**Alternatives considered**: Build a new AIAS-specific blueprint from scratch — rejected (duplicates
proven structure, more surface to test).

## R2 — Autonomous agent

**Decision**: Reuse the `Sorcha.Agent` CLI in **rules mode** (`RulesDecisionEngine`, JSON Logic),
run with an AIAS config. "Two agents" (Assure-ID / Cyber) = two configs of the same binary, not new
code.

**Rationale**: The agent already does autonomous decisioning over pending actions with dual
SignalR+polling listeners and records decisions back via `ActionExecutor`. No agent-runtime changes
needed.

**Alternatives considered**: `AiDecisionEngine` (Claude) for the decision — rejected for M1 (the
checks are deterministic; an LLM adds nondeterminism and cost to a step that doesn't need it). May
revisit for richer rejection copy.

## R3 — The one new capability: external-check hook

**Decision**: Add an **external-check hook** to the rules engine. Before evaluating JSON Logic, run a
configured set of checks (email-verified, photo-present, postcode-exists, profanity) and merge their
boolean results into the rules context as facts (e.g. `/checks/postcodeExists`,
`/checks/profane`, `/checks/emailVerified`, `/checks/photoPresent`). The existing JSON-Logic rules
then approve/reject on those facts.

**Rationale**: The rules engine today only evaluates JSON Logic over the submitted payload — it can't
call out to a postcode service or scan for profanity. A thin, testable pre-decision hook is the
minimal extension that keeps the decision itself declarative (rules stay JSON).

**Alternatives considered**: (a) a server-side pre-action validator endpoint stamping the payload —
rejected for M1 (more moving parts, a new endpoint, more to provision; chosen "checks run in the
agent" per the design decision). (b) hard-coding the checks in the agent decision — rejected (not
configurable, not unit-testable in isolation).

## R4 — Address / postcode existence

**Decision**: Use UK **postcodes.io** (public, no key) for the postcode-existence check, with a
**bundled offline fixture** fallback toggled by config (`assure-id.checks.json`). When postcodes.io
is unreachable, the check resolves against the fixture so the demo never breaks offline (SC-007).

**Rationale**: postcodes.io is free, keyless, and authoritative for UK postcodes — and gives the
on-brand humour hook ("could not locate that address"). The offline fixture satisfies the
reboot-proof / no-internet-venue constraint.

**Alternatives considered**: a paid address API (needs a key/secret — rejected); no real lookup
(loses the genuine check + the funny rejection — rejected).

## R5 — Profanity check

**Decision**: A **local wordlist scan** of the submitted free-text details, bundled with the agent
config. Deterministic, offline, no dependency.

**Rationale**: Keeps the check offline and dependency-free; the wordlist is editable for the demo.

**Alternatives considered**: an online moderation API — rejected (network dependency + key for a
trivial check).

## R6 — Photo path

**Decision**: Reuse F107 **as-is** — `FileRenderer` (camera `capture="user"` + upload) →
`PhotoTokenResizer` (240×320 JPEG ≤20KB) → `embedAs` → `portrait` claim on the Assured Identity VC.
The verdict-render path (`VerdictTrailPanel`) already paints the portrait. **No new photo code.**

**Rationale**: The capture→embed→render path exists and is tested. Photo is **optional** at the
Assured Identity stage (it becomes mandatory only for cyber in M2).

**Alternatives considered**: persona portrait persistence — rejected (not needed under the
single-credential / present-and-map model; avoids storing a biometric at rest).

## R7 — Branding

**Decision**: Bake AIAS branding into the **blueprint template** (`{{issuerName}}` token + an AIAS
theme / `x-review` header), as the AssuredIdentity demo does. Do **not** block on the unimplemented
admin-dashboard org-branding feature.

**Rationale**: Demo-level branding via the template is proven and sufficient for the conference;
per-org branding admin UI is out of scope and unfinished.

## R8 — Provisioning & repeatability

**Decision**: A single idempotent PowerShell module `demos/AIAS/AiasDemo.psm1` (mirroring
`demos/AssuredIdentity/AssuredIdentityDemo.psm1`) that creates org + branding + blueprint + agent
config from a clean network, Docker-first then n1, plus a `rehearse.ps1` test hook (one approval +
one rejection). Provisioning is built **in this milestone**, not deferred to M5 (M5 only
consolidates).

**Rationale**: The network will be wiped repeatedly; "re-run one script" is a hard constraint
(SC-001/SC-006). The AssuredIdentity module is a proven, idempotent template to follow.
