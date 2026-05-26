# Phase 1 Contracts — MCP Server Foundation

This feature adds no new platform REST endpoints. The "contracts" are: (1) the MCP transport surface, (2) the tier→tool entitlement matrix, and (3) the tool→service-client reconciliation table. The authoritative backend contracts are the existing platform endpoints reached via the typed `Sorcha.ServiceClients`.

## 1. Transport surface

| Transport | Entry | Auth | Notes |
|---|---|---|---|
| **stdio** | process started with `--transport stdio` (default) + `--jwt-token` / `SORCHA_JWT_TOKEN` | startup token validated once (`SorchaIssuer`/`SorchaAudiences`) | one caller per process |
| **Streamable HTTP** | `app.MapMcp()` on the MCP server's `WebApplication`; exposed externally as gateway route `/mcp` | per-request `Authorization: Bearer`, validated by `AddJwtAuthentication` before dispatch; rejected if absent/invalid | stateless mode; `ConfigureSessionOptions` scopes the advertised tool set per request |

Both transports resolve the same `ICallerContext` and reach the same tool surface, scoped by tier.

## 2. Tier → tool entitlement matrix (foundation re-tagging — no new tools)

Tier-primary, role-secondary. Participation/read tools are cross-tier. `wallet_sign` is **removed** from the advertised surface this feature.

| Tool | Tier(s) | Role | Slice |
|---|---|---|---|
| `sorcha_health_check` | Platform | admin | Operator/Admin |
| `sorcha_log_query` | Platform | admin | Operator/Admin |
| `sorcha_metrics` | Platform | admin | Operator/Admin |
| `sorcha_audit_query` | Platform | admin | Operator/Admin |
| `sorcha_tenant_list` | Platform | admin | Operator/Admin |
| `sorcha_tenant_create` | Platform | admin | Operator/Admin |
| `sorcha_tenant_update` | Platform | admin | Operator/Admin |
| `sorcha_user_list` | Platform | admin | Operator/Admin |
| `sorcha_user_manage` | Platform | admin | Operator/Admin |
| `sorcha_peer_status` | Platform | admin | Operator/Admin |
| `sorcha_validator_status` | Platform | admin | Operator/Admin |
| `sorcha_register_stats` | Platform | admin | Operator/Admin |
| `sorcha_token_revoke` | Platform | admin | Operator/Admin |
| `sorcha_blueprint_list` | Platform | designer | Designer |
| `sorcha_blueprint_get` | Platform | designer | Designer |
| `sorcha_blueprint_create` | Platform | designer | Designer |
| `sorcha_blueprint_update` | Platform | designer | Designer |
| `sorcha_blueprint_validate` | Platform | designer | Designer |
| `sorcha_blueprint_simulate` | Platform | designer | Designer |
| `sorcha_disclosure_analysis` | Platform | designer | Designer |
| `sorcha_blueprint_diff` | Platform | designer | Designer |
| `sorcha_blueprint_export` | Platform | designer | Designer |
| `sorcha_schema_validate` | Platform | designer | Designer |
| `sorcha_schema_generate` | Platform | designer | Designer |
| `sorcha_jsonlogic_test` | Platform | designer | Designer |
| `sorcha_workflow_instances` | Platform | designer | Designer |
| `sorcha_inbox_list` | Consumer, Platform | — | Participation |
| `sorcha_action_details` | Consumer, Platform | — | Participation |
| `sorcha_action_validate` | Consumer, Platform | — | Participation |
| `sorcha_action_submit` | Consumer, Platform | — | Participation |
| `sorcha_workflow_status` | Consumer, Platform | — | Participation |
| `sorcha_disclosed_data` | Consumer, Platform | — | Participation |
| `sorcha_transaction_history` | Consumer, Platform | — | Participation / citizen read |
| `sorcha_register_query` | Consumer, Platform | — | Participation / read |
| `sorcha_wallet_info` | Consumer, Platform | — | Citizen read (read-only) |
| `sorcha_wallet_sign` | — | — | **REMOVED** (deferred to dedicated wave) |

