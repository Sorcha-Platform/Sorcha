# Contract: Reverse Stream Relay

**Feature**: 071-p2p-register-sync | **Date**: 2026-03-28

## gRPC Contract (existing proto — no changes)

Uses existing `PeerCommunication.Stream` bidirectional RPC from `peer_communication.proto`:

```protobuf
service PeerCommunication {
  rpc SendMessage(PeerMessage) returns (MessageAck);            // Existing unary relay
  rpc Stream(stream PeerMessage) returns (stream PeerMessage);  // Upgrade from UNIMPLEMENTED
}
```

## Router-Side Behavior (RouterCommunicationService)

### Stream RPC — New Implementation

**Lifecycle**:
1. NAT'd peer initiates `Stream()` call to Router
2. Router registers the peer's `IServerStreamWriter<PeerMessage>` in a concurrent dictionary keyed by peer ID
3. Router reads incoming messages from the peer's request stream
4. For each incoming message: Router looks up the `recipient_peer_id` and either:
   - Pushes to recipient's active reverse stream (if NAT'd with active stream)
   - Creates direct gRPC channel to recipient (if recipient has reachable address)
   - Returns error to sender if recipient is unreachable
5. Router can also push incoming messages FROM other peers down to this peer's response stream
6. On stream completion/error: Router removes the stream entry

### SendMessage RPC — Modified Behavior

**Change**: When recipient has no reachable address, check for active reverse stream before failing.

Current: `recipient.Address` empty → return NOT_FOUND
New: `recipient.Address` empty → check `_reverseStreams[recipientPeerId]` → push via stream → return OK

## Peer-Side Behavior (RelayCommunicationService)

### New: EstablishReverseStreamAsync

1. Get seed node channel from `PeerConnectionPool`
2. Call `client.Stream()` to initiate bidirectional stream
3. Start background receive loop reading from `responseStream`
4. Dispatch received messages to `RelayMessageHandler`
5. On disconnect: exponential backoff reconnect (2s → 4s → 8s → 16s → max 60s)
6. Keepalive: send ping message every 30 seconds

### Modified: SendViaRelayAsync

Current: Always sends via `SendMessage` unary RPC
New: If reverse stream to seed is active, send via stream. Fall back to `SendMessage` if stream unavailable.

## Message Flow: Peer A → Router → Peer B (both NAT'd)

```
Peer A                    PeerRouter                    Peer B
  │                           │                            │
  │─── Stream() ─────────────▶│◀──────────── Stream() ────│
  │    (reverse stream)       │         (reverse stream)   │
  │                           │                            │
  │── PeerMessage ───────────▶│                            │
  │   (recipient=PeerB)       │── PeerMessage ────────────▶│
  │                           │   (via B's reverse stream) │
  │                           │                            │
  │                           │◀── PeerMessage ────────────│
  │◀── PeerMessage ───────────│   (recipient=PeerA)        │
  │   (via A's reverse stream)│                            │
```
