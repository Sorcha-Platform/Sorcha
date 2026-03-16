# Quickstart: Relay-Aware Peer Communication

**Phase 1 Output** | **Date**: 2026-03-16

## Implementation Order

The feature has a natural dependency chain. Implement in this order:

### Phase A: Foundation (no behavioral changes)

1. **Proto changes** — Add 4 `MessageType` values to `peer_communication.proto`
2. **Relay payload POCOs** — Create `RelayMessages.cs` with all request/response models
3. **RelayCommunicationService** — Core relay primitive (send-and-forget + request/response correlation)
4. **DI registration** — Register `RelayCommunicationService` as singleton in `Program.cs`

**Test gate**: Unit tests for `RelayCommunicationService` — mock seed channel, verify message forwarding, verify correlation timeout.

### Phase B: Sending Side (outbound relay)

5. **CommunicationProtocolManager** — Add relay fallback when `peer.Address` is empty (check BEFORE protocol chain)
6. **TransactionDistributionService** — Add relay fallback in `SendToPeerAsync`

**Test gate**: Unit tests verify relay triggered for empty-address peers, direct path used for peers with addresses.

### Phase C: Receiving Side (inbound relay)

7. **PeerCommunicationServiceImpl** — NEW gRPC service to handle incoming `SendMessage` calls
8. **RelayMessageHandler** — Dispatch incoming relay messages to appropriate handlers
9. **Register in Program.cs** — `app.MapGrpcService<PeerCommunicationServiceImpl>()`

**Test gate**: Unit tests for `RelayMessageHandler` — verify dispatch for each message type, verify responses sent back via relay.

### Phase D: Register Sync via Relay

10. **RegisterReplicationService** — Add relay batch sync path in `PullFullReplicaAsync`
11. **RegisterSyncBackgroundService** — Add periodic relay poll timer + per-register semaphores
12. **PeerServiceConfiguration** — Add `RelayPollIntervalSeconds` to `RegisterSyncConfiguration`

**Test gate**: Unit tests for relay batch sync loop, batch processing, peer fallback on failure.

### Phase E: Integration Testing

13. **Integration test** — Two peers + seed node relay, end-to-end register sync verification

## Key Integration Points

### Where relay hooks into existing code

| Existing Class | Method | Change |
|----------------|--------|--------|
| `CommunicationProtocolManager` | `SendMessageAsync` | Add `if (string.IsNullOrEmpty(peer.Address))` check before protocol chain |
| `TransactionDistributionService` | `SendToPeerAsync` | Add same address check before direct gRPC call |
| `RegisterReplicationService` | `PullFullReplicaAsync` | Add `else if (string.IsNullOrEmpty(sourcePeer.Address))` after channel-null check |
| `RegisterSyncBackgroundService` | `ExecuteAsync` | Add second `PeriodicTimer` loop for relay polling |
| `Program.cs` | Service registration | Add `RelayCommunicationService`, `RelayMessageHandler`, `PeerCommunicationServiceImpl` |

### Constructor dependency additions

| Class | New Dependency |
|-------|---------------|
| `CommunicationProtocolManager` | `RelayCommunicationService` |
| `TransactionDistributionService` | `RelayCommunicationService` |
| `RegisterReplicationService` | `RelayCommunicationService` |
| `RelayMessageHandler` | `RelayCommunicationService`, `RegisterCache`, `RegisterSyncBackgroundService` |
| `PeerCommunicationServiceImpl` | `RelayMessageHandler` |

## Configuration

Add to `appsettings.json` under `PeerService:RegisterSync`:

```json
{
  "PeerService": {
    "RegisterSync": {
      "RelayPollIntervalSeconds": 60
    }
  }
}
```

No other configuration changes needed. Relay is automatic when `peer.Address` is empty.

## Verification Checklist

After implementation, verify:

- [ ] NAT'd peer (empty address) messages route through relay
- [ ] Peers with addresses still use direct connection
- [ ] No seed node connected → relay returns failure gracefully
- [ ] Transaction notifications reach NAT'd peers via relay
- [ ] Register sync works between NAT'd peers via relay batches
- [ ] Periodic poll catches missed notifications
- [ ] Per-register semaphore prevents concurrent syncs
- [ ] SenderPeerId is populated on all relay messages
- [ ] Response size stays within message limits (50-docket batches)
- [ ] Correlation timeout cleans up pending entries
