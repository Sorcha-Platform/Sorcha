# SEC-011: Service-to-Service Auth Gap Closure

**Date:** 2026-04-03
**Status:** Approved
**Scope:** Close 5 open internal endpoints with `RequireService` policy; defer per-service identity to SEC-011b

---

## Problem

Five internal endpoints across Register Service and Peer Service accept unauthenticated requests. While Docker network isolation provides a degree of protection, any process that can reach the container network can call these endpoints — creating risks for topology disclosure, unauthorized subscription manipulation, and sync state tampering.

The S2S auth infrastructure already exists (`ServiceAuthClient`, `RequireService` policy, `SetAuthHeaderAsync` helper) but was not applied to these endpoints during initial development.

## Endpoints to Secure

| # | Endpoint | Service | File | Current Auth |
|---|---|---|---|---|
| 1 | `GET /api/internal/registers` | Register | `Program.cs:249` | AllowAnonymous |
| 2 | `POST /api/internal/register-subscriptions` | Register | `Program.cs:261` | AllowAnonymous |
| 3 | `POST /api/internal/register-sync-status` | Register | `Program.cs:411` | AllowAnonymous |
| 4 | `POST /api/registers/{id}/subscribe` | Peer | `Program.cs:659` | None |
| 5 | `DELETE /api/registers/{id}/subscribe` | Peer | `Program.cs:693` | None |

**Target state:** All five endpoints use `.RequireAuthorization("RequireService")`, which requires `token_type=service` in the JWT.

## Callers to Verify

Each calling service client must call `SetAuthHeaderAsync()` before making requests to these endpoints. Verify and fix if missing:

| Client Class | Method | Calls Endpoint |
|---|---|---|
| `RegisterServiceClient` | `NotifySubscriptionAsync` | POST /api/internal/register-subscriptions |
| `RegisterServiceClient` | `ReportSyncStatusAsync` | POST /api/internal/register-sync-status |
| `RegisterServiceClient` | `GetInternalRegistersAsync` | GET /api/internal/registers |
| `PeerServiceClient` | `SubscribeToRegisterAsync` | POST /api/registers/{id}/subscribe |
| `PeerServiceClient` | `UnsubscribeFromRegisterAsync` | DELETE /api/registers/{id}/subscribe |

## Service Principal Configuration

Services that make S2S calls need `ServiceAuth:ClientId` and `ServiceAuth:ClientSecret` configured. Verify docker-compose.yml provides these environment variables for:

- **Register Service** — calls Peer Service (subscribe/unsubscribe)
- **Peer Service** — calls Register Service (sync status)
- **Tenant Service** — calls Register Service (subscription notifications)
- **Blueprint Service** — calls Register Service (internal register discovery)

If any service lacks a service principal, create one via the Tenant Service admin API or seed script.

## Implementation Steps

1. **Register Service Program.cs** — replace `AllowAnonymous()` with `RequireAuthorization("RequireService")` on endpoints 1-3. Remove `TODO(SEC-011)` comments.
2. **Peer Service Program.cs** — add `RequireAuthorization("RequireService")` to endpoints 4-5. Remove `TODO(SEC-011)` comments.
3. **Service clients** — audit each caller method listed above. Add `SetAuthHeaderAsync()` where missing.
4. **Docker-compose.yml** — ensure `ServiceAuth__ClientId` and `ServiceAuth__ClientSecret` env vars are set for all services that make S2S calls. Use shared `x-service-auth-env` anchor if not already present.
5. **Service principal seeding** — verify that service principals exist for each calling service. Add to seed script if missing.
6. **Tests** — for each secured endpoint, add integration test verifying:
   - 401 when no token is provided
   - 401 when a user token is provided (wrong token_type)
   - 200 when a valid service token is provided

## Testing Strategy

- Unit tests: mock `RequireService` policy, verify endpoints reject unauthenticated calls
- Integration tests (WebApplicationFactory): verify full auth pipeline with real JWT validation
- Docker smoke test: `docker compose up`, confirm services can still communicate after auth is enforced

## Deferred: SEC-011b — Defence-in-Depth

The following enhancements provide per-service identity granularity. They should be implemented before production but are not required for the current gap closure:

### Per-Service Identity Policies
Check the `service_name` claim to restrict which service can call which endpoint:

```csharp
// Example: only peer-service can report sync status
options.AddPolicy("RequirePeerService", policy =>
    policy.RequireClaim("token_type", "service")
          .RequireClaim("service_name", "peer-service"));
```

**Endpoint → Allowed Caller mapping:**

| Endpoint | Allowed Service(s) |
|---|---|
| `GET /api/internal/registers` | blueprint-service, peer-service |
| `POST /api/internal/register-subscriptions` | tenant-service |
| `POST /api/internal/register-sync-status` | peer-service |
| `POST /api/registers/{id}/subscribe` | register-service |
| `DELETE /api/registers/{id}/subscribe` | register-service |

### Scope Enforcement
Validate the `scope` claim matches the operation. The `CanWriteDockets` policy already has a placeholder for this. Extend to all S2S operations.

### API Gateway Internal Route Blocking
Explicitly deny `/api/internal/*` in YARP configuration as belt-and-suspenders — even though the gateway currently doesn't proxy these paths, an explicit deny prevents accidental exposure if routes are reconfigured.

### Audit Logging
Log all S2S auth failures (401/403) with the calling service name and target endpoint for security monitoring.
