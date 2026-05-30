# Contract: internal peer-service interfaces

These are the C# seams introduced/extended inside `Sorcha.Peer.Service`. Signatures
are indicative; final names follow existing conventions.

## `ReverseStreamManager` (NEW singleton — port from PeerRouter)

```csharp
public sealed class ReverseStreamManager
{
    void RegisterStream(string peerId, IServerStreamWriter<PeerMessage> stream); // replaces prior, cancels old CTS
    bool TryGetStream(string peerId, out ReverseStreamEntry? entry);
    void RemoveStream(string peerId);
    Task DispatchAsync(string peerId, PeerMessage message, CancellationToken ct); // NEW: throws RpcException(Unavailable) if no live stream
    int  ActiveCount { get; }                                                     // NEW: gauge source
}
```

**Contract**: thread-safe; `RegisterStream` is idempotent-replace; `DispatchAsync`
never blocks indefinitely (write or fail). One entry per `peerId`.

## `RelayCommunicationService` (EXTEND single-seed → multi-anchor)

```csharp
// today: holds one reverse stream to a seed
Task EstablishReverseStreamAsync(CancellationToken ct);            // EXTEND: establish to the SET of public anchors
IReadOnlyCollection<Anchor> Anchors { get; }                       // NEW: current anchors + per-anchor RTT/state
Task SendViaRelayAsync(string targetPeerId, MessageType type, byte[] payload, CancellationToken ct); // existing, anchor-aware
Task<TResponse> SendAndWaitAsync<TResponse>(...);                  // existing, correlation-matched
```

**Contract**: maintains one reverse stream per configured/discovered public anchor;
per-anchor reconnect with backoff + heartbeat; recovers a dropped anchor without
operator action (FR-009); losing all anchors fails sends explicitly and recovers on
reconnect (FR-010).

## `CommunicationProtocolManager` (EXTEND path selection)

```csharp
RoutingPreference SelectPath(string targetPeerId);  // self-anchor → lowest-RTT remote anchor → next-best on breaker-open
```

**Contract**: order is (1) self-anchor direct write over local reverse stream, else
(2) lowest measured-RTT remote anchor, with `CircuitBreaker` failover to next-best
(FR-008). A NAT'd target is never selected via a direct address (invariant).
Re-evaluated per request; adapts as RTT/anchor-set change.

## Anchor advertisement ingest (EXTEND advert/heartbeat)

```csharp
// emit (NAT'd node): include live anchor set on its advert/heartbeat
// ingest (any node): update NodeRoutingTable[targetPeerId].Anchors
void OnAdvertReceived(PeerAdvert advert); // EXTEND to read AnchorAdvertisement; converge within one cycle
```

**Contract**: the propagated anchor set drives `SelectPath`; stale anchors are
pruned within one advert/heartbeat cycle so traffic is not routed to a dead path
beyond one refresh (spec edge case).

## Configuration

| Key | Meaning | Default |
|---|---|---|
| `PeerService:PublicAddress` | set ⇒ public/rendezvous-capable; empty ⇒ NAT'd/spoke | empty (existing) |
| `PeerService:SeedNodes` | public peers a NAT'd node dials out to (initial anchors) | empty (existing) |
| `PeerService:Relay:RendezvousEnabled` | accept inbound reverse `Stream` when public | derived from `PublicAddress` (default on for public) |
| `RegisterSync:RelayPollIntervalSeconds` | safety-net poll for missed relayed notifications | 20 (existing) |

## Observability (`Sorcha.Peer` meter — FR-012 / SC-004)

| Instrument | Type | Tags |
|---|---|---|
| `peer_reverse_streams_active` | gauge | `role=rendezvous` |
| `peer_relay_forward_duration` | histogram | `flow=submit\|sync` |
| `peer_path_selection_total` | counter | `path=self\|remote` |
| `peer_anchor_failover_total` | counter | — |
| `peer_anchor_reconnect_total` | counter | — |

OTel span `peer.relay.forward` around each brokered forward.
