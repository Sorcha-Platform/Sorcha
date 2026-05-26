# Implementation Plan: MCP Server Capability Gap Closure (Feature 140)

**Branch**: `140-mcp-capabilities` | **Date**: 2026-05-26 | **Spec**: [spec.md](./spec.md)
**Depends on**: Feature 139 (merged) — token forwarding, F136 tier gate, typed-client reconciliation, manifest-integrity gate, both transports.

## Approach

Each wave ADDS new MCP tools on top of the F139 foundation, reusing its proven patterns. There are **no new auth/transport mechanisms** — only new tools + the typed-client methods they need.

### The invariant every new tool MUST satisfy (enforced by F139's gates)
1. **Tier-tagged** in `ToolEntitlements.All` (`src/Apps/Sorcha.McpServer/Services/ToolEntitlement.cs`) — admin/designer = platform+role; citizen = consumer; participation = cross-tier.
2. **Routed through a typed `Sorcha.ServiceClients` method** (add the method to the client if missing; never hand-roll a URL). Attach `CallerTokenForwardingHandler` to any new typed client in `Program.cs`.
3. **Registered in the manifest in BOTH places** — the gateway `Discoverability` catalogue (`src/Services/Sorcha.ApiGateway/appsettings.json`) and repo-root `server.json` — or `ManifestIntegrityTests` fails the build. Bump `server.json` version per wave.
4. **Covered by tests** — unit (auth gate + success/parse) and, where a live path exists, the Docker-gated integration suite. Description meets the FR-017 quality bar.
5. **Observed** — invocations flow through the existing `AddCallToolFilter` → `ToolAuditService`/`McpMetrics` automatically; no per-tool work.

### Execution: one PR per wave, in priority order. Each wave is independently shippable on top of F139.

---

## Wave 1 — Register control & federation (P1) — the MCP-101/102 gap

New tools (all **platform + admin**):

| Tool | Operation | Client method |
|---|---|---|
| `sorcha_register_subscribe` | subscribe node to a register | `IPeerServiceClient.SubscribeToRegisterAsync` (exists) |
| `sorcha_register_unsubscribe` | unsubscribe | `IPeerServiceClient.UnsubscribeFromRegisterAsync` (exists) |
| `sorcha_register_sync_state` | typed sync state | `IRegisterServiceClient.GetSyncStateAsync` (exists) |
| `sorcha_register_relationship` | node's local relationship | `IRegisterServiceClient.GetLocalRelationshipAsync` (exists) |
| `sorcha_transaction_status` | lifecycle status (active/revoked/superseded) | `IRegisterServiceClient.GetTransactionAsync` (exists) or **add** `GetTransactionStatusAsync` (F079 `/transactions/{id}/status`) |
| `sorcha_transaction_inclusion_proof` | Merkle inclusion proof | **add** `GetInclusionProofAsync` (`/transactions/{id}/inclusion-proof`) |
| `sorcha_transaction_verification_bundle` | portable offline bundle | **add** `GetVerificationBundleAsync` (`/transactions/{id}/verification-bundle`) |
| `sorcha_transaction_revoke` | submit revocation with reason | **add** `RevokeTransactionAsync` (`POST /transactions/revoke`) |

Acceptance (SC-001): an operator runs subscribe → sync-state → (submit via existing `action_submit`) → verify → revoke entirely through MCP. A consumer token is refused every Wave-1 tool.

---

## Wave 2 — Credential & presentation lifecycle (P2)

New tools (**platform** + appropriate role):

| Tool | Operation | Client method |
|---|---|---|
| `sorcha_credential_offer` | create OID4VCI offer + status | `IHaipServiceClient.CreateCredentialOfferAsync` / `GetOfferStatusAsync` (exist) |
| `sorcha_presentation_request` | create OID4VP request | `IHaipServiceClient.CreatePresentationRequestAsync` (exists) |
| `sorcha_presentation_status` | poll lifecycle to terminal outcome (F111) | **add** Blueprint `GetPresentationStatusAsync` (`/api/presentations/{id}/status`) |
| `sorcha_credential_revoke` / `_suspend` / `_reinstate` / `_refresh` | issued-credential lifecycle | **add** Blueprint methods (`POST /api/v1/credentials/{id}/{revoke|suspend|reinstate|refresh}`) |

---

## Wave 3 — Citizen self-service (P2) — **consumer tier**

| Tool | Operation | Client method |
|---|---|---|
| `sorcha_my_credentials` | list the citizen's credentials | `ICitizenWalletClient.ListCredentialsAsync` (exists) |
| `sorcha_my_devices` (list/rename/revoke) | device management | `ICitizenWalletClient.ListDevicesAsync` / `RenameDeviceAsync` / `RevokeDeviceAsync` (exist) |
| `sorcha_my_persona` (read/update) | persona | **add** persona client methods (`GET/PUT /me/persona`) |
| `sorcha_pending_applications` | F124 pending-app notice | **add** wallet client method (`GET /api/v1/wallet/pending-applications`) |
| `sorcha_my_presentations` | cross-device presentation history | `ICitizenWalletClient.ListPresentationsAsync` (exists) |
| `sorcha_my_invitations` | list/accept org/register invitations | `IRegisterInvitationServiceClient` (exists) + tenant org-invitation methods |

All scoped to the calling citizen by the platform; cross-citizen access impossible.

---

## Wave 4 — Platform-administration depth (P3)

| Tool | Operation | Client method |
|---|---|---|
| `sorcha_org_status` | suspend/reactivate org | **add** `ITenantServiceClient.SetOrganizationStatusAsync` (`PUT /api/platform/organizations/{id}/status`) |
| `sorcha_platform_settings` (read/update) | platform settings (public-org toggle) | **add** tenant platform-settings methods |
| `sorcha_org_user_audit` | read-only org user list | `ITenantServiceClient` user list (reuse/extend) |
| `sorcha_validator_control` (start/stop/restart) | validator orchestration | **add** `IValidatorServiceClient` admin methods (`/api/admin/validators/*`) |
| `sorcha_user_provision` / `sorcha_user_password_reset` | platform user mgmt | **add** tenant platform-user methods |

`audit_query`/`log_query`/`metrics` remain NotSupported until a real observability/audit surface exists (F139 decision).

## Out of scope (unchanged)
Raw `wallet_sign` (dedicated security-reviewed wave), full OAuth 2.1 AS (backlog), node-lifecycle tools (operator-only).

## Structure decision
Single-app feature centred on `src/Apps/Sorcha.McpServer/Tools/**` + additive methods in `Sorcha.ServiceClients.Http`. No new services, no persistence. Each wave: new tools + client methods + entitlement/manifest/server.json updates + tests, as one PR.
