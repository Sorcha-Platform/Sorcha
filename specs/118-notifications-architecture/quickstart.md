# Quickstart — Notifications & Realtime Architecture

**Date**: 2026-05-05
**Spec**: [spec.md](spec.md)

End-to-end verification that the feature works against a fresh Docker host. All commands run from the repo root unless noted.

---

## Prerequisites

- Docker Desktop running
- .NET 10 SDK
- `docker-compose.yml` from the repo (Postgres + Redis already wired)
- A clean install per `docs/getting-started/quickstart.md`

## 1. Spin up the stack

```bash
docker-compose up -d
docker-compose logs -f tenant-service blueprint-service wallet-service register-service api-gateway
```

Verify every service starts and reports the storage registration log includes a hub backplane entry:

```
[STORAGE-LOG] Persistent: Microsoft.AspNetCore.SignalR.IHubContext (backplane=redis, prefix=sorcha:signalr:tenant)
[STORAGE-LOG] Persistent: Microsoft.AspNetCore.SignalR.IHubContext (backplane=redis, prefix=sorcha:signalr:blueprint)
[STORAGE-LOG] Persistent: Microsoft.AspNetCore.SignalR.IHubContext (backplane=redis, prefix=sorcha:signalr:wallet)
[STORAGE-LOG] Persistent: Microsoft.AspNetCore.SignalR.IHubContext (backplane=redis, prefix=sorcha:signalr:register)
[STORAGE-LOG] Persistent: Sorcha.Tenant.Service.Services.IInboxStore (postgres)
```

## 2. Sign in and obtain a JWT

Use the existing dev credentials:

```bash
JWT=$(curl -s -X POST http://localhost/api/auth/login \
    -H "Content-Type: application/json" \
    -d '{"email":"admin@sorcha.local","password":"Dev_Pass_2025!"}' \
    | jq -r .accessToken)
echo "$JWT" | head -c 60
```

## 3. Verify hub topology

Five hub routes should respond. Browser via the gateway:

```bash
curl -i -H "Authorization: Bearer $JWT" \
    -H "Connection: Upgrade" -H "Upgrade: websocket" \
    "http://localhost/hubs/tenant/negotiate?negotiateVersion=1"
# expect 200 with connectionToken JSON

curl -i -H "Authorization: Bearer $JWT" \
    "http://localhost/hubs/blueprint/negotiate?negotiateVersion=1"
# expect 200

curl -i -H "Authorization: Bearer $JWT" \
    "http://localhost/hubs/wallet/negotiate?negotiateVersion=1"
# expect 200

curl -i -H "Authorization: Bearer $JWT" \
    "http://localhost/hubs/register/negotiate?negotiateVersion=1"
# expect 200 (after FR-011 [Authorize] cutover) or 200 anonymous (before)

curl -i -H "Authorization: Bearer $JWT" \
    "http://localhost/hubs/chat/negotiate?negotiateVersion=1"
# expect 200
```

Negotiate without auth — TenantHub, BlueprintHub, WalletHub MUST reject:

```bash
curl -s -o /dev/null -w "%{http_code}\n" \
    "http://localhost/hubs/tenant/negotiate?negotiateVersion=1"
# expect 401
```

Deprecated route alias still works during the deprecation window:

```bash
curl -i -H "Authorization: Bearer $JWT" \
    "http://localhost/actionshub/negotiate?negotiateVersion=1"
# expect 200 with Deprecation header set
```

After deprecation window closes:

```bash
curl -i -H "Authorization: Bearer $JWT" \
    "http://localhost/actionshub/negotiate?negotiateVersion=1"
# expect 410 with JSON body naming /hubs/blueprint as replacement
```

## 4. Inbox round-trip

Post an internal inbox entry as a service principal (using a service token from the auth flow used by Wallet → Tenant):

```bash
SERVICE_JWT=$(./scripts/issue-service-token.ps1 -Service blueprint)
PLATFORM_USER_ID="<dev-user-guid>"

curl -i -X POST http://localhost/api/internal/inbox \
    -H "Authorization: Bearer $SERVICE_JWT" \
    -H "Content-Type: application/json" \
    -d "{
        \"platformUserId\": \"$PLATFORM_USER_ID\",
        \"category\": \"Action\",
        \"severity\": \"ActionRequired\",
        \"correlationKey\": \"tx:test-wallet-1:tx-aaa\",
        \"detailHref\": \"/api/instances/00000000-0000-0000-0000-000000000001/actions/00000000-0000-0000-0000-000000000002\",
        \"sourceEventId\": \"$(uuidgen)\",
        \"occurredAt\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
        \"title\": \"Action required\",
        \"summary\": \"You have a new action to acknowledge.\"
    }"
# expect 201 Created, JSON body with new entry Id
```

Read the inbox as the user:

```bash
curl -s -H "Authorization: Bearer $JWT" \
    "http://localhost/api/me/inbox?pageSize=20" | jq '.entries[0]'
# expect the new entry, ReadAt: null

curl -s -H "Authorization: Bearer $JWT" \
    "http://localhost/api/me/inbox/unread-count" | jq .
# expect { "unread": 1 }
```

Mark read:

