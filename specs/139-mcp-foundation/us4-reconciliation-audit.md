# US4 — Endpoint Reconciliation Audit (T022)

**Goal**: every advertised MCP tool calls a typed `Sorcha.ServiceClients` method (no hand-rolled URLs), eliminating the endpoint-drift bug class. This document is the working checklist for T023–T030.

## Technical path (verified)

- The typed HTTP clients (`AddHttpClient<IFoo,Foo>` in `Sorcha.ServiceClients.Http/Extensions/HttpServiceCollectionExtensions.cs`) attach **no auth `DelegatingHandler`** — only `ServiceAuthClient` is registered, not wired onto them. So the MCP server can attach `CallerTokenForwardingHandler` to each typed client's named HttpClient in `Program.cs` (`services.AddHttpClient("IRegisterServiceClient").AddHttpMessageHandler<CallerTokenForwardingHandler>()`) **without touching shared infra** or disrupting other services.
- Point the MCP server's `ServiceClients:*:Address` at the **API Gateway** so the typed clients route through the front door (gateway holds the real routes + F136 enforcement). This replaces the per-service `localhost:NNNN` addresses the bare-client tools use today.
- Tools then inject the typed client instead of `IHttpClientFactory`.

## Reconciliation status by tool

Legend: ✅ existing client method · ➕ add a client method · 🔧 drifted/needs rework · 🖥 local compute (no backend).

### Reusable now (✅) — lowest-risk first batch
| Tool | Client method |
|---|---|
| `sorcha_register_stats` | `IRegisterServiceClient.GetStatsAsync` |
| `sorcha_transaction_history` | `IRegisterServiceClient.GetTransactionsByWalletAsync` |
| `sorcha_blueprint_get` | `IBlueprintServiceClient.GetBlueprintAsync` |
| `sorcha_blueprint_validate` / `sorcha_action_validate` | `IBlueprintServiceClient.ValidatePayloadAsync` |
| `sorcha_wallet_info` | `IWalletServiceClient.GetWalletAsync` |
| `sorcha_peer_status` | `IPeerServiceClient.QueryValidatorsAsync` (health/roster) |
| `sorcha_register_query` | `IRegisterServiceClient` transaction/query reads |

### Local compute (🖥) — no change needed
`sorcha_schema_generate`, `sorcha_schema_validate`, `sorcha_jsonlogic_test`, `sorcha_disclosure_analysis`.

### Need a new client method (➕) — `Sorcha.ServiceClients.Http` additions
- **Blueprint**: `AddBlueprintAsync` (create), `UpdateBlueprintAsync`, `ExportBlueprintAsync`, `GetBlueprintDiffAsync`, `SimulateActionAsync`, `GetWorkflowInstancesAsync`, `GetWorkflowStatusAsync`, `GetActionDetailsAsync`, `GetInboxAsync`, `GetDisclosedDataAsync` — back `blueprint_create/update/export/diff/simulate`, `workflow_instances/status`, `action_details`, `inbox_list`, `disclosed_data`.
- **Validator**: `GetValidatorStatusAsync` — back `validator_status`.
- **Tenant**: there is **no `ITenantServiceClient`** today. `tenant_list/create/update`, `user_list/manage`, `token_revoke` need either a new `ITenantServiceClient` or reuse of the existing platform/participant clients. Decide: introduce `ITenantServiceClient` (cleanest, benefits the platform) vs targeted methods.

### Drifted / needs rework (🔧)
- **`sorcha_action_submit`** — currently POSTs `/api/actions/{actionInstanceId}/submit` (does not exist). Real path is `POST /api/instances/{instanceId}/actions/{actionId}/execute`. **The tool's parameter model also needs rework**: it takes a single `actionInstanceId`, but the real endpoint needs `instanceId` + `actionId`. Add `IBlueprintServiceClient.ExecuteActionAsync(instanceId, actionId, payload)` and update the tool's parameters.
- **`sorcha_tenant_create`** — POSTs `/api/organizations {name,adminEmail}`; real org-with-admin provisioning is `POST /api/platform/organizations` (SystemAdmin, different body). Reconcile via the new Tenant client.
- **`sorcha_audit_query`, `sorcha_log_query`, `sorcha_metrics`** — target `/api/audit`, `/api/admin/logs`, `/api/admin/metrics`, whose backends are **unconfirmed**. Verify whether these endpoints exist (Tenant audit? observability surface?). If no backend exists, either point at the real source or mark the tool as not-yet-supported (return a clear "unavailable" status) rather than advertising a dead tool. **Decision needed before reconciling these three.**

## Decisions locked (2026-05-26)

