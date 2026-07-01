# Cross-installation federation: anonymous public-register read + node-identity peer auth

**Date:** 2026-07-01
**Status:** Design (for implementation in-repo — not a prodexec hand-off)
**Owner:** Stuart

---

## 1. Problem

A node in one installation cannot bootstrap trust from another installation's **public** register.

Concretely (root-caused 2026-07-01): a local Docker stack running as installation **`Phaethon`**
(`JwtSettings__InstallationName=Phaethon`, `NodeId=Phaethon.sorcha.dev`) is configured to pull the
**Sorcha System Register (SSR)** from n1 (installation **`sorcha`**). The peer dials n1 correctly
(`n1.sorcha.dev:50051`, TLS, port open) and n1 is fully reachable (HTTPS 200 to `/health`, `/app`,
`/peer/health`) — **there is no network/firewall block.** The handshake **fast-fails (~60 ms)**
because the peer authenticates with an installation-scoped **`{installation}:service`** JWT, and
under the Feature 136 tiered-audience/issuer hardening **cross-installation tokens are rejected by
design**. So:

> Phaethon service token → refused by sorcha (F136) → peer marks n1 dead → SSR never syncs →
> local registers stay at `Height=0` (nothing seals) → every downstream publish/seal (e.g. the AIAS
> demo blueprint publish) fails with 403 / seal-timeout.

**Two separate authorities is correct.** The bug is using a *local-installation credential to
authenticate against a remote installation* for what should be an **open, public** operation.

## 2. Principle

**A public register is open data. Reading and replicating it must not require the caller to hold a
credential in the register's installation.** Trust in a replicated register is established by the
register's **own cryptography** — its genesis `InitialControlRecord` attestations, `CryptoPolicy`,
sealed-docket/validator signatures, and the register's DID/roster — **not by the caller's token.**

This is the decentralised (DAD) model: disclosure of public data is open by default; permissionless
federation means installation A can replicate installation B's public registers without being
enrolled in B's identity domain. The SSR is the **root of trust** and is `Advertise: true,
IsFullReplica: true` — being able to pull it anonymously is exactly how a new node bootstraps.

## 3. Design

### 3.1 Anonymous read + replicate for public registers
- Register **read** and **replicate/sync** paths allow **anonymous** access **when the register is
  public** (the `Advertise` flag / an explicit public-read policy). Private / non-advertised
  registers are unchanged — they still require register-scoped auth.
- The puller **MUST cryptographically verify** a replicated register before trusting/persisting it
  (genesis attestations + `CryptoPolicy` + docket/validator signatures + register DID/roster),
  **fail-closed**. Never trust-on-transport.
- Anonymous read is **rate-limited** (existing centralised SEC-002 limiting). Open ≠ unlimited.

### 3.2 Node-identity peer authentication
- The **peer federation handshake / gossip / sync** authenticates with the **node's own keypair**
  (an installation-neutral peer identity), **not** a `{installation}:service` JWT. Two nodes from
  different installations can peer and exchange public-register data by (a) proving node identity and
  (b) verifying register crypto — no shared installation required.
- Installation-scoped service JWTs remain correct for **intra-installation** service-to-service
  calls; they are simply the wrong instrument for **inter-node federation**. F136's cross-installation
  rejection stays as-is for *authorization*; federation gets a path that never presents an
  installation token in the first place.

### 3.3 What stays authenticated (unchanged — DAD alteration-control preserved)
- **Writes to any register** require the **target register's** governance/participant authority
  (resolved via that register's roster/participant DIDs — register-scoped, not installation-scoped).
  Anonymous read does **not** enable cross-installation writes: a Phaethon node can *read* n1's public
  registers but cannot *write* to them without being a participant on them.
- **Private / non-advertised registers** require auth.
- **Governance operations** unchanged.

## 4. Decision: fully anonymous read (not "any authenticated foreign identity")

Public-register read is **fully anonymous** (with mandatory crypto-verification + rate limiting),
**not** "readable by any authenticated identity from any installation." Rationale: it is simpler,
more open, and the register's cryptography already carries the trust. The only reason to prefer
authenticated-but-cross-installation read is per-reader audit/metering — not worth the coupling for a
root-of-trust public register. (If metering is ever needed, add optional, non-gating attribution.)

## 5. Code seams (confirm exact locations at implementation)

- **Peer service (`Sorcha.Peer.Service`)** — the handshake/health/sync auth is the ~60 ms refusal.
  Replace the installation service-JWT on the federation path with node-identity auth. Touch points
  observed: `PeerListManager`, `HealthMonitorService`, `SeedNodes` config
  (`PeerService__SeedNodes__*`, `EnableTls`), the gRPC peer transport (:50051). **Confirm whether the
  node already has a signing keypair distinct from `ServiceAuth` (ClientId=`service-peer`).**
- **Register service read/replicate endpoints** — `/api/registers`, `/api/registers/{id}` (and the
  sync/replication endpoint). Add an **anonymous path gated on the register's public/`Advertise`
  flag**; keep private registers behind auth. (Today `/api/registers` returns 401 unauthenticated.)
- **Replication verification** — ensure the sync path verifies genesis attestations + docket/validator
  signatures **before persisting** a replicated register (via `ITrustEvaluator` / register
  verification). If verification on replicate is currently implicit or missing, that is a gap to
  close as part of this work — anonymous read is only safe with mandatory verification.
- **F136 boundary (`SorchaAudiences` / `SorchaIssuer`)** — **no change** to cross-installation token
  rejection for authenticated authorization. This work adds an anonymous/public read path that does
  **not** validate an installation token at all; it must not be implemented as "accept foreign
  installation tokens."

## 6. Non-goals
- Cross-installation **writes**, or issuing "foreign/federation service tokens."
- Collapsing installations / shared identity (the two authorities stay separate — that is correct).
- Weakening F136 audience rejection for authenticated authorization.

## 7. Security analysis
- **Anonymous read is safe iff**: (a) mandatory crypto-verification of the replicated register,
  fail-closed; (b) strict gating on the public/`Advertise` flag (never expose private registers);
  (c) rate-limiting the anonymous path.
- **Threats & mitigations**: DoS on the open read path → rate limit; spoofed/forged register served by
  a hostile peer → crypto-verify genesis + docket/validator sigs (reject on mismatch); private-data
  leak → the gate keys only on the public flag; node-identity spoofing on the peer path → node keypair
  proof.

## 8. Validation
- **Primary**: a `Phaethon` node pulls the `sorcha` (n1) **public SSR anonymously**, verifies its
  crypto, replicates it, local sealing resumes, and the AIAS demo then provisions end-to-end — with
  the two installations remaining distinct.
- **Negative**: a **private** register on n1 still returns 401 to the Phaethon node; a **write** to an
  n1 register from Phaethon is still refused (not a participant).
- **Regression**: intra-installation service-to-service auth and F136 audience rejection unchanged.

## 9. Open questions (resolve during planning)
1. Does the peer already hold a node keypair/identity distinct from the `service-peer` JWT, or is one
   introduced by this work? (`NodeId=Phaethon.sorcha.dev` exists; is there a signing key behind it?)
2. What exactly is the "public" gate — the `Advertise` flag alone, or a dedicated public-read policy
   on the register?
3. Does the replication/sync path already cryptographically verify a pulled register, or is that a
   gap this work must fill?
4. Peer TLS: the seed dials n1 with `EnableTls=true` while the local peer server runs `EnableTls=false`
   — confirm the federation TLS posture (mutual? server-only? node-cert vs installation-cert) as part
   of the node-identity design.