Result: a consumer token sees the Participation + citizen-read tools (≥1 each) — fixing the F136 shut-out (FR-004). `service`-tier tokens see nothing and are rejected at connect.

> Note: `schema_validate`, `schema_generate`, `jsonlogic_test` are pure-compute (no backend) and need no token forwarding, but keep the same entitlement tagging.

## 3. Tool → service-client reconciliation

Each backed tool maps to a typed `Sorcha.ServiceClients` method (add the method to the client when absent — never hand-roll a URL in the tool). Status legend: ✅ client method exists · ➕ add client method · 🔧 known route fix · 🖥 local compute (no backend).

| Tool | Target client / operation | Status |
|---|---|---|
| `sorcha_action_submit` | `IBlueprintServiceClient` → `POST /api/instances/{instanceId}/actions/{actionId}/execute` | 🔧 (was `/api/actions/{id}/submit`) |
| `sorcha_tenant_create` | `ITenantServiceClient` → `POST /api/platform/organizations` | 🔧 (was `/api/organizations`) |
| `sorcha_inbox_list` | `IBlueprintServiceClient` → `GET /api/actions/pending` | ✅/verify |
| `sorcha_action_details` | `IBlueprintServiceClient` action/instance read | verify/➕ |
| `sorcha_action_validate` | `IBlueprintServiceClient` validate-against-schema | verify/➕ |
| `sorcha_workflow_status` | `IBlueprintServiceClient` instance status | verify/➕ |
| `sorcha_workflow_instances` | `IBlueprintServiceClient` instances-by-blueprint | verify/➕ |
| `sorcha_blueprint_*` (list/get/create/update/validate/simulate/diff/export/disclosure) | `IBlueprintServiceClient` | verify/➕ per method |
| `sorcha_register_query` | `IRegisterServiceClient` query | verify/➕ |
| `sorcha_register_stats` | `IRegisterServiceClient` stats | verify/➕ |
| `sorcha_transaction_history` | `IRegisterServiceClient` tx history | verify/➕ |
| `sorcha_disclosed_data` | `IRegisterServiceClient` disclosed data | verify/➕ |
| `sorcha_wallet_info` | `IWalletServiceClient` wallet metadata (read-only) | verify/➕ |
| `sorcha_tenant_list` / `tenant_update` | `ITenantServiceClient` / `IPlatformOrg*` | verify/🔧 |
| `sorcha_user_list` / `user_manage` | `ITenantServiceClient` platform user ops | verify/🔧 |
| `sorcha_token_revoke` | `ITenantServiceClient` token revoke | verify/➕ |
| `sorcha_audit_query` | `ITenantServiceClient` / event-audit | verify/➕ |
| `sorcha_peer_status` | `IPeerServiceClient` (gRPC) | verify |
| `sorcha_validator_status` | `IValidatorServiceClient` | verify |
| `sorcha_health_check` | gateway `/health` aggregate | verify |
| `sorcha_log_query` / `sorcha_metrics` | gateway / observability surface | verify |
| `sorcha_schema_validate` / `sorcha_schema_generate` / `sorcha_jsonlogic_test` | local compute | 🖥 |

**Implementation rule**: the audit confirms each row against the live service. Any "verify" that turns out drifted becomes a 🔧; any missing client method becomes a ➕ in `Sorcha.ServiceClients.Http`. The integration smoke harness (R-006) is the acceptance gate — every advertised tool must resolve to a live operation (SC-001).

## 4. Integrity gate contract

CI reflects the `[McpServerTool]`-attributed set from the MCP assembly and asserts:
- the gateway `appsettings.json` MCP catalogue lists exactly that set, and
- `server.json` is consistent and version-pinned.

Mismatch → build fails (SC-005).
