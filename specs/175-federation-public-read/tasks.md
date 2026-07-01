# Tasks: Cross-installation federation — anonymous public-register read + node-identity peer auth

**Feature**: 175-federation-public-read | **Spec/Plan/Design**: [spec](./spec.md) · [plan](./plan.md) · design note `docs/superpowers/specs/2026-07-01-federation-anonymous-public-read-design.md`

**STOP-AT-IMPLEMENTATION**: this file is the terminal `/speckit.tasks` artifact. Do **not** start
coding until confirmed. `[P]` = parallelizable (different files, no dependency). Each task names its
target area; exact paths finalise in Phase A.

> **Status (2026-07-01):** Phase A ✅ · Phase B ✅ (T010–T013, peer suite 733/733) · Phase C ✅
> (T020/T022 anonymous read; T021 revised to reuse-in-place RegisterId-match — full verify-on-replicate
> relocation deferred to a flagged ADR, see `research.md` "Register service should own docket integrity")
> · Phase D ✅ (write-refusal covered by Phase C tests; F136 authed rejection unchanged — existing tests)
> · Phase E: docs done here; **T040 two-node e2e is the live-validation step** (run against a real
> two-installation setup, not an automated CI test). PR #1073.

---

## Phase A — Verify the seams (resolve O1–O4 before touching code)

- **T001 [P]** Trace `Sorcha.Peer.Service` federation handshake/sync auth: confirm it presents the `service-peer` installation JWT, and whether a **node signing key/cert** already exists distinct from it (**O1**). Record the exact auth wiring + files.
- **T002 [P]** Confirm the **public gate**: is `Advertise == true` the authoritative public-register flag, or is there a separate public-read policy (**O2**)? Note the register read/replicate endpoints in `Sorcha.Register.Service`.
- **T003 [P]** Trace the register **replicate/sync ingest** path: does it already crypto-verify (genesis attestations + policy + docket/validator sigs) fail-closed before persisting, or is that a gap (**O3**)? Identify the `ITrustEvaluator`/verification entry points.
- **T004 [P]** Confirm **peer TLS posture** actually served by n1's published peer endpoint (plain gRPC/TLS/mTLS on 50051) so the client + node-cert design matches (**O4**).
- **T005** Consolidate T001–T004 into a 1-page "confirmed seams" note appended to `research.md`; lock: use-existing vs introduce node key; verify-gap yes/no; TLS posture. **Gate: do not proceed until this is settled.**

## Phase B — Node-identity peer auth (US2, P1) — the link must work first

- **T010** Introduce/confirm **node identity** for the peer (node key or node cert), installation-neutral, distinct from the `service-peer` installation JWT. *(depends T001/T005)*
- **T011** Replace the installation-JWT auth on the **peer handshake/gossip/sync** with node-identity auth (+ TLS/mTLS posture per T004). Two nodes in different installations complete the handshake without presenting an installation token.
- **T012 [P]** Unit tests (`Sorcha.Peer.Service.Tests`): handshake succeeds cross-installation with node identity; still rejects an unidentified/forged node.
- **T013 [P]** Confirm intra-installation peer behaviour unchanged (regression).

## Phase C — Anonymous public read + verify-on-replicate (US1, P1)

- **T020** Add an **anonymous read/replicate path** on the register read + sync endpoints, **gated on the public/`Advertise` state** (per-request), bypassing installation-token validation (not accepting foreign tokens). *(depends T002)*
- **T021** Make **verify-on-replicate mandatory + fail-closed**: genesis attestations + crypto policy + docket/validator sigs + register-id match, before persist. Close the gap if T003 found one. *(depends T003)*
- **T022** Apply **rate-limiting** (SEC-002) to the anonymous public-read path.
- **T023 [P]** Unit/integration (`Sorcha.Register.Service.Tests`): anonymous read of a **public** register returns it; a **tampered** register is rejected (not persisted); anonymous read of a **private** register is refused.

## Phase D — Guard rails (US3, P1) — must-not-regress

- **T030 [P]** Tests: **write** to a register from a non-participant/other installation is refused (no cross-installation write).
- **T031 [P]** Tests: **F136** cross-installation rejection for **authenticated** calls unchanged; intra-installation service-to-service auth unchanged.

## Phase E — End-to-end + docs

- **T040** Cross-installation **integration test** (`Sorcha.Integration.Tests`): node A (installation A) pulls node B's public SSR anonymously → verifies → replicates → local sealing proceeds past height 0. (This is the AIAS-unblock proof — SC-005.)
- **T041 [P]** Docs: register read/replicate endpoint auth + the federation model (API-DOCUMENTATION.md, AUTHENTICATION-SETUP.md, service READMEs); note the deliberate public-anonymous-read + node-identity design.
- **T042 [P]** If applicable, correct the AIAS live-validation note once federation unblocks provisioning (`aias-conference-demo.md` follow-through).

---

### Dependencies / order
Phase A (gate) → Phase B (peer link) → Phase C (read+verify) → Phase D (guards, parallel with C tests) → Phase E (e2e+docs). B before C: no anonymous read is reachable until the cross-installation peer link is up.

### MVP
Phases A+B+C deliver the core unblock (a node pulls + verifies another installation's public SSR). D+E harden and prove it.
