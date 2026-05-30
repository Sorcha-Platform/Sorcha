# Cross-installation register subscription over NAT (Feature 143 demo)

How a register **owned by one Sorcha installation** is subscribed to, replicated by, and
submitted-against by a **different installation** — across a real NAT boundary — using the
Feature 143 reverse-stream rendezvous.

**Topology proven (2026-05-30):** `tiny` = NAT'd register **owner/issuer** (outbound-only,
public IP 81.111.103.112) dials out to `n1` = public **subscriber** (Azure, `n1.sorcha.dev`,
Caddy:50051 → peer-service h2c). Two **separate installations**: different `JWT_SIGNING_KEY`,
different `INSTALLATION_NAME` (`tiny.sorcha.dev` vs `n1.sorcha.dev`), and **independently-minted
system-register genesis** (different control-signing fingerprints). They peer; they do **not**
share JWT keys and do **not** peer each other's system register.

## The mechanism (the genuinely novel bit)

The trust boundary is the **register**, not JWT or the installation. Three facts make
cross-installation subscription work with no shared trust anchor:

1. **Register advertisement is pure peer gossip.** A register created with `advertise=true`
   (the `Advertise` flag on `POST /api/registers/initiate`) is announced to peers. The NAT'd
   owner pushes the advert to the public node over its **own outbound heartbeat / reverse
   stream** — the proven direction (the owner can't be dialed, but it dials out). The public
   node ingests it and learns `register 018230… is owned by peer tiny.sorcha.dev`. No
   installation context, no JWT — just `(registerId → peerId)`.

2. **Subscription needs only the registerId + a route to the owner.** The subscriber admin
   calls `POST /api/organizations/{orgId}/register-subscriptions { register_id }`
   (`RegisterSubscriptionService.SubscribeAsync` → status **Active**, fires a notify to
   Register Service to create a stub and start peer sync → peer-service `SubscribeToRegister`
   with mode `FullReplica`). The cross-peer gRPC sync itself is **unauthenticated** (peer auth
   is non-blocking) — the peer connection is the boundary, not a JWT.

3. **Register genesis trust is register-scoped, not installation-scoped.**
   `SystemRegisterSyncVerifier` verifies a synced genesis against the local trust anchor
   **only for the system register** (`SystemRegisterConstants.SystemRegisterId`, a hardcoded
   constant). **Every other register bypasses that check** and is verified against the
   **validator roster embedded in its own genesis control record** (Feature 086). So the
   public subscriber accepts the NAT'd owner's register — sealed by the owner's validator key,
   which is *not* in the subscriber's installation — purely on the register's self-contained
   roster. This is why two installations with totally different keys can share a register.

**Register Invitations are NOT the path.** `register-invitations` are organisation/installation-
scoped (the target org decrypts with a wallet known to *one* installation). They don't cross an
installation boundary. Ignore them for this topology.

## Transport (Feature 143)

- The NAT'd owner dials the public node and holds a persistent reverse `Stream`; the public node
  (rendezvous-capable: `PeerService__RelayRendezvousEnabled=true` or a detected external address)
  registers it in `ReverseStreamManager` keyed by the owner's peer id.
- **Submit fan-out** (`TransactionDistributionService.ForwardSubmissionAsync`) and **replication
  pull** (`RegisterReplicationService` + `RegisterSyncBackgroundService` relay poll) reach the
  NAT'd owner by brokering over the held reverse stream
  (`RelayCommunicationService.SendViaRelayAsync` self-anchors on `ReverseStreamManager.TryGetStream`).
- **Connection-direction invariant:** the subscriber initiates every cross-node connection, so
  the owner must be inbound-reachable — satisfied here by the owner dialing out and being reached
  over its own reverse stream.

## Fix landed during bring-up — PR #880

The replica-pull path gated the reverse-stream relay on an **empty** advertised address, but a
NAT'd owner self-registers via `RegisterPeer` with a non-empty *placeholder* address
(`PeerConnectionPool:434`, `ResolvedPeerId` — the comment even notes the remote "never dials this
back over NAT"). So a public subscriber holding the owner's reverse stream silently skipped the
relay and never replicated. Added `RelayCommunicationService.CanReachViaReverseStream(peerId)` and
used it in `RegisterReplicationService` (full-replica pull) and `RegisterSyncBackgroundService`
(relay poll), so a held reverse stream engages the relay regardless of advertised address —
matching what the submit/fan-out path already did. Verified: n1 logs
`Attempting relay batch sync … from NAT'd peer tiny.sorcha.dev` → `fully replicated`.

## Operator sequence (verified)

```
# ISSUER (tiny):  deploy/twoinstall-issuer.ps1  — runs against http://tiny:8090
#   org + analyst + ADVERTISED DevMode register (tiny owns, tiny validator on roster) + blueprint
# SUBSCRIBER (n1): POST /api/organizations/{publicOrg}/register-subscriptions { register_id }
#   -> n1 discovers the advert, subscribes Active/FullReplica, pulls genesis over the reverse stream
# CITIZEN (n1):    deploy/twoinstall-citizen-n1.ps1 — signup -> wallet -> submit Action 1
#   -> fans out to tiny over the reverse stream for sealing
```

## Status

Proven: two separate installations; F143 reverse stream across the real NAT boundary;
cross-installation advert → subscribe → **genesis docket replicated** over the reverse stream
(PR #880). **Open gap blocking the full credential loop:** post-genesis dockets (the
BlueprintPublish) fail finalisation on the subscriber because the relay-synced genesis Control
transaction loses its `MetaData` in the wire→Mongo→`TransactionModel` round-trip
(`RelayMessageHandler:468` serialises with default/PascalCase options; the roster-extraction
strategies key off `MetaData.TransactionType==Control`), so the validator roster can't be
reconstructed and the blueprint never reaches the subscriber. Fix: make the relay tx
serialise / ingest / re-read agree on naming policy + case-insensitivity (the F142/086
roster-reader family, extended into the F143 relay path).
