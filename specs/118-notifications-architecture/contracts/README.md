# Contracts — Notifications & Realtime Architecture

This directory holds the wire-shape contracts for Feature 118.

| File | Purpose |
|---|---|
| `hub-signal.schema.json` | JSON Schema for the conceptual `HubSignal` envelope. Validates that hub event method parameter sets conform to the thin-signal contract (FR-016 — FR-019). |
| `inbox-entry.schema.json` | JSON Schema for the wire shape of `InboxEntry` returned by `GET /api/me/inbox/{id}`. |
| `inbox-endpoints.openapi.yaml` | OpenAPI 3.1 fragment for the new `/api/me/inbox/*` and `/api/internal/inbox` endpoints. Will be merged into the gateway's served `openapi.yaml` once the endpoints land. |
| `tenant-hub-client.cs.md` | Typed-client interface contract for `ITenantHubClient`. |
| `blueprint-hub-client.cs.md` | Typed-client interface contract for `IBlueprintHubClient` (post-rename from `IActionsHubClient`). |
| `wallet-hub-client.cs.md` | Typed-client interface contract for `IWalletHubClient` (expanded scope vs. today). |
| `register-hub-client.cs.md` | Typed-client interface contract for `IRegisterHubClient` (already exists in tree; recorded here for completeness). |

Every typed-client interface contract notes its event method, parameter list, group it fires on, and the matching authenticated REST detail endpoint. Code review uses these documents to verify FR-018 — every event has a documented detail endpoint.
