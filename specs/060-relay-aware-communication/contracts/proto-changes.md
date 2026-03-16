# Proto Contract Changes: Relay-Aware Peer Communication

**Phase 1 Output** | **Date**: 2026-03-16

## File: `src/Services/Sorcha.Peer.Service/Protos/peer_communication.proto`

### Change: Extend MessageType Enum

No new RPCs. No new services. No new proto files. Only 4 new enum values on the existing `MessageType`.

#### Current State (values 0-7)

```protobuf
enum MessageType {
  UNKNOWN = 0;
  TRANSACTION_NOTIFICATION = 1;
  TRANSACTION_REQUEST = 2;
  TRANSACTION_RESPONSE = 3;
  PEER_STATUS_UPDATE = 4;
  HEARTBEAT = 5;
  BLS_KEY_SHARE_DISTRIBUTION = 6;
  BLS_PARTIAL_SIGNATURE = 7;
}
```

#### Target State (values 0-11)

```protobuf
enum MessageType {
  UNKNOWN = 0;
  TRANSACTION_NOTIFICATION = 1;
  TRANSACTION_REQUEST = 2;
  TRANSACTION_RESPONSE = 3;
  PEER_STATUS_UPDATE = 4;
  HEARTBEAT = 5;
  BLS_KEY_SHARE_DISTRIBUTION = 6;
  BLS_PARTIAL_SIGNATURE = 7;
  // Relay register sync message types
  REGISTER_SYNC_REQUEST = 8;
  REGISTER_SYNC_RESPONSE = 9;
  TRANSACTION_DATA_REQUEST = 10;
  TRANSACTION_DATA_RESPONSE = 11;
}
```

### Impact Assessment

| Consumer | Impact | Action Required |
|----------|--------|-----------------|
| PeerRouter (`RouterCommunicationService`) | None | Router relays any `PeerMessage` regardless of `MessageType` value. Enum is in peer's proto, router just sees an int. |
| Peer Service (existing gRPC services) | None | Existing handlers only process their known types. Unknown types fall through to default case. |
| Peer Service (new `PeerCommunicationServiceImpl`) | New handler | Dispatches new message types to `RelayMessageHandler`. |
| Proto regeneration | Rebuild required | `dotnet build` will regenerate C# types from updated proto. |

### Wire Format

The proto `PeerMessage` is unchanged structurally:

```protobuf
message PeerMessage {
  string sender_peer_id = 1;
  string recipient_peer_id = 2;
  MessageType message_type = 3;
  bytes payload = 4;
  int64 timestamp = 5;
}
```

New message types use the `payload` field to carry JSON-serialized request/response POCOs (defined in `RelayMessages.cs`, documented in `data-model.md`). This means:

- No breaking wire format changes
- No proto version bump needed
- Backward compatible — old peers ignore unknown message types
- Forward compatible — new message types can be added without coordination

### New gRPC Service Implementation

A new server-side gRPC service implementation is needed (the Peer Service currently has no handler for `PeerCommunication.SendMessage` — only the PeerRouter implements it):

```
File: src/Services/Sorcha.Peer.Service/GrpcServices/PeerCommunicationServiceImpl.cs

Implements: PeerCommunication.PeerCommunicationBase.SendMessage(PeerMessage, ServerCallContext)
Registration: app.MapGrpcService<PeerCommunicationServiceImpl>() in Program.cs
```

This is documented in the design spec section 4.
