# Implementation Plan: Cross-installation federation — anonymous public-register read + node-identity peer auth

**Branch**: `175-federation-public-read` | **Date**: 2026-07-01 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/175-federation-public-read/spec.md`

> Design source: `docs/superpowers/specs/2026-07-01-federation-anonymous-public-read-design.md`.

## Summary

Let a node in one installation pull/replicate another installation's **public** registers (the SSR
being the critical one) **without** an installation-scoped credential. Two seams: (1) the register
read/replicate path gains an **anonymous, public-gated** mode that verifies the register's own
cryptography before trusting it; (2) the peer federation handshake authenticates by **node identity**
rather than a `{installation}:service` JWT. Writes, private registers, and F136 authenticated-authz
rejection are unchanged.

## Technical Context

**Language/Version**: C# 14 / .NET 10.

**Primary components touched**:
- `Sorcha.Peer.Service` — federation handshake / gossip / sync auth (`PeerListManager`,
  `HealthMonitorService`, seed-node dial, gRPC :50051, `EnableTls`), currently authenticates with the
  `service-peer` installation service token.
- `Sorcha.Register.Service` — register read + replicate endpoints (`/api/registers`,
  `/api/registers/{id}`, sync path); add an anonymous path gated on the register's public/`Advertise`
  state.
- Register verification (`ITrustEvaluator` / register/genesis verification) — mandatory crypto
  verification of a replicated register before persistence.
- Auth boundary (`Sorcha.ServiceDefaults.Auth` — `SorchaAudiences`/`SorchaIssuer`, F136) — **not**
  modified for authz; the anonymous path must bypass installation-token validation, not accept foreign
  tokens.

**Storage**: No new storage. Registers/dockets in Mongo (per-register DB) as today.

**Testing**: xUnit unit/integration; a cross-installation federation integration test (two
installations, node A pulls B's public SSR anonymously + verifies) is the acceptance driver.

**Target Platform**: Docker (multi-node / two-installation) + n1.

**Performance/Constraints**: Anonymous public-read path rate-limited (SEC-002). Verification is
fail-closed. Public gate evaluated per-request.

**Project Type**: Backend microservices (peer + register) + shared auth/verification libs.

**Scale/Scope**: Federation control-plane change; bounded to the two seams above.

## Constitution Check

*GATE: pass before Phase 0. Re-check after design.*

| Principle | Assessment |
|-----------|------------|
| Microservices-first; no new service | ✅ Modifies existing peer + register services + shared verification; no new service. |
| Internal comms gRPC / external REST | ✅ Peer federation stays gRPC; register read is REST. |
| **Zero-trust: "all service-to-service auth"** | ⚠️ **Justified exception** — this is **cross-installation federation of PUBLIC data**, not intra-installation service-to-service. It is not unauthenticated-and-untrusted: the **register's cryptography** is verified (fail-closed) and the **peer link uses node identity**. Anonymous applies only to *reading public data*; writes/private stay authenticated. See Complexity Tracking. |
| Security / no new secrets | ✅ No new secrets. Node identity uses the node's own key (confirm O1). Rate-limited. |
| Testing (xUnit, integration) | ✅ Cross-installation integration test is the acceptance driver. |
| Docs / XML | ✅ Design note + this spec/plan; endpoint + auth docs updated at implementation. |
| License headers, .NET 10 | ✅ |

**Result: PASS with one justified deviation** (public-data anonymous read), recorded below.

## Project Structure

### Documentation (this feature)
```text
specs/175-federation-public-read/
├── plan.md          # this file
├── research.md      # Phase 0 — resolve the 4 open questions
├── data-model.md    # Phase 1 — public-register / node-identity / verification-result
├── quickstart.md    # Phase 1 — two-installation federation walkthrough
├── contracts/
│   └── federation.md   # anonymous read gate + node-identity peer handshake + verify contract
└── tasks.md         # Phase 2 (/speckit.tasks)
```

### Source (repository root)
```text
src/Services/Sorcha.Peer.Service/         # node-identity peer handshake/sync auth (replace installation JWT)
src/Services/Sorcha.Register.Service/     # anonymous read/replicate gated on public/Advertise
src/Core|Common/ (register/genesis verification, ITrustEvaluator)  # mandatory verify-on-replicate
src/Common/Sorcha.ServiceDefaults.Auth/   # (read-only) F136 boundary — anonymous path bypasses, not accepts
tests/ Sorcha.Peer.Service.Tests, Sorcha.Register.Service.Tests, Sorcha.Integration.Tests  # + cross-installation federation test
```

**Structure Decision**: Two focused seams (peer-auth, register-read-gate) plus a
verify-on-replicate guarantee. No new service, no schema change. The auth library is read-only — the
anonymous path is an *absence* of installation-token validation on public reads, gated by the
register's public state, not a new token type.

## Complexity Tracking

| Deviation | Why needed | Simpler alternative rejected because |
|-----------|------------|--------------------------------------|
| Anonymous (unauthenticated) read of public registers | Permissionless federation: a node in another installation must bootstrap trust from a public register (the SSR) without being enrolled in the remote installation | Requiring a remote/foreign service token couples the two authorities (out-of-band enrolment, no external issuance under F136) and contradicts "public"; anonymous + mandatory crypto-verify + rate-limit is safer and more open |
| Node-identity peer auth (not installation JWT) | Cross-installation peer links can't present an installation-scoped token (F136 rejects it) | Making both nodes the same installation collapses two correctly-separate authorities |
