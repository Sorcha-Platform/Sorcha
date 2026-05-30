# Contract: `PeerCommunication.Stream` rendezvous (server side)

The wire contract already exists in
`src/Services/Sorcha.Peer.Service/Protos/peer_communication.proto`:

```proto
service PeerCommunication {
  rpc SendMessage(PeerMessage) returns (MessageAck);          // exists (unary)
  rpc Stream(stream PeerMessage) returns (stream PeerMessage); // exists in proto, server NOT implemented in peer-service
}
message PeerMessage {
  string sender_peer_id    = 1;
  string recipient_peer_id = 2;
  MessageType message_type = 3;   // TRANSACTION_NOTIFICATION, REGISTER_SYNC_REQUEST/RESPONSE, TRANSACTION_DATA_REQUEST/RESPONSE, HEARTBEAT, ...
  bytes  payload           = 4;   // carries a CorrelationId for req/resp matching
  int64  timestamp         = 5;
}
```

**This feature adds the server side of `Stream` to peer-service.** No `.proto`
change is expected (message types already cover submit + sync). If a field is
needed to carry the advertised anchor set inside `HEARTBEAT`/advert payloads, it
is added to the *advert* model, not this proto.

## Server behaviour (rendezvous-capable peer)

Implemented in `PeerCommunicationServiceImpl.Stream`, ported from
`RouterCommunicationService.Stream`.

| Aspect | Contract |
|---|---|
| Gating | Only a peer with `PublicAddress` set (rendezvous-capable) accepts `Stream`. A NAT'd peer rejects inbound `Stream` (it is a client, not a hub). |
| First message | MUST carry `sender_peer_id`; the server registers the reverse stream under that id (`ReverseStreamManager.RegisterStream`). Missing id ⇒ `InvalidArgument`. |
| Reconnect | A new `Stream` for an existing `peer_id` supersedes the old (old `StreamCts` cancelled, replaced) — idempotent. |
| Inbound from peer | Messages with a `recipient_peer_id` are forwarded (`ForwardStreamMessageAsync`): to that recipient's reverse stream if present, else direct channel if the recipient has an address, else `Unavailable`. |
| Outbound to peer | The server pushes brokered submit/sync requests to the NAT'd peer by writing to its `ResponseStream` (`DispatchAsync`); the peer's `RelayMessageHandler` services them and replies with a correlated response message. |
| Liveness | `LastActivityAt` updated per message; heartbeat (~30s) keeps the stream warm; disconnect ⇒ `RemoveStream`. |
| Cancellation | Client disconnect / `OperationCanceledException` / `StatusCode.Cancelled` ⇒ clean teardown in `finally`. |

## Brokered flows over the reverse stream

| Flow | Message types | Direction over stream |
|---|---|---|
| Submit to NAT'd owner | `TRANSACTION_NOTIFICATION` | rendezvous → owner |
| Sync: docket request | `REGISTER_SYNC_REQUEST` → `REGISTER_SYNC_RESPONSE` | rendezvous → owner → rendezvous |
| Sync: tx data | `TRANSACTION_DATA_REQUEST` → `TRANSACTION_DATA_RESPONSE` | rendezvous → owner → rendezvous |
| Owner self fan-out of sealed dockets | `TRANSACTION_NOTIFICATION` (owner-initiated) | owner → rendezvous → subscribers |

## Error contract

- No active reverse stream for target + no direct address ⇒ `Unavailable` (caller
  fails over to next-best anchor; never hangs — FR-010).
- Unknown/uninitialised `sender_peer_id` on first message ⇒ `InvalidArgument`.
- Rendezvous disabled on a NAT'd-only node ⇒ `FailedPrecondition`.
