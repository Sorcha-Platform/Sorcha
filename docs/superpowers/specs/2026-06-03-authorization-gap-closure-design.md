# Authorization-gap closure — design

**Date:** 2026-06-03
**Initiative:** Security hardening (from the 2026-06-02 architecture review)
**Sub-project:** 2 of N — Authorization-gap closure
**Branch:** `147-authorization-gap-closure`
**Source findings:** `docs/reviews/2026-06-02-architecture-review.md` §2 (H1, H2, LOW), §7, §8

---

## 1. Problem

Four sensitive endpoints (or policies) accept callers they should refuse. The unifying defect: **the authorization decision is not enforced in code at the endpoint** — it relies on a comment, on the gateway (which is only `RequireAuthenticated`), or on a policy that a per-endpoint omission silently weakens.

| # | Finding | Today | Who gets in that shouldn't |
|---|---------|-------|-----------------------------|
| H1 | System-wallet create/recover are `AllowAnonymous` | `POST /api/v1/wallets/system` and `/system/recover` carry `.AllowAnonymous()` with comments asserting an enforcement that isn't in code | Any authenticated token via the gateway; **fully unauthenticated on the internal network**. `recover` imports a BIP39 mnemonic → an attacker can seat an attacker-controlled validator docket-signing wallet. |
| H2 | `CanManageBlueprints` passes on `hasOrgId OR isService` | Consumer/citizen tokens carry `org_id` (F136), so a citizen satisfies it on the bare-policy endpoints (`/api/blueprints` CRUD, `SchemaEndpoints`, `CredentialEndpoints`, `StatusListEndpoints`) | Consumer/citizen tokens reach blueprint & schema **authoring**. |
| F124 (LOW) | Pending-applications uses plain `.RequireAuthorization()` | `/api/v1/wallet/pending-applications` group lacks `RequireConsumerAudience` unlike every sibling citizen surface | A platform token can read/set a citizen's notice. |
| LOW | Tenant re-registers `RequireSystemAdmin` role-only | `AddTenantAuthorization` calls `AddSorchaAuthorizationPolicies()` (org-scoped `RequireSystemAdmin`) then **re-adds** it role-only; last-write-wins | A `SystemAdmin` in *any* org clears `platform-*` gateway routes — the system-admin-org constraint is dropped. |

### Guiding principle

A sensitive endpoint must enforce its own tier/role authorization **in code**, never relying on the gateway and never on a comment. Where a rule can be forgotten per-endpoint, push it **into the policy definition** so omission is impossible.

---

## 2. Design

### 2.1 H1 — system-wallet endpoints (`Sorcha.Wallet.Service`)

Both endpoints live under `walletGroup` (`/api/v1/wallets`, group policy `CanManageWallets`) but each overrides the group with `.AllowAnonymous()` — that override is the entire hole. **Remove `.AllowAnonymous()` from both** (which restores in-code authz independent of the gateway), and attach the right policy:

- **`POST /system` (create)** → `.RequireAuthorization(AuthorizationPolicies.RequireService)`.
  Sole caller: Validator Service via the consolidated `WalletServiceClient`, which already attaches a `:service` token (`ServiceClientAuthHelper`). No caller change.

- **`POST /system/recover` (BIP39 import)** → new policy **`CanRecoverSystemWallet`**:

  > *(`token_type == service` **AND** carries `:service` audience)* **OR** *(`Administrator`/`SystemAdmin` role **AND** carries `:platform` audience)*

  - The admin CLI's `sorcha system-register import-validator-key` logs in (`sorcha auth login`) and sends a platform-tier admin token → **passes** the second branch.
  - A future service automation → **passes** the first branch.
  - A consumer/citizen token → **refused** (no `:service`, not admin, and consumer-tier audience).
  - Anonymous → refused (no `AllowAnonymous`).
  - The existing **409-on-exists** guard in the handler (`WalletEndpoints.cs:1768-1784`, "Refusing to recover … one already exists") is **kept** as belt-and-braces. It only stops *overwriting* an existing wallet; the auth gate is what closes the **fresh-install empty-window race** and the unauthenticated-internal-network reach.

  Because the OR spans tiers and a `RequireAssertion` lambda has no DI access, `CanRecoverSystemWallet` is implemented as a small custom `IAuthorizationRequirement` + `AuthorizationHandler<T>` that injects the DI singleton `SorchaAudiences` and tests audiences via `AuthorizationPolicyExtensions.HasTierAudience`. This mirrors the F136 `TierAudienceAuthorizationHandler` pattern exactly — the expected audience string is resolved at request time from the configured `InstallationName`, never baked in.

  Note on the group policy: after removing `AllowAnonymous`, these two endpoints are also subject to the group's `CanManageWallets` (= `org_id OR service`). Both valid callers satisfy it (service token, or admin with `org_id`), so the layered check is defense-in-depth with no false-deny.

### 2.2 H2 — `CanManageBlueprints` (`Sorcha.Blueprint.Service`)

Fix **in the policy definition**, not per-endpoint, so the gate can't be omitted on a future endpoint. Redefine `CanManageBlueprints` (in `AddBlueprintAuthorization`) via a custom `BlueprintManagementRequirement` + handler (injects `SorchaAudiences`):

> *(`token_type == service` **AND** carries `:service` audience)* **OR** *(`org_id` present **AND** carries `:platform` audience)*

