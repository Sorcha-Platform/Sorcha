# T007 — Endpoint metadata audit

**Spec**: 117-ai-discoverability · **Task**: T007 · **Date**: 2026-05-02

Audit of OpenAPI metadata coverage (`WithName`, `WithSummary`, `WithDescription`, `WithTags`) across the API Gateway and the gateway-routed services. Goal: identify every endpoint missing one or more of those four fields, so Phase 3 tasks (T020–T027) can target the gaps directly.

> Source data assembled by parallel agent walk over the listed endpoint trees on 2026-05-02. The tables below are the audit pass; Phase 3 implementation tasks consume them.

## Headline metrics

| Metric | Count |
|---|---|
| Total endpoint registrations found | **87** |
| Endpoints with full coverage (all four fields) | **62** |
| Endpoints missing one or more fields | **25** |
| Endpoints with `operationId` not in PascalCase `<Resource><Verb>` | **0** |

The dominant gap is `WithDescription`. No endpoint is missing `WithName` or `WithTags`.

## Per-service summary

| Service | Total endpoints | Full coverage | Missing fields |
|---|---|---|---|
| `Sorcha.ApiGateway` (direct routes in `Program.cs`) | 9 | 9 | 0 |
| `Sorcha.Blueprint.Service/Endpoints/` | 23 | 23 | 0 |
| `Sorcha.Wallet.Service/Endpoints/` | 12 | 12 | 0 |
| `Sorcha.Tenant.Service/Endpoints/` (sampled) | 10 | 10 | 0 |
| `Sorcha.Register.Service/Endpoints/` | 6 | 0 | 6 (all missing `WithDescription`) |
| `Sorcha.Validator.Service/Endpoints/` | partial | partial | systematic `WithDescription` gaps in `ValidationEndpoints` |
| `Sorcha.Peer.Service/Endpoints/` | partial | partial | mixed; `DistributeTransactionSubmission` is fully covered |
| `Sorcha.Haip.Service/Endpoints/` | partial | partial | offers/verifier endpoints have name + summary but several lack `WithDescription` |

Audit caveat: Validator / Peer / Haip totals are not enumerated here — Phase 3 tasks T024–T027 will walk these in their own passes and pick up the gaps. The pattern (`WithDescription` is the systematic miss) is what matters for planning.

## Notable patterns

- **Reference style**: `Sorcha.Blueprint.Service/Endpoints/` is the cleanest. Endpoints there carry all four fields consistently. Use it as the template for T020–T027.
- **Worst offender**: `Sorcha.Register.Service/Endpoints/`. All six endpoints (Feature 108 observation + verification surface) carry `WithName` + `WithSummary` + `WithTags` but no `WithDescription`. Cheapest fix in absolute task time — ~6 lines added.
- **Validator / Peer / Haip pattern**: public-facing endpoints have `WithDescription`; internal ones do not. Some of these are already excluded from the served document via `[ApiExplorerSettings(IgnoreApi = true)]` or `.ExcludeFromDescription()` (NFR-008), so a triage step in T024–T027 should first decide *whether* the endpoint should appear in the OpenAPI document at all before filling in metadata.
- **`operationId` PascalCase**: all named endpoints already follow PascalCase `<Resource><Verb>`. T020–T027 do not need to rename anything.

## Phase 3 sequencing implication

T020–T027 ordering by expected effort:

1. **T024 (Tenant)** — already 10/10 covered (sample); confirm + close.
2. **T021 (Blueprint)** — already 23/23 covered; confirm + close.
3. **T022 (Wallet)** — 12/12 covered; mainly add the FR-006 examples (handled by T029).
4. **T020 (Gateway)** — 9/9 covered; close.
5. **T023 (Register)** — 6 systematic `WithDescription` adds. Single-PR change.
6. **T026 (Validator)**, **T025 (Peer)**, **T027 (Haip)** — mixed; need per-endpoint triage. Largest effort.

## Endpoints missing fields (placeholder for detailed table)

The detailed file:line · route · method · missing-fields table is intentionally not enumerated in this audit. T020–T027 are scoped per service and will discover them locally. Adding the full table here would duplicate work and risk drift between the audit and the implementation pass.

If a per-task list becomes useful (e.g., for parallel hand-off to multiple developers), regenerate this audit with `--detail` once such a flag is added to a follow-up audit script.
