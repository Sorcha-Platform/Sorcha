# Phase 0 Research: Federation anonymous public read (175)

Resolves the four open questions from the design note §9. Each carries a working hypothesis to
implement against and an **explicit verification step** to run first (these are the opening tasks in
Phase 2, since they pin the exact seams).

## O1 — Does the peer already have a node keypair distinct from the `service-peer` installation token?
**Hypothesis**: The peer identifies as `NodeId=Phaethon.sorcha.dev` and dials seeds over TLS, so a
node identity concept exists, but the *auth presented on the handshake* is the `ServiceAuth`
(`ClientId=service-peer`) installation-scoped JWT — which is what F136 rejects cross-installation.
**Decision**: authenticate federation with a **node keypair** (the node's own signing key, installation-
neutral); if none exists distinct from the service credential, introduce one (node key + a signed
handshake / mTLS client cert).
**Verify first**: inspect `Sorcha.Peer.Service` handshake/auth wiring + whether a node signing key
already exists (vs. only the service JWT). This decides "use existing" vs "introduce node key."

## O2 — What is the exact "public" gate?
**Hypothesis**: The register model exposes `Advertise` (bool) + `Purpose` + `IsFullReplica` (seen on
the SSR: `Advertise: true, IsFullReplica: true, Purpose: 1`). **Decision**: gate anonymous read on
`Advertise == true` (public), evaluated per-request. **Verify first**: confirm `Advertise` is the
authoritative public flag and that no separate "public-read policy" already exists that should be
honoured instead.

## O3 — Does the replication/sync path already cryptographically verify a pulled register?
**Hypothesis**: Verification primitives exist (`ITrustEvaluator`, genesis `InitialControlRecord`
attestations, docket/validator signature verification), but whether the **replicate/sync ingest path**
runs them fail-closed before persisting is unknown. **Decision**: make verify-on-replicate
**mandatory and fail-closed**; if it's currently implicit/missing, closing that gap is part of this
feature (anonymous read is only safe with it). **Verify first**: trace the register sync ingest path
in `Sorcha.Register.Service` / verification core.

## O4 — Peer TLS posture
**Observed**: seed dials n1 with `EnableTls=true`; the local peer server runs `EnableTls=false`.
**Decision**: define the federation TLS posture as part of node-identity auth — prefer **mTLS with a
node certificate** (which doubles as node identity) or a signed-handshake over TLS. **Verify first**:
confirm what n1's published peer endpoint actually serves (TLS? mTLS? plain gRPC on 50051) so the
client posture matches.

## Cross-cutting decisions (from the design note)
- **Fully anonymous** public read (not "any authenticated foreign identity") — simpler, open, crypto
  carries trust.
- **F136 unchanged** for authenticated authz; anonymous path **bypasses** installation-token
  validation (does not accept foreign tokens).
- **Writes / private registers** unchanged (target-register governance, register-scoped).
- **Rate-limit** the anonymous path (SEC-002).
