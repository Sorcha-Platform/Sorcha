# Quickstart: validating Peer NAT Traversal

How to exercise the feature locally and across the real `tiny`↔`n1` network.

## Roles

- **Rendezvous + subscriber** = public node (`n1`, or a local public stand-in). Has
  `PeerService:PublicAddress` set; accepts reverse `Stream`; owns no demo register.
- **NAT'd owner** = outbound-only node (`tiny`). `PublicAddress` empty;
  `SeedNodes` = the public node. Owns the register + validator.

## A. Two-peer in-proc integration (CI, no real NAT)

Simulate "NAT'd" by giving the owner peer no inbound listener / empty
`PublicAddress` and pointing it at the subscriber peer as its seed.

1. Start two `Sorcha.Peer.Service` instances in-proc: `pub` (PublicAddress set) and
   `natd` (PublicAddress empty, SeedNodes=[pub]).
2. Assert `natd` establishes a reverse `Stream` to `pub`; `pub.ReverseStreamManager.ActiveCount == 1`.
3. Create a register owned by `natd`; subscribe `pub`.
4. Submit a transaction on `pub` for `natd`'s register → assert it is brokered to
   `natd`, sealed, and the docket replicates back to `pub`.
5. Kill the reverse stream → assert `natd` reconnects and step 4 succeeds again
   (no operator action).

Run: `dotnet test tests/Sorcha.Peer.Service.Tests --filter "FullyQualifiedName~NatTraversal"`

## B. Real cross-node E2E (the SC-001 gate)

On `tiny` (NAT'd owner) and `n1` (public rendezvous + subscriber):

1. **Prep tiny**: docker reset + pull fresh CI images (the 9-day stale containers
   are cleared); start the peer-service + register/validator stack with
   `PeerService__PublicAddress=""` and `PeerService__SeedNodes__SeedNodes__0__Hostname`
   = n1's peer gRPC address.
2. **Prep n1**: ensure `PeerService__PublicAddress` is n1's reachable address;
   rendezvous enabled.
3. **Verify anchoring**: on n1, `peer_reverse_streams_active{role=rendezvous} >= 1`;
   n1's routing table shows tiny reachable via anchor n1 (self-anchor).
4. **Create + subscribe**: create a register owned by tiny; n1 subscribes.
5. **Submit**: submit an action on n1 against tiny's register.
   - **PASS (SC-001)**: it seals on tiny and the sealed docket is observable on n1.
6. **Resilience (SC-002/003)**: restart n1's peer-service (or sever the stream);
   confirm tiny reconnects and submit/sync resume with zero operator action.
7. **Routing (SC-004)**: with a second public anchor present, confirm
   `peer_path_selection_total{path=self}` is used by n1 (self-anchor), and a remote
   subscriber prefers lowest-RTT.

## C. Retirement check (SC-005 / US4)

- `Sorcha.PeerRouter` project is deleted; solution builds; no compose/service
  references it.
- Re-run A + B with no separate relay component deployed → all pass.

## Metrics to watch (`Sorcha.Peer` meter)

`peer_reverse_streams_active`, `peer_relay_forward_duration{flow}`,
`peer_path_selection_total{path}`, `peer_anchor_failover_total`,
`peer_anchor_reconnect_total`.

## Success-criteria mapping

| Step | Criterion |
|---|---|
| B.5 | SC-001 (gating; un-parks the demo) |
| B.6 | SC-002, SC-003 |
| B.7 | SC-004 |
| C | SC-005 |
| compare B.5 latency vs public-owner baseline | SC-006 |