- Auto-fixes every bare site at once with **zero endpoint edits**: `Program.cs:702` (`/api/blueprints` CRUD), `SchemaEndpoints.cs:93/105/117/128/138`, `CredentialEndpoints.cs:33`, `StatusListEndpoints.cs:51`.
- The two siblings that already compose `+RequirePlatformAudience` (`RehearsalEndpoints.cs:39`, `BlueprintFromPublishedEndpoint.cs:69`) are **left untouched**. They keep a strict platform-only gate (the standalone `RequirePlatformAudience` still rejects service tokens there) — no behaviour change, the redundancy is harmless and documents intent.
- The service branch is **preserved** so legitimate service-to-service blueprint management still works; only the consumer-tier walk-in is closed.

### 2.3 F124 — pending-applications (`Sorcha.Wallet.Service`)

`PendingApplicationEndpoints.cs:24-26` group: replace plain `.RequireAuthorization()` with `.RequireAuthorization(AuthorizationPolicies.RequireConsumerAudience)`, matching every sibling citizen surface. Closes the platform-token-reads-citizen-notice gap. One-line change.

### 2.4 LOW — Tenant `RequireSystemAdmin` (`Sorcha.Tenant.Service`)

In `AddTenantAuthorization` (`AuthenticationExtensions.cs:151-152`), **delete** the duplicate role-only `options.AddPolicy("RequireSystemAdmin", …)` so the shared, org-scoped definition from `AddSorchaAuthorizationPolicies` (system-admin-org `00000000-0000-0000-0000-000000000001` **AND** `SystemAdmin` role) stands.

**Pre-condition to verify before deleting:** confirm every Tenant `RequireSystemAdmin` usage is a platform-management endpoint genuinely meant to be system-admin-org-scoped (the architecture's "Platform Management Endpoints — SystemAdmin only"). If any usage legitimately needs role-only-any-org behaviour, it gets its own named policy instead. Expectation: all usages are platform-management, so the delete is correct.

---

## 3. Testing (TDD)

Tests are written before the production change for each component.

- **Handler unit tests** (primary; fast; no host):
  - `BlueprintManagementAuthorizationHandler`: consumer (`org_id` + `:consumer`) → **deny**; platform admin (`org_id` + `:platform`) → allow; service (`token_type=service` + `:service`) → allow; `org_id` + no/`:consumer` audience → deny; no `org_id`, non-service → deny.
  - `CanRecoverSystemWallet` handler: service (+`:service`) → allow; admin role + `:platform` → allow; consumer → deny; admin role but `:consumer`/no `:platform` → deny; authenticated non-admin → deny.
- **Endpoint-metadata regression test**: assert `/api/v1/wallets/system` and `/system/recover` carry **no** `AllowAnonymousAttribute` and require their expected policy; assert the pending-applications group requires consumer audience. Guards against `AllowAnonymous` re-introduction. (Mechanism finalized in planning — minimal host enumerating `EndpointDataSource`, or handler-level assertion if the host route is heavier than the codebase norm.)
- **Tenant test**: a `SystemAdmin` in a **non-system** org is **denied** `RequireSystemAdmin` — the exact regression the duplicate caused; a system-admin-org `SystemAdmin` is allowed.

**Test-runner note (MTP):** Microsoft.Testing.Platform ignores `dotnet test --filter` (MTP0001). Build + test are scoped to each **affected service's test project** (`Sorcha.Wallet.Service.Tests`, `Sorcha.Blueprint.Service.Tests`, `Sorcha.Tenant.Service.Tests`), each running its whole suite.

---

## 4. Increments & delivery

One focused PR for the sub-project, four separable commits (built + tested against the affected test project before each commit):

1. **H1** — Wallet system-wallet create/recover gating (`RequireService` + new `CanRecoverSystemWallet`) + handler/metadata tests.
2. **H2** — Blueprint `CanManageBlueprints` folded into a tier-aware policy + handler tests.
3. **F124** — Wallet pending-applications `RequireConsumerAudience` + metadata test.
4. **LOW** — Tenant `RequireSystemAdmin` duplicate removal + denial test.

Push → open PR → merge on green **claude-review**. The full-solution `build-and-test` workflow is currently red across all PRs due to an unrelated revoked Refit 10.1.6 NuGet signing cert (NU3012 at restore); PRs stay UNSTABLE/mergeable and claude-review is the effective gate.

Documentation sync on merge: the `jwt` skill (policy catalogue), `sorcha-architecture` skill if any documented endpoint surface changes, and `docs/guides/AUTHENTICATION-SETUP.md` if policy semantics are documented there.

---

## 5. Out of scope (named, not silently dropped)

- **H3** (PWA-local verifier `requireIssuerSignature:false`) → sub-project 3 (verification-correctness).
- **M3** (OidcExchangeService issuer trust, PasskeyRecoveryService WebAuthn) → sub-project 3.
- Other §2 LOW items — token-revocation fail-open on Redis error (deliberate availability tradeoff / risk acceptance), stale `sorcha:citizen-wallet` dev config (dead config cleanup), default service-principal secrets in base appsettings (config/ops), anonymous-but-guarded bootstrap (already guarded). None are authorization-gating in code; they belong to Bucket B / deferred-docs, not this sub-project.

---

## 6. Key decisions (settled during brainstorming)

1. **Recover gate** = `RequireService` OR (`Administrator` + `:platform`), keeping the existing 409 guard — chosen over a deploy-time secret / one-shot disable because it reuses the platform's existing authz vocabulary and removing `AllowAnonymous` is what actually shuts both doors. (The 409-on-exists guard the operator might reach for already exists and does **not** close the empty-window race — the auth gate does.)
2. **F124 included** in this sub-project — same consumer⊥platform isolation theme as H2, one-line fix.
3. **H2 fixed in the policy**, not per-endpoint, so it can't be omitted again — the reviewer's explicit recommendation.
