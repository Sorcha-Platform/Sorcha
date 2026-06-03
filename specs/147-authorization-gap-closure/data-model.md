# Phase 1 Data Model: Authorization-gap closure

No persistent data, schema, or DTO changes. The "model" here is the authorization configuration: policies, the custom requirements/handlers, and the claim shapes they evaluate.

## Claim shapes evaluated (existing — not changed)

| Concept | Claim | Source |
|---------|-------|--------|
| Token type | `token_type` ∈ {`user`,`service`} | `TokenClaimConstants.TokenType` / `TokenTypeService` |
| Tier audience | `aud` = `{installation}:{consumer\|platform\|service\|enrol-session}` | `SorchaAudiences.For(tier)` |
| Org membership | `org_id` (non-empty) | `TokenClaimConstants.OrgId` |
| Role | `ClaimTypes.Role` ∈ {`SystemAdmin`,`Administrator`,…} | token role claims |

## Policies (after this feature)

| Policy | Service | Definition | Change |
|--------|---------|-----------|--------|
| `RequireService` | shared | `token_type==service` AND `aud`==`:service` | unchanged (reused for create) |
| `CanRecoverSystemWallet` | Wallet | *(`token_type==service` AND `:service`)* **OR** *(`IsInRole(Administrator\|SystemAdmin)` AND `:platform`)* | **NEW** |
| `CanManageWallets` | Wallet | `org_id` OR `service` | unchanged (group policy; still applies) |
| `CanManageBlueprints` | Blueprint | *(`token_type==service` AND `:service`)* **OR** *(`org_id` non-empty AND `:platform`)* | **REDEFINED** (was `org_id OR service`) |
| `RequireConsumerAudience` | shared | authenticated AND `:consumer` | unchanged (reused for pending-apps) |
| `RequireSystemAdmin` | shared (Tenant override removed) | `org_id`==system-admin-org AND `IsInRole(SystemAdmin)` | **Tenant duplicate deleted** so shared definition stands |

## New types

### `BlueprintManagementRequirement : IAuthorizationRequirement`
Marker requirement. No state.

### `BlueprintManagementAuthorizationHandler : AuthorizationHandler<BlueprintManagementRequirement>`
- Injects `SorchaAudiences`.
- Succeeds iff:
  - `token_type==service` AND `HasTierAudience(user, audiences, Tier.Service)`; **or**
  - user has a non-empty `org_id` claim AND `HasTierAudience(user, audiences, Tier.Platform)`.
- Never calls `Fail()`.

### `SystemWalletRecoveryRequirement : IAuthorizationRequirement`
Marker requirement. No state.

### `SystemWalletRecoveryAuthorizationHandler : AuthorizationHandler<SystemWalletRecoveryRequirement>`
- Injects `SorchaAudiences`.
- Succeeds iff:
  - `token_type==service` AND `HasTierAudience(user, audiences, Tier.Service)`; **or**
  - (`user.IsInRole("Administrator")` OR `user.IsInRole("SystemAdmin")`) AND `HasTierAudience(user, audiences, Tier.Platform)`.
- Never calls `Fail()`.

## Registration (DI)

Inside `AddBlueprintAuthorization` / `AddWalletAuthorization` (each already calls `AddSorchaAuthorizationPolicies()` first, which registers the `SorchaAudiences` singleton):

```csharp
services.AddSingleton<IAuthorizationHandler, BlueprintManagementAuthorizationHandler>();
// AddAuthorization options:
options.AddPolicy("CanManageBlueprints", p => p.AddRequirements(new BlueprintManagementRequirement()));
```

```csharp
services.AddSingleton<IAuthorizationHandler, SystemWalletRecoveryAuthorizationHandler>();
options.AddPolicy("CanRecoverSystemWallet", p =>
    p.RequireAuthenticatedUser().AddRequirements(new SystemWalletRecoveryRequirement()));
```

## Invariants

- INV-1: A consumer-tier token never satisfies `CanManageBlueprints` or `CanRecoverSystemWallet` (no `:platform`/`:service` audience, no role).
- INV-2: Existing service-to-service create and admin-CLI recover flows are unchanged (FR-004).
- INV-3: Audience comparison always goes through `SorchaAudiences` resolved from DI (FR-012) — no literal audience strings in handler code.
- INV-4: Handlers never `Fail()` — only `Succeed()` — so they compose with any other requirement on the same policy.
