# Research: System Administration Tooling (Feature 049)

**Date**: 2026-03-05
**Branch**: `049-system-admin-tooling`

## Research Summary

All backend endpoints exist and are functional. This is a pure frontend/CLI feature — no backend changes needed (with one YARP routing exception).

---

## Decision 1: YARP Admin Validators Route

**Decision**: Add a new YARP route for `/api/admin/validators/{**catch-all}` proxying to the Validator Service.

**Rationale**: The Validator Service exposes admin endpoints at `/api/admin/validators/*` (start, stop, status, process, monitoring), but the API Gateway has no matching YARP route. All other Feature 049 endpoints already have YARP routes configured.

**Alternatives considered**:
- Route through existing `/api/validator/{**}` catch-all → Rejected because that route transforms to `/api/v1/{**}`, which doesn't match the backend's `/api/admin/` prefix.
- Call Validator Service directly from UI → Rejected because all traffic should flow through the API Gateway for consistent auth and observability.

**Impact**: 1 route addition in `appsettings.json` + `reverseProxy.json`. Must use `RequireAdministrator` auth policy.

---

## Decision 2: UI Service Layer Architecture

**Decision**: Create 2 new UI service interfaces + extend 2 existing ones.

| Interface | Action | Methods |
|-----------|--------|---------|
| `IValidatorAdminService` | Extend | +14 methods (consent, metrics, threshold, config) |
| `IRegisterService` | Extend | +3 methods (policy get/history/update) |
| `ISystemRegisterService` | New | 4 methods (status, blueprints list/get/version) |
| `IServicePrincipalService` | New | 8 methods (full CRUD + lifecycle) |

**Rationale**: Follows existing pattern where each admin domain has a dedicated service interface in `Sorcha.UI.Core/Services/`. Implementations use `HttpClient` injected via DI.

**Alternatives considered**:
- Single mega-interface `IAdminService` → Rejected for violating Interface Segregation Principle and making the DI registration confusing.

---

## Decision 3: CLI Command Structure

**Decision**: Add subcommand groups to existing `register` and `validator` parent commands.

```
register → policy → get | history | update
         → system → status | blueprints

validator → consent → pending | approve | reject | refresh
          → metrics → [default] | validation | consensus | pools | caches | config
          → threshold → status | setup
```

**Rationale**: Follows existing CLI pattern (e.g., `register list`, `validator status`). Nested subcommands use the same `Command` class hierarchy.

**Alternatives considered**:
- Flat commands (`register-policy-get`) → Rejected; doesn't match existing structure.
- New top-level `admin` command → Rejected; `admin` already exists for health/alerts.

---

## Decision 4: CLI Refit Interfaces

**Decision**: Extend existing CLI Refit interfaces rather than creating new ones.

| Interface | Action | New Methods |
|-----------|--------|-------------|
| `IRegisterServiceClient` | Extend | +4 (policy get/history/update, system register) |
| `IValidatorServiceClient` | Extend | +14 (admin, metrics, consent, threshold) |
| `ITenantServiceClient` | Extend | +8 (service principal CRUD) |

**Rationale**: CLI already has `HttpClientFactory` methods for each service. Adding methods to existing interfaces avoids new factory methods and keeps the architecture flat.

---

## Decision 5: Validator Page Tab Layout

**Decision**: Extend `ValidatorPanel` to 5 tabs using `MudTabs`.

```
[ Mempool (existing) | Consent Queue | Metrics | Threshold | Config ]
```

Each tab is a separate component:
- `ValidatorMempoolTab` — refactored from existing `ValidatorPanel` content
- `ValidatorConsentPanel` — new
- `ValidatorMetricsPanel` — new
- `ValidatorThresholdPanel` — new
- `ValidatorConfigPanel` — new

**Rationale**: Follows existing tab patterns (Register Detail uses `MudTabs` with `MudTabPanel`). Separate components keep each tab's state isolated.

---

## Decision 6: Register Policy — Two Locations

**Decision**: Policy UI appears in 2 places:
1. **Create Register Wizard**: New `RegisterPolicyStep` inserted as step 2 (between "Name" and "Wallet")
2. **Register Detail Page**: New `RegisterPolicyTab` as 3rd tab (after Transactions, Docket Chain)

**Rationale**: Policy should be configurable at creation time (with defaults) and viewable/updatable on existing registers. The wizard currently has 4 steps (Name → Wallet → Options → Create); Policy inserts logically after naming.

---

## Decision 7: Service Principal DataGrid

**Decision**: Replace the hardcoded `ServicePrincipalList` MudTable with a full `ServicePrincipalCrud` component using `MudDataGrid`.

**Rationale**: The current page shows 5 hardcoded entries with a "coming soon" banner. The new component needs CRUD actions (create, edit scopes, suspend, reactivate, revoke, rotate secret) which are better served by `MudDataGrid` with action columns (matches `OrganizationList` pattern).