1. **`ITenantServiceClient`** — **introduce it** as a new typed client in `Sorcha.ServiceClients.Http/Tenant/` (interface + impl + DI registration in `AddHttpServiceClients`), then reconcile `tenant_list/create/update`, `user_list/manage`, `token_revoke` onto it. `tenant_create` → `POST /api/platform/organizations` (the correct admin provisioning route).
2. **`audit_query` / `log_query` / `metrics`** — **mark not-supported**. No backend log/metrics/audit-query API exists. Rewrite each tool to: keep the admin auth gate, then return a clear `NotSupported` result ("the platform exposes no X API yet; this tool will be wired up when an observability/audit surface lands") instead of calling a phantom endpoint. Remove the dead backend-call code + now-unused ctor deps; update each tool's tests. Keep the tools advertised (admin tier) so the catalogue/manifest stays stable, but they fail honestly.
3. **`register_stats`** — **enrich the client**, don't regress. Add to `IRegisterServiceClient`: a method returning the per-register transaction stats (unique wallets/senders/recipients, payload totals, earliest/latest — the `/api/query/stats?registerId=` shape) plus a recent-registers list, so the reconciled tool preserves today's rich output. Then reconcile `register_stats` onto those methods (dropping its bare HttpClient).

## Reconciliation progress

- ✅ **`blueprint_get`** reconciled onto `IBlueprintServiceClient.GetBlueprintAsync` — the **proven pattern** (commit `c339d2b0`): inject typed client, keep shaping logic, attach `CallerTokenForwardingHandler` to the client in `Program.cs`, flip tests to mock the client. Build + tests green; live forwarding tests still pass.
- ✅ **Batch 1 (reusable-now)** complete:
  - `register_stats` → enriched `IRegisterServiceClient`: added `GetRegisterTransactionStatsAsync` (`/api/query/stats`) + `GetRecentRegistersAsync` (`/api/registers/`); `GetStatsAsync` supplies the platform count. Rich per-register output preserved.
  - `transaction_history` → reworked params (registerId now **required**, optional workflowInstanceId). Uses `GetTransactionsByInstanceIdAsync` (instance-scoped) or `GetTransactionsAsync` (paged). The old `/api/transactions` route did not exist.
  - `wallet_info` → reworked to take a **walletAddress** param (was ambient `/api/wallet/info`). Uses `IWalletServiceClient.GetWalletAsync`.
  - **Left on bare HttpClient (no clean mapping — token still forwards via default-client handler):**
    - `peer_status` — consumes bespoke `/api/peers/stats` + `/api/peers/health`; no Peer client exists in `Sorcha.ServiceClients.Http` and the shapes don't map to any existing typed method.
    - `register_query` — reads **materialised record data** (`/api/registers/{id}/data`, OData); no typed method returns that shape (register client only exposes transaction reads). Forcing it onto transaction reads would change semantics.
    - `action_validate` — the live `/api/actions/{id}/validate` endpoint returns a rich `{isValid, errors[]}`; the only typed method `ValidatePayloadAsync` returns a bare bool (loses the error list) AND needs blueprintId+actionId the tool doesn't take. Reworking would regress error reporting.
- ✅ **Batch 2 (Blueprint)** complete: added ~12 raw-JSON Blueprint client methods (list/create/update/diff/route/calculate/workflow-instances/status/action-details/inbox/disclosed/execute); reconciled the designer + participant tools; reworked `action_submit` to `instanceId`+`actionId` → `POST .../execute`. The 4 pure-compute tools untouched.
- ✅ **Batch 3 (Tenant)** complete: introduced `ITenantServiceClient` (+ impl + DI). Reconciled `tenant_list/create/update`, `user_list/manage`, `token_revoke`; fixed `tenant_create` → `POST /api/platform/organizations`.
- ✅ **Batch 4 (validator + phantom)** complete:
  - `validator_status` — **left on bare HttpClient** (token still forwards via the default-client handler). It composes three distinct endpoints (`/health`, `/api/admin/validators/{id}/status`, `/api/validators/{id}/count`) into a bespoke result; `IValidatorServiceClient` has no status endpoint and adding one would force an MCP-specific composite shape into the shared client. Noted, not force-fitted.
  - `audit_query`, `log_query`, `metrics` — **marked `NotSupported`** (LOCKED DECISION). Auth gate kept; dead backend-call code, unused ctor deps, and private DTOs removed; tests assert the NotSupported + auth-gate behaviour. Tools stay advertised.

## Suggested execution order (T023–T030)

1. **Wire the handler + gateway base** onto the typed clients in `Program.cs` (the T008 carry-forward). Build green.
2. **Batch 1 (✅ tools)** — reconcile the 7 reusable-now tools; add a live integration test (admin token → `register_stats` reaches the gateway, no drift). This also closes the US1 T015 success-path.
3. **Batch 2 (Blueprint ➕)** — add the ~10 Blueprint client methods; reconcile the Blueprint/workflow/action/inbox/disclosed tools; rework `action_submit` params + `ExecuteActionAsync`.
4. **Batch 3 (Tenant)** — decide on `ITenantServiceClient`; reconcile tenant/user/token tools; fix `tenant_create`.
5. **Batch 4 (validator + the 3 phantom admin tools)** — `GetValidatorStatusAsync`; resolve audit/log/metrics (real source or not-supported).
6. Remove the default-client forwarding handler once all tools are on typed clients (it becomes redundant), or keep it as belt-and-braces.

Each batch is independently shippable and must be covered by the integration safety net across permitted/refused tiers.
