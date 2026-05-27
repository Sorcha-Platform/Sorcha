# Contract: Peer Protocol Changes (US2)

**Service**: `Sorcha.Peer.Service` | **Transport**: gRPC (HTTP/2)

## Proto changes

### `peer_communication.proto`
```protobuf
message RegisterPeerRequest {
  PeerInfo peer_info       = 1;
  bytes    public_key      = 2;  // NEW: ED25519 public key (node identity)
  int64    timestamp       = 3;  // NEW: unix seconds, freshness-checked
  bytes    challenge_nonce = 4;  // NEW: server-issued nonce echoed back
  bytes    signature       = 5;  // NEW: sign(public_key) over
                                 //   peer_id ‖ address ‖ port ‖ timestamp ‖ challenge_nonce
}

// NEW pre-step so the server controls the nonce:
rpc RequestChallenge(ChallengeRequest) returns (ChallengeResponse);
message ChallengeRequest  { string claimed_peer_id = 1; }
message ChallengeResponse { bytes challenge_nonce = 1; int64 expires_at = 2; }
```

### `peer_heartbeat.proto`
```protobuf
message RegisterAdvertisement {
  // existing fields 1-7 ...
  bytes signature = 8;  // NEW: sign over register_id ‖ latest_version ‖ latest_docket_version
}

message PeerHeartbeatRequest {
  // existing: sequence_number (3), timestamp (4) — now VALIDATED
  bytes signature = N;  // NEW: sign over heartbeat body (peer_id ‖ sequence_number ‖ timestamp)
}
```

## Behavioral contract

| RPC | Precondition | Postcondition / rejection |
|-----|--------------|---------------------------|
| `RequestChallenge` | claimed peer id non-empty | returns single-use nonce with short TTL |
| `RegisterPeer` | valid signature over the challenge by `public_key`; `peer_id` == thumbprint(public_key); timestamp within skew | accepted ⇒ `PeerNode` stores `PublicKey`. Invalid signature / id mismatch / stale timestamp / unknown-or-expired challenge ⇒ **refused** (`Success=false`). Rate-limited per source. |
| `SendHeartbeat` / `StreamHeartbeat` | `sequence_number > PeerNode.LastHeartbeatSequenceNumber`; timestamp advancing within skew; valid body signature by stored `PublicKey` | accepted ⇒ update last seq/timestamp. Non-advancing seq / stale timestamp / bad-or-missing signature ⇒ **rejected** (replay). |
| Advertisement processing | advertisement signature valid by the originating node's stored `PublicKey` | unsigned / bad-signature ⇒ **dropped, not propagated**. |

## Transport contract

- **Development**: cleartext HTTP/2 permitted (current behavior).
- **Production / Staging**: mTLS **required**; unauthenticated/unencrypted peer transport is **refused at startup/connection** (fail-closed). `PeerAuthInterceptor` MUST NOT silently treat missing auth as anonymous outside Development.

## Node identity contract (`NodeIdentityService`)

- On first startup, generate an ED25519 keypair via `CryptoModule`; persist private key encrypted (Key Protection Provider) in `PeerDbContext`; reuse across restarts.
- `NodeId` = thumbprint(public key). Exported in registration and heartbeats.