```bash
curl -s -X POST -H "Authorization: Bearer $JWT" \
    "http://localhost/api/me/inbox/$ENTRY_ID/read"
# expect 204

curl -s -H "Authorization: Bearer $JWT" \
    "http://localhost/api/me/inbox/unread-count" | jq .
# expect { "unread": 0 }
```

Idempotency check — re-POST the same entry:

```bash
curl -i -X POST http://localhost/api/internal/inbox \
    -H "Authorization: Bearer $SERVICE_JWT" \
    -H "Content-Type: application/json" \
    -d "$SAME_BODY_AS_BEFORE"
# expect 200 OK with body { "id": "<original-id>", "idempotent": true }
```

## 5. Realtime hub-event verification

Open the dev UI in a browser at http://localhost/app, sign in, navigate to MyActions. Open browser DevTools → Network → WS, confirm `wss://localhost/hubs/blueprint?...` and `wss://localhost/hubs/tenant?...` are open and exchanging frames.

In a second terminal, post an inbox entry as service. The browser's `MainLayout` notification bell badge should increment within 300 ms (NFR-002). Click the bell — `PendingActionInbox` opens with the new entry. Refresh the page — entry persists (durable inbox).

## 6. Multi-node correctness verification

Bring up two Tenant Service replicas via the multinode compose:

```bash
docker-compose -f docker-compose.yml -f docker-compose.multinode.yml up -d
docker-compose ps
# expect tenant-service-1 and tenant-service-2 both healthy
```

Run the cross-replica test:

```bash
dotnet test tests/Sorcha.Integration.Tests/MultiNode/HubBackplaneCrossReplicaTests.cs --logger console
# all green
```

Manual verification: open two browser windows; YARP routes one to replica 1 (sticky cookie) and the other to replica 2. Trigger an inbox write that targets the shared user. Both windows receive the bell update within 200 ms (NFR-001).

## 7. Thin-signal contract verification

Subscribe to the Redis backplane as an external observer:

```bash
docker exec -it sorcha-redis redis-cli
> PSUBSCRIBE sorcha:signalr:*
```

Trigger a workflow event. Inspect the published messages — every payload conforms to `{ "EventType": "...", "Ids": [...], "OccurredAt": "...", "TraceId": "..." }`. No claim values, no descriptions, no balances.

## 8. Polling fallback verification

In the browser, open `Pages/Wallets/WalletDetail` for a wallet with active operations. Open DevTools → Network → block all WebSocket connections. Within 20 s, the page begins polling its REST refresh endpoint (visible in Network tab as periodic GETs to `/api/wallets/{addr}`). Restore WebSockets — polling stops; realtime resumes. No console errors, no error toast.

## 9. Group-name builder enforcement

```bash
./scripts/check-no-inline-group-strings.ps1
# expect "OK: zero inline group-string literals found in production code"
```

## 10. Storage audit verification

```bash
curl -s http://localhost:5110/health/storage-providers | jq .
# expect status: "Healthy"
# entry list includes: Sorcha.Tenant.Service.Services.IInboxStore (Persistent, postgres)
# entry list includes: Microsoft.AspNetCore.SignalR.IHubContext (Persistent, redis)
```

Set `Storage:AllowInMemoryInProduction=false` and bring the Tenant Service up without `ConnectionStrings:Sorcha:Postgres`:

```bash
ASPNETCORE_ENVIRONMENT=Production \
docker-compose up tenant-service
# expect immediate exit with [STORAGE-FAIL-FAST] message
```

## 11. Decommission window verification (after EventsHub retired)

```bash
curl -i "http://localhost/hubs/events/negotiate?negotiateVersion=1"
# expect 410 with body:
# { "error": "events_hub_retired",
#   "replacement": ["TenantHub", "BlueprintHub", "WalletHub"],
#   "deprecation_date": "2026-05-19",
#   "guidance": "/specs/118-notifications-architecture/spec.md#user-story-2" }
```

```bash
curl -s "http://localhost/metrics" | grep sorcha_signalr_events_hub_subscribers
# expect zero or absent (instrument removed after final decommission)
```

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `[STORAGE-FAIL-FAST]` at boot | No Redis connection string | Set `ConnectionStrings:Sorcha:Redis` or `ConnectionStrings:{Service}:Redis` |
| Hub negotiate returns 401 with valid JWT | Token missing `platform_user_id` claim | Re-issue token via `/api/auth/login`; check claim issuance in Tenant Service |
| Inbox POST returns 403 | Service token missing `RequireService` policy claim | Use `./scripts/issue-service-token.ps1 -Service <name>` not a user JWT |
| Two browser tabs see different unread counts | Backplane Redis unreachable from one replica | Check `sorcha_signalr_backplane_state` gauge per replica |
| `OnTransactionReceipted` no longer fires on WalletDetail page | UI not yet migrated to WalletHub for tx receipt events | Confirm migration phase 3 has shipped; check `WalletHubConnection` is wired |
| Inline group-string check fails | New code constructed `$"wallet:{addr}"` directly | Replace with `WalletHubGroups.Wallet(addr)` |
| Multi-node test passes locally but fails in CI | Sticky-session cookie not honoured by CI YARP | Verify `docker-compose.multinode.yml` has the YARP affinity rule |
