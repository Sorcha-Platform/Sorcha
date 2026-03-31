# Quickstart: 078 — Register Sync Status Lifecycle & UI Improvements

## What This Feature Does

Maps the peer-to-peer register sync lifecycle to user-visible register statuses, adds real-time table updates, encryption warnings, and immediate sync triggers.

## Key Changes

### Backend (5 touch points)
1. **Peer Service** — `RegisterSyncBackgroundService`: Immediate sync trigger on new subscription
2. **Peer Service** — Status reporting: Notify Register Service when sync state changes
3. **Register Service** — Subscription handler: Map sync state to RegisterStatus
4. **Register Service** — New endpoint: `POST /registers/{id}/disable-dev-mode` (one-way)
5. **Register Service** — Offline debounce: 30s grace before marking Offline

### Frontend (4 touch points)
1. **Register List** (`Index.razor`): Placeholder for subscribed-but-not-synced registers with Recovery status
2. **Register List** (`RegisterCard.razor`): Warning icon for DevMode=true registers
3. **Register Detail** (`Detail.razor`): Remove notification boxes, auto-prepend table rows
4. **Governance Tab** (`RegisterPolicyTab.razor`): One-way encryption enable switch

## Testing Approach

```bash
# 1. Subscribe to a remote register, verify status = Checking immediately
# 2. Watch status transition: Checking → Recovery → Online
# 3. Kill source peer, verify status → Offline after 30s
# 4. Restart source peer, verify status → Checking → Recovery → Online
# 5. Submit transaction on source, verify it appears in detail table without refresh
# 6. View DevMode register, verify warning icon
# 7. Enable encryption on governance tab, verify one-way lock
```

## Files to Modify

| File | Change |
|------|--------|
| `Sorcha.Peer.Service/Replication/RegisterSyncBackgroundService.cs` | Immediate sync signal |
| `Sorcha.Peer.Service/Replication/RegisterSyncBackgroundService.cs` | Status reporting to Register Service |
| `Sorcha.Register.Service/Program.cs` | Status mapping in subscription handler |
| `Sorcha.Register.Service/Program.cs` | New disable-dev-mode endpoint |
| `Sorcha.Register.Core/Managers/RegisterManager.cs` | Prevent DevMode re-enable |
| `Sorcha.UI.Web.Client/Pages/Registers/Index.razor` | Placeholder entries, warning icons |
| `Sorcha.UI.Web.Client/Pages/Registers/Detail.razor` | Remove notification boxes, auto-update tables |
| `Sorcha.UI.Core/Components/Registers/RegisterCard.razor` | DevMode warning icon |
| `Sorcha.UI.Core/Components/Registers/RegisterPolicyTab.razor` | Encryption enable switch |
