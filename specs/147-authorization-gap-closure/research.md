# Phase 0 Research: Authorization-gap closure

All decisions below are grounded in the existing codebase (F136 auth primitives and the existing test harness). No NEEDS CLARIFICATION remained from the spec.

## R1 — How to express an "OR across tiers" gate without breaking service tokens

**Context**: Both `CanManageBlueprints` (H2) and `CanRecoverSystemWallet` (H1-recover) must admit *either* a service-tier caller *or* a specific human-tier caller. Listing multiple ASP.NET policies on an endpoint **AND**s them; a single `RequireAssertion` lambda has no DI access to resolve `SorchaAudiences`.

**Decision**: Implement each as a custom `IAuthorizationRequirement` + `AuthorizationHandler<T>` that injects the DI singleton `SorchaAudiences` and tests audiences via `AuthorizationPolicyExtensions.HasTierAudience(user, audiences, tier)`. The handler `context.Succeed(requirement)` on either branch; it never calls `Fail()` (so it composes cleanly). This mirrors `TierAudienceAuthorizationHandler` (`src/Common/Sorcha.ServiceDefaults/Auth/TierAudienceAuthorizationHandler.cs`) exactly.

**Rationale**: Resolves the expected audience string from the configured `InstallationName` at request time (FR-012) — never baked in. Single requirement = the policy can't be split/forgotten. Matches the established F136 pattern, so it's idiomatic and reviewable.

**Alternatives considered**:
- *Compose two policies on the endpoint* — ASP.NET ANDs them; cannot express OR. Rejected.
- *`RequireAssertion` reading `aud` claims directly* — would have to hard-code `"sorcha:platform"`/`":service"`, breaking per-installation namespaces (FR-012). Rejected.
- *A shared `ServiceOrPlatformRequirement` in ServiceDefaults* — the two gates differ (Blueprint keys on `org_id`; recover keys on the `Administrator` role), so a shared abstraction would be parameter-heavy for two call sites. YAGNI — keep service-local; revisit if a third caller appears.

## R2 — Placement & registration of the new requirement/handler types

**Decision**: Service-local. Blueprint → `src/Services/Sorcha.Blueprint.Service/Authorization/`; Wallet → `src/Services/Sorcha.Wallet.Service/Authorization/`. Register the handler in the service's existing `Add{Service}Authorization` via `services.AddSingleton<IAuthorizationHandler, THandler>()` (handlers are stateless; `SorchaAudiences` is an immutable singleton) and define the named policy with `policy.AddRequirements(new TRequirement())`.

**Rationale**: Keeps the authz logic next to the service it guards (Principle I — no upward deps; the types depend only on the downward `ServiceDefaults.Auth`). `SorchaAudiences` is already registered as a singleton by `AddSorchaAuthorizationPolicies()` (called first inside each `Add{Service}Authorization`), so the handler's dependency is satisfied.

**Alternatives considered**: Putting the requirement/handler in `Extensions/AuthenticationExtensions.cs` — works, but conflates config wiring with the requirement type; a dedicated `Authorization/` folder is clearer.

## R3 — Test strategy

**Decision**: Two layers.

1. **Policy-evaluation tests (primary)** — mirror `tests/Sorcha.ServiceDefaults.Tests/AuthorizationPolicyExtensionsTests.cs`: build a `ServiceProvider` with `services.AddLogging(); services.Add{Service}Authorization();`, resolve `IAuthorizationService`, and `AuthorizeAsync(principal, policyName)` against the full caller matrix. Default installation is `"sorcha"` (no `IConfiguration` → `SorchaAudiences(null)` → `"sorcha"`), so audience claims are `"sorcha:consumer|platform|service"`. This exercises the **real** authorization pipeline including the registered tier handler.
2. **Endpoint-metadata regression tests** — for the Wallet system-wallet + pending-applications endpoints (no `WebApplicationFactory` exists in `Sorcha.Wallet.Service.Tests`): build a minimal host, call the `Map*Endpoints` extension, enumerate `EndpointDataSource.Endpoints`, locate the routes by pattern, and assert (a) **no** `IAllowAnonymous` metadata and (b) an `IAuthorizeData` carrying the expected policy name. This is the direct SC-005 guard against `AllowAnonymous` reintroduction. If mapping the full endpoint set proves to need unavailable services at map-time, fall back to asserting the policy via the route group in isolation.

**Rationale**: Policy-eval tests prove the security *logic* fast and deterministically (matches the codebase's existing approach). Metadata tests prove the *wiring* (the actual H1 defect was wiring — `AllowAnonymous`). Blueprint H2 is fully proven by the policy-eval test because the fix is the policy definition, not endpoint wiring (the endpoints already reference `CanManageBlueprints`).

**Alternatives considered**: Full `WebApplicationFactory` 401/403/2xx tests for Wallet — heavy (WalletManager + DB deps) for what is an authz-pipeline concern that short-circuits before the handler; not worth standing up a new factory. Blueprint already has factories but the policy-eval test is sufficient and cheaper.

## R4 — FR-010 verification (Tenant `RequireSystemAdmin` usages)

**Finding (verified)**: All four usages — `PlatformManagementEndpoints.cs:32,39`, `PlatformOrgEndpoints.cs:33,39,45`, `PlatformSettingsEndpoints.cs:27` — are platform-administration endpoints and **all already compose `("RequireSystemAdmin", "RequirePlatformAudience")`**. None need role-only-any-org semantics. **Conclusion**: deleting the duplicate role-only registration is safe; the shared org-scoped definition (system-admin-org `00000000-0000-0000-0000-000000000001` AND `SystemAdmin` role) is the intended behaviour for every site.

## R5 — Role-claim mechanics for the recover handler

**Finding (verified via `AuthorizationPolicyExtensionsTests.RequireAdministrator_*`)**: role checks use `ClaimTypes.Role` and `RequireRole("SystemAdmin","Administrator")` / `context.User.IsInRole(...)` work in policy-evaluation tests when the principal is built with `new Claim(ClaimTypes.Role, "Administrator")`. The recover handler's human branch will test `context.User.IsInRole("Administrator") || context.User.IsInRole("SystemAdmin")` AND `HasTierAudience(Platform)`. (The shared `RequireAdministrator` policy accepts both `SystemAdmin` and `Administrator`; the recover handler mirrors that role set.)

## R6 — Caller-flow confirmation (no legitimate regression)

**Findings (verified)**:
- `create` (`POST /api/v1/wallets/system`) sole caller: Validator Service `SystemWalletInitializer` → consolidated `WalletServiceClient.CreateOrRetrieveSystemWalletAsync`, which sets a `:service` token via `ServiceClientAuthHelper`. `RequireService` passes unchanged.
- `recover` (`POST /api/v1/wallets/system/recover`) sole caller: CLI `sorcha system-register import-validator-key`, which logs in (`authService.GetAccessTokenAsync`) and sends a platform-tier admin token. The consolidated `RecoverSystemWalletAsync` has **no** server-side caller. `CanRecoverSystemWallet`'s admin+platform branch passes; the `:service` branch is forward-looking.
- The group policy `CanManageWallets` (= `org_id OR service`) still applies after `AllowAnonymous` is removed; both legitimate callers satisfy it (service token / admin with org_id), so the layered check is defense-in-depth with no false-deny.

## R7 — Test-runner constraint

Microsoft.Testing.Platform ignores `dotnet test --filter` (MTP0001). Build + test are scoped to each affected service's **whole** test project: `Sorcha.Wallet.Service.Tests`, `Sorcha.Blueprint.Service.Tests`, `Sorcha.Tenant.Service.Tests`.
