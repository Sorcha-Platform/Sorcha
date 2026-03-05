# Design: System Administration Tooling (Feature 049)

**Date**: 2026-03-04
**Branch**: `049-system-admin-tooling`
**Spec**: [specs/049-system-admin-tooling/spec.md](../../specs/049-system-admin-tooling/spec.md)

## Problem

~65 backend API endpoints across the Validator, Register, and Tenant services have no admin UI or CLI coverage. Administrators cannot configure register policies, approve validators, view metrics, manage service principals, or monitor the system register without raw API calls.

## Scope

6 UI features + 5 CLI command groups. All backend endpoints already exist — this is pure frontend/CLI work.

| Feature | UI | CLI |
|---------|:--:|:---:|
| Service Principal CRUD | Replace hardcoded placeholder | Already done |
| Register Policy | Wizard step + Register detail tab | `register policy` |
| Validator Consent Queue | New tab on Validator page | `validator consent` |
| Validator Metrics | New tab on Validator page | `validator metrics` |
| System Register | New admin page | `register system` |
| Threshold Signing | New tab on Validator page | `validator threshold` |

## Approach: UI-First

Build each admin surface in the UI first, then add matching CLI commands. The UI defines data shapes and user flows; the CLI mirrors them for automation.

## UI Architecture

### Validator Page — Tab Extension

The existing `/admin/validator` page has a `ValidatorPanel` with mempool monitoring. Extend to 5 tabs:

```
[ Mempool (existing) | Consent Queue | Metrics | Threshold | Config ]
```

New components:
- `ValidatorConsentPanel` — pending validators grouped by register, approve/reject actions
- `ValidatorMetricsPanel` — KPI cards + expandable subsystem detail panels, auto-refresh
- `ValidatorThresholdPanel` — per-register BLS status, setup action
- `ValidatorConfigPanel` — read-only config viewer from `/api/metrics/config`

### Register Policy — Two Locations

1. **Create Register Wizard** — new `RegisterPolicyStep` between naming and wallet selection
2. **Register Detail Page** — new `RegisterPolicyTab` showing current policy, version history, propose update

### System Register — New Page

New `/admin/system-register` route added to System nav group. Contains `SystemRegisterDashboard` component with status card + paginated blueprint catalog.

### Service Principals — CRUD Replacement

Replace `ServicePrincipalList` (hardcoded) with `ServicePrincipalCrud` component. Full DataGrid with create/edit/suspend/revoke/rotate actions. `SecretDisplayDialog` for one-time credential display with copy-to-clipboard.

## CLI Commands

```
sorcha register policy get|history|update
sorcha register system status|blueprints
sorcha validator consent pending|approve|reject|refresh
sorcha validator metrics [validation|consensus|pools|caches|config]
sorcha validator threshold status|setup
```

Follows existing patterns: Refit clients, Spectre.Console tables, JSON output, standard error handling.

## Service Interfaces (UI)

| Interface | New Methods |
|-----------|-------------|
| `IValidatorAdminService` | Consent (pending/approve/reject/refresh), Metrics (aggregated + subsystem), Threshold (status/setup) |
| `IRegisterService` | Policy (get/history/propose update) |
| `ISystemRegisterService` (new) | Status, blueprints (list/get/version) |
| `IServicePrincipalService` (new) | Full CRUD + suspend/reactivate/rotate |

## Out of Scope

See [UI-CLI-GAP-ANALYSIS.md](../../.specify/UI-CLI-GAP-ANALYSIS.md) for the full backlog. Deferred to Features 050 and 051:
- Wallet delegation & access control
- Credential lifecycle management
- Participant publishing to register
- Participant suspend/reactivate
- Verifiable presentations (OID4VP)
- Push notification management
- Schema provider admin (already has basic page)
- Gateway alerts wiring
- Events admin
- Encrypted payload operation status
