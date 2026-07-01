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

---

## Phase A confirmed seams (2026-07-01) — T005 (gate)

Two read-only investigations settled O1–O4 with file:line evidence. **This corrects the design's
root-cause and resizes Phase C.**

### O1 — Peer federation auth → **BUILD (was assumed "reuse/adjust")**
The peer's **outbound** federation calls are **UNAUTHENTICATED** — `PeerConnectionPool.CreateChannel`
(:535-546) is a bare channel; no Bearer/interceptor; the `service-peer` `ServiceAuth` env is **unused**.
`NodeId` is a **string only** (`PeerServiceConfiguration.cs:20-28`); **no node key/cert**; the
challenge/nonce config (`ChallengeTtlSeconds`) is **unimplemented** — `RegisterPeerRequest` has no
signature field and `PeerDiscoveryServiceImpl.RegisterPeer` does **zero** crypto validation. ⇒
Node-identity peer auth is **net-new**, and it's also a **security hardening** (peers currently
register with no identity proof). **Change points:** `PeerConnectionPool.CreateChannel`,
`peer_discovery.proto` (add signature/challenge), `PeerDiscoveryServiceImpl.RegisterPeer`, a new node
key/cert type, `Program.cs` cert wiring. Node has `ICryptoModule` via DI to reuse for signing.

### O2 — Public gate → **PARTIAL: `Advertise` exists but is NOT an access gate**
`Register.Advertise` (bool) is used **only for outbound peer advertisement**
(`AdvertisementResyncService.cs:112`, `Program.cs:749-750`) — **never for read authorization**. Read
endpoints `GET /api/registers` + `/{id}` require `RequireAuthenticated` (`Program.cs:559-561` → 401
anon); dockets require `CanReadTransactions` (`:1390-1392`). ⇒ We must **wire `Advertise` into an
anonymous read path** (new). **Change points:** `Program.cs:559-561, 671-715` (+ docket read).

### O3 — Verify-on-replicate → **GAP, but the LOGIC exists on the wrong side of the trust boundary**
Verification today lives in the **Peer Service** `DocketFinalizationService.cs:112-173` (genesis-trust
vs. trusted genesis file, chain integrity, docket hash, **proposer signature via `ICryptoModule`**)
**before** it POSTs to the Register Service. The **Register Service ingest** (`WriteDocket`,
`Program.cs:1478-1841`) does **NOT** re-verify — a comment says *"the peer already verified"*;
`GenesisControlRecordExtractor.cs:32-69` extracts the control record **without** validating attestation
signatures or that RegisterId matches. ⇒ For **untrusted-peer** federation this is a real gap: the
**ingesting node must verify authoritatively, fail-closed**, not trust the delivering peer. Good news:
the crypto logic is **reusable** (`DocketFinalizationService` pattern + `ICryptoModule` +
`RegisterControlRecord` attestations/roster) — the work is **relocating/duplicating it to the
authoritative ingest** + adding **attestation-signature** + **RegisterId-match** checks. **Change
points:** `Program.cs:1478-1841`, `GenesisControlRecordExtractor`, new verifier utility (mirror
`RegisterCreationOrchestrator.VerifyAttestationsAsync`).

### O4 — TLS → **n1:50051 serves TLS; client TLS wiring is bare**
Runtime probe: n1:50051 is **HTTPS** (cleartext GET → *"Client sent an HTTP request to an HTTPS
server"*; TLS negotiates a cert). Client dials `https://` (`EnableTls=true`) which is correct, but
`CreateChannel` sets no explicit TLS/cert handling and the server runs H2 **cleartext**. ⇒ Fix client
TLS wiring + trust; **mTLS with a node cert can double as O1's node identity** (unify the two).

### ⚠️ Root-cause correction (propagate to design note + memory)
The earlier **"F136 cross-installation token rejection on the peer handshake"** conclusion is **WRONG**:
the peer sends **no token** on outbound calls, so F136 cannot be rejecting the peer link — the ~60ms
failure is **TLS/transport** on the gRPC channel. **F136 only bites the register *read* path** (the
`/api/registers` 401). The feature's design still holds; the *peer* mechanism was mis-attributed.

### Locked decisions
1. **Peer auth = BUILD** node identity (node key/cert), ideally as **mTLS client cert** (satisfies O1+O4
   together) + signed `RegisterPeer` challenge.
2. **Anonymous read = new path** gated on `Advertise==true` (per-request), bypassing installation-token
   validation (not accepting foreign tokens).
3. **Verify-on-replicate = authoritative at the ingesting node, fail-closed** — reuse the existing
   `DocketFinalizationService` crypto, add attestation + RegisterId checks, run it where data is
   persisted regardless of delivering peer.
4. Phase B slightly larger than first scoped (net-new peer identity). Phase C's verify sub-task is
   **reuse+relocate**, not greenfield. **Gate cleared — ready for Phase B on confirmation.**
