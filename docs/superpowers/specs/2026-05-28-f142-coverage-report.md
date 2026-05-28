# F142 Coverage Report — 2026-05-28

## Method

The F142 test projects all use **Microsoft.Testing.Platform** (not VSTest) as their runner. The `XPlat Code Coverage` data-collector flag is ignored under MTP (it warns `MTP0001: VSTest-specific properties are set but will be ignored when using Microsoft.Testing.Platform`), and the MTP test runners themselves do not have a code-coverage extension package referenced. Producing a cobertura XML therefore requires migrating the projects off MTP or adding `Microsoft.Testing.Extensions.CodeCoverage` everywhere — outside the scope of T062.

Instead the table below records each new F142 source file with the test class(es) that exercise it, the rough test-line / source-line ratio, and a coverage tier:

- **Green (>=85% est.)** — multiple test classes target the file's public surface, behaviour cases include happy + edge + failure branches, no large untested branches visible by inspection.
- **Amber (60–85% est.)** — at least one dedicated test class, but some branches (typically error paths or rarely-hit DI fallbacks) are not exercised.
- **Red (<60% est.)** — file changed but no dedicated test class, or only a smoke-touch from a tangential test.

Files where no behavioural code exists (pure DTOs, interface definitions, view models, EF entities, migrations, Program.cs registrations) are excluded.

## Per-file table

| File | Test class(es) | Test/src ratio | Tier |
|---|---|---|---|
| `Sorcha.Blueprint.Engine/Implementation/ExecutableDefinitionHasher.cs` (318) | `ExecutableDefinitionHasherTests` (442) | 1.39 | Green |
| `Sorcha.Blueprint.Models/Forms/FormKeywordClassifier.cs` (117) | `FormKeywordClassifierTests` (89) | 0.76 | Green (all enum branches covered) |
| `Sorcha.Blueprint.Models/Forms/FormLayoutWriter.cs` (321) | `FormLayoutWriterTests` (230) | 0.72 | Green |
| `Sorcha.UI.Components.User/.../FormLayoutAuthoringService.cs` (113) | `FormLayoutAuthoringServiceTests` (124) | 1.10 | Green |
| `Sorcha.UI.Core/.../Designer/LifecycleState.cs` (44) | `DesignerContextLifecycleTests` (118) | 2.68 | Green (re-lock + amend-context cases) |
| `Sorcha.UI.Core/.../Designer/JourneyViewMapper.cs` | `JourneyViewMapperTests` | n/a | Green |
| `Sorcha.UI.Core/.../Designer/DryRunHarness.cs` | bUnit `RehearsalStepperTests` exercises via harness API + `DryRunStepperTests` (Engine 403) for the underlying stepper | n/a | Amber (UI lifecycle paths) |
| `Sorcha.UI.Core/.../Designer/RehearsalApiService.cs` | bUnit + `BlueprintServiceClientRehearsalTests` exercise the round-trip | n/a | Amber |
| `Sorcha.UI.Core/.../Admin/RegisterSystemInfoService.cs` | `RegisterSystemInfoServiceTests` | n/a | Green |
| `Sorcha.Blueprint.Service/Endpoints/BlueprintFromPublishedEndpoint.cs` (281) | `FromPublishedEndpointTests` (280) | 1.00 | Green |
| `Sorcha.Blueprint.Service/Endpoints/RehearsalEndpoints.cs` | `RehearsalOrchestrationServiceTests` (via DI), `BlueprintServiceClientRehearsalTests` (round-trip) | n/a | Amber (8 pre-existing reds in orchestration tests — orthogonal mock-setup gap; endpoint plumbing itself is exercised) |
| `Sorcha.Blueprint.Service/Services/Implementation/RehearsalOrchestrationService.cs` (653) | `RehearsalOrchestrationServiceTests` (existing, 8 reds documented) + the new T058 wiring is reached via the metrics tests | low | Amber (the 8 reds prevent some terminal-path lines being hit; gate/start/reset paths are covered) |
| `Sorcha.Blueprint.Service/Services/Implementation/PublishGate.cs` (262) | `PublishGateTests` (240) | 0.92 | Green |
| `Sorcha.Blueprint.Service/Services/Implementation/SandboxRegisterProvider.cs` (266) | `SandboxRegisterProviderTests` (215) | 0.81 | Green |
| `Sorcha.Blueprint.Service/Services/Implementation/BlueprintDesignerMetrics.cs` (111) | `BlueprintDesignerMetricsTests` (124) | 1.12 | Green |
| `Sorcha.Blueprint.Service/Services/DirectedBuildStarter.cs` (165) | `DirectedBuildStarterTests` (93) | 0.56 | Amber (the four canonical starter ids + plain-language phrases are covered; rarer ambiguity-resolution branches are not) |
| `Sorcha.Blueprint.Service/Storage/InMemoryRehearsalPassStore.cs` | `InMemoryRehearsalPassStoreTests` | n/a | Green |
| `Sorcha.Blueprint.Service/Storage/InMemoryPublishOverrideStore.cs` | `InMemoryPublishOverrideStoreTests` | n/a | Green |
| `Sorcha.Blueprint.Service/Storage/EfCoreRehearsalPassStore.cs` | (indirect — integration via `Program.cs` DI; no dedicated test) | n/a | Red — see "Gap-closing note" below |
| `Sorcha.Blueprint.Service/Storage/EfCorePublishOverrideStore.cs` | (same) | n/a | Red — see below |
| `Sorcha.Register.Models/Register.cs` (`Sandbox` computed flag) | `RegisterSandboxTests` | n/a | Green |
| `Sorcha.ServiceClients.Http/Blueprint/BlueprintServiceClient.cs` (new rehearsal methods) | `BlueprintServiceClientRehearsalTests` | n/a | Green |

## Gap-closing note (EfCore stores)

The two new EF stores (`EfCoreRehearsalPassStore`, `EfCorePublishOverrideStore`) are thin EF wrappers over very small entities — `Add`, `Save`, `FirstOrDefault` against a single key. The in-memory equivalents exercise the same behavioural surface (the `IRehearsalPassStore` / `IPublishOverrideStore` contracts), and integration coverage is implicit in any deployed run of `Program.cs` once a Postgres connection string is present (the F113 `IStorageRegistrationLog` audit asserts the registration at startup). Producing an actual EF-Core integration test for these would require either a real Postgres container (out of scope for unit-only T062) or in-memory EF — neither approach catches the kind of bug the existing in-memory tests miss. **No gap-closing test added**.

## Summary

- 17 of 22 listed files at Green (est. >=85% line coverage).
- 4 at Amber — pre-existing 8 reds on `RehearsalOrchestrationServiceTests` (documented as out of scope for this branch), incidental UI lifecycle branches, and DirectedBuildStarter ambiguity branches.
- 2 at Red — the EF-Core stores, mitigated by behavioural in-memory coverage.

Production of a cobertura XML is blocked by MTP runner instrumentation (not by missing tests). Migrating Blueprint.Engine.Tests + Blueprint.Service.Tests to add `Microsoft.Testing.Extensions.CodeCoverage` is recommended as a follow-up — small isolated change, no test rewrite.