---

## Decision 8: Secret Display Pattern

**Decision**: Use a dedicated `SecretDisplayDialog` component for one-time credential display.

**Rationale**: Both create and rotate-secret flows need to show a secret exactly once with copy-to-clipboard and a warning. A shared dialog component avoids duplication.

---

## Decision 9: Metrics Auto-Refresh

**Decision**: Use the existing `System.Threading.Timer` polling pattern from `ValidatorPanel`.

**Rationale**: The Validator panel already implements a 3-second polling timer with play/pause toggle. Metrics will use the same pattern with a configurable interval (default 5 seconds per spec).

---

## Decision 10: Existing Service Client Coverage

**Decision**: The consolidated `Sorcha.ServiceClients` library already has partial coverage. UI services will call the API Gateway directly via `HttpClient` (existing pattern), not through `Sorcha.ServiceClients`.

| Endpoint Area | ServiceClients Coverage | UI Approach |
|---------------|------------------------|-------------|
| Register Policy | `GetRegisterPolicy`, `GetPolicyHistory` exist | UI HttpClient → Gateway |
| System Register | `SystemRegisterBlueprintExists` only | UI HttpClient → Gateway |
| Validator Admin | `SubmitTransactionAsync` only | UI HttpClient → Gateway |
| Service Principals | None | UI HttpClient → Gateway |

**Rationale**: UI services wrap HttpClient calls directly to the API Gateway (existing pattern in `ValidatorAdminService`, `RegisterService`, `OrganizationAdminService`). No need to add UI dependency on `Sorcha.ServiceClients`.

---

## Endpoint-Route Mapping

All Feature 049 endpoints and their YARP route status:

| Endpoint | YARP Route | Auth Policy | Status |
|----------|-----------|-------------|--------|
| `GET /api/registers/{id}/policy` | `registers-policy-route` | CanManageRegisters | Exists |
| `GET /api/registers/{id}/policy/history` | `registers-policy-route` | CanManageRegisters | Exists |
| `POST /api/registers/{id}/policy/update` | `registers-policy-route` | CanManageRegisters | Exists |
| `GET /api/system-register` | `system-register-base-route` | Authenticated | Exists |
| `GET /api/system-register/blueprints` | `system-register-route` | Authenticated | Exists |
| `GET /api/system-register/blueprints/{id}` | `system-register-route` | Authenticated | Exists |
| `GET /api/system-register/blueprints/{id}/versions/{v}` | `system-register-route` | Authenticated | Exists |
| `POST /api/admin/validators/start` | **MISSING** | RequireAdministrator | **Needs route** |
| `POST /api/admin/validators/stop` | **MISSING** | RequireAdministrator | **Needs route** |
| `GET /api/admin/validators/{id}/status` | **MISSING** | RequireAdministrator | **Needs route** |
| `POST /api/admin/validators/{id}/process` | **MISSING** | RequireAdministrator | **Needs route** |
| `GET /api/admin/validators/monitoring` | **MISSING** | RequireAdministrator | **Needs route** |
| `GET /api/metrics` | `validator-metrics-route` | Authenticated | Exists |
| `GET /api/metrics/validation` | `validator-metrics-route` | Authenticated | Exists |
| `GET /api/metrics/consensus` | `validator-metrics-route` | Authenticated | Exists |
| `GET /api/metrics/pools` | `validator-metrics-route` | Authenticated | Exists |
| `GET /api/metrics/caches` | `validator-metrics-route` | Authenticated | Exists |
| `GET /api/metrics/config` | `validator-metrics-route` | Authenticated | Exists |
| `POST /api/validators/threshold/setup` | `validators-direct-route` | Authenticated | Exists |
| `POST /api/validators/threshold/sign` | `validators-direct-route` | Authenticated | Exists |
| `GET /api/validators/threshold/{id}/status` | `validators-direct-route` | Authenticated | Exists |
| `POST /api/service-principals` | `service-principals-base-route` | Authenticated | Exists |
| `GET /api/service-principals` | `service-principals-base-route` | Authenticated | Exists |
| `GET /api/service-principals/{id}` | `service-principals-direct-route` | Authenticated | Exists |
| `PUT /api/service-principals/{id}/scopes` | `service-principals-direct-route` | Authenticated | Exists |
| `POST /api/service-principals/{id}/suspend` | `service-principals-direct-route` | Authenticated | Exists |
| `POST /api/service-principals/{id}/reactivate` | `service-principals-direct-route` | Authenticated | Exists |
| `DELETE /api/service-principals/{id}` | `service-principals-direct-route` | Authenticated | Exists |

**Action**: Add 1 YARP route group for `/api/admin/validators/{**}` → Validator cluster with `RequireAdministrator` policy.
