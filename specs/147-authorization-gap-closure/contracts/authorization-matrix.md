# Contract: Authorization matrix (per operation)

No new HTTP endpoints. This is the behavioural contract — the allow/deny decision each in-scope operation MUST enforce after this feature. "Allow" means authorization passes (the request proceeds to the handler); "Deny" means an authorization failure (401 if unauthenticated, 403 if authenticated-but-unauthorized).

Caller classes: **Anon** (no token), **Consumer** (`:consumer`, carries `org_id`, no roles), **Platform** (`:platform`, carries `org_id`), **Platform-Admin** (`:platform` + `Administrator`/`SystemAdmin` role), **Service** (`token_type==service` + `:service`).

## H1 — `POST /api/v1/wallets/system` (create)  — policy `RequireService`

| Caller | Result |
|--------|--------|
| Anon | **Deny** (401) |
| Consumer | **Deny** (403) |
| Platform | **Deny** (403) |
| Platform-Admin | **Deny** (403) |
| Service | **Allow** |

## H1 — `POST /api/v1/wallets/system/recover` (recover) — policy `CanRecoverSystemWallet`

| Caller | Result |
|--------|--------|
| Anon | **Deny** (401) |
| Consumer | **Deny** (403) |
| Platform (no admin role) | **Deny** (403) |
| Platform-Admin | **Allow** |
| Service | **Allow** |
| Admin role but `:consumer` audience | **Deny** (403) |
| any caller, when an active system wallet already exists | **Deny** (409 — existing guard, after authz passes) |

## H2 — Blueprint authoring (CRUD `/api/blueprints`, `SchemaEndpoints`, `CredentialEndpoints`, `StatusListEndpoints`) — policy `CanManageBlueprints`

| Caller | Result |
|--------|--------|
| Anon | **Deny** (401) |
| Consumer (carries `org_id`) | **Deny** (403) — the gap being closed |
| Platform (carries `org_id`) | **Allow** |
| Platform (no `org_id`) | **Deny** (403) |
| Service | **Allow** |

Sibling endpoints already composing `+RequirePlatformAudience` (`RehearsalEndpoints`, `BlueprintFromPublishedEndpoint`): unchanged — Platform allowed, Service denied there (platform-only), Consumer denied.

## F124 — `/api/v1/wallet/pending-applications` (GET/PUT/DELETE) — policy `RequireConsumerAudience`

| Caller | Result |
|--------|--------|
| Anon | **Deny** (401) |
| Consumer | **Allow** (scoped to own PlatformUser) |
| Platform | **Deny** (403) — the gap being closed |
| Service | **Deny** (403) |

## LOW — Tenant platform-administration (`PlatformManagementEndpoints`, `PlatformOrgEndpoints`, `PlatformSettingsEndpoints`) — policy `RequireSystemAdmin` (+ `RequirePlatformAudience`, already composed)

| Caller | Result |
|--------|--------|
| SystemAdmin in system-admin-org (`…0001`) + `:platform` | **Allow** |
| SystemAdmin in a **non-system** org | **Deny** (403) — the gap being closed |
| Non-SystemAdmin in system-admin-org | **Deny** (403) |

## Cross-cutting

- Every "Deny" above holds whether the request arrives via the gateway **or** directly on the internal network (authorization is enforced at the operation, not the perimeter — FR-011 / SC-005).
- All audience comparisons resolve through `SorchaAudiences` for the configured installation (FR-012); a token from another installation's namespace is denied.
