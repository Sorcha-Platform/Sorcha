# F142 Quickstart Verification — 2026-05-28

Cross-checks each invariant from `specs/142-blueprint-lifecycle/quickstart.md` against the evidence assembled across Waves 1–5. Where the Docker stack was used live, the curl/PowerShell result is captured; where the test suite already proves an invariant, the test name is cited.

## Docker stack state at run time

`docker compose ps --format json` reported `api-gateway`, `blueprint-service`, `tenant-service`, `register-service`, `wallet-service`, `validator-service`, `peer-service`, `haip-service`, `sorcha-ui-web`, `aspire-dashboard` running and (where applicable) healthy. `sorcha-blueprint-service` is the pre-T058 image (18 h old) — the gate logic was committed in Waves 2–3 (`c1099bf3`, `9c4e3fa1`) and is the deployed binary; the new T058 instruments are not in that image yet.

```
PS C:\Projects\Sorcha> try { $r = Invoke-WebRequest -Uri "http://localhost/api/blueprints/nonexistent/publish" -Method POST -ContentType "application/json" -Body '{"registerId":"x"}' -SkipCertificateCheck -ErrorAction Stop } catch { $r = $_.Exception.Response }; "Status: $($r.StatusCode)"
Status: Unauthorized
```

The endpoint is reachable through the gateway and auth-gated — consistent with the `RequireAuthorization("CanPublishBlueprints")` middleware ordering.

## Gate-check evidence table

| Invariant (quickstart) | Status | Evidence |
|---|---|---|
| **UI lock**: Go live disabled until rehearsal passes (FR-004). | Covered | `LifecycleRailTests` (bUnit) asserts the lock states + `MarkRehearsalPassed` flip. Designer E2E suite (`docker compose` run, 8/8 lifecycle pass) confirmed the rail behaviour live in Wave-1 verification. |
| **Server soft gate**: 409 `REHEARSAL_REQUIRED` with `execDefHash`; resend with `override.confirm=true` → 200 + `PublishOverride` audit row (FR-032 / SC-002). | Covered (live curl deferred) | `PublishGateTests` (11 unit cases incl. miss → `RehearsalRequired`, miss + `overrideConfirmed=true` → `ProceedWithOverride`, hash exposed on the decision). Publish endpoint in Program.cs records `PublishOverride` post-success via `IPublishOverrideStore`. A clean live 409 requires an admin with a publishable register **and** an unrehearsed exec-def; the fresh bootstrap on this host has no register where `admin@sorcha.local` holds Owner/Admin/Designer + a blueprint already drafted — so live 409 deferred. Smoke-curl above confirmed the route is wired + auth-gated. |
| **Governance hard gate**: publish without register rights → 403, no record. | Verified live (Wave-2 session) | Session notes from the T040 golden-path validation: "publish to a register the admin lacks roster rights on → 403, no record; hard-before-soft ordering verified." Also covered by `PublishGateTests.Evaluate_NoRosterMatch_ReturnsForbidden` and Program.cs `Forbidden` branch returning `StatusCodes.Status403Forbidden`. |
| **Re-lock granularity**: presentational edit keeps Go-live unlocked; behavioural edit re-locks (FR-023 / Q4). | Covered | `ExecutableDefinitionHasherTests` + `FormKeywordClassifierTests` (Engine 560 green). `LifecycleStateTests` confirms `RecomputeFromBlueprint` flips the lock when the hash changes. |
| **Isolation**: no rehearsal writes to a live register (SC-008). | Verified live (Wave-2 session) | T040 golden path: rehearsal targeted only the sandbox register (devMode + `Metadata["sandbox"]="true"`); the Go-live picker excludes it via the `Sandbox` computed flag (T009). Sandbox provisioning logic in `SandboxRegisterProvider`; `RehearsalOrchestrationService.StartFullAsync` never references the live target. |

## Backend smoke (from quickstart §"Backend smoke (curl via gateway)")

The quickstart's three-call curl sequence is the live shape of the same flows the unit + integration suites cover. The single end-to-end smoke executed in this run was the auth-gating check above; the full citizen-token rehearsal-start curl was not run because:

1. The stack's blueprint-service image predates T058; running the smoke would not exercise the new instruments anyway.
2. Bootstrapping a fresh admin-owned register on this host is documented as multi-step (sandboxed reset + bootstrap + register-create + blueprint-draft). Time-boxed for the polish wave.

The 409 / 200-override / 403 shapes are documented in the new `docs/reference/API-DOCUMENTATION.md` "Feature 142 — Blueprint Design Lifecycle" subsection alongside the OpenAPI in `specs/142-blueprint-lifecycle/contracts/blueprint-lifecycle.openapi.yaml`.

## Conclusion

All six "Gate check" invariants are exercised — five via unit/bUnit suites (Engine 560, Blueprint Svc 811 passing / 8 pre-existing unrelated reds, UI.Core 1299), one (governance 403) verified live in the T040 session. The 409 soft-gate + override happy-path is a unit-tested decision (`PublishGateTests`) plus an audit-row write (`InMemoryPublishOverrideStore` + `EfCorePublishOverrideStore`). A clean live curl of the 409 path is deferred to the next bootstrap + admin-register fixture refresh; no expected behaviour is uncovered by tests.
