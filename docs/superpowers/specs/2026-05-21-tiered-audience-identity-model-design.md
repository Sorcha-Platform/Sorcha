# Tiered-Audience Identity Model + Issuer Hardening (Spec A)

**Date:** 2026-05-21
**Status:** Design — pending review
**Depends on:** nothing (foundational)
**Consumed by:** Spec B — PWA authentication & signup parity (separate doc, to follow)

---

## 1. Problem

Sorcha's JWT `aud` claim does almost no work today, and one default is an active footgun:

- **No endpoint or authorization policy requires a specific `aud`.** Authorization is entirely role-based (user) or `token_type`/scope-based (service). The audience is validated at the bearer pipeline but every token shares the same audience list, so it isolates nothing.
- **The real principal boundary is the `token_type` claim** (`"user"` vs `"service"`) plus two separate vocabularies — **roles** for users, **scopes** for services — and two separate issuers (`TokenService` for users, `ServiceAuthService` client-credentials for services). That boundary works, but it lives in a claim, not the audience, so internal `/api/internal/*` endpoints authenticate *any* validly-signed token and only then check `token_type`.
- **`sorcha:citizen-wallet` is effectively an unused constant** — even the enrol path mints `Audiences.FirstOrDefault()`, falling back to that string only if the array is empty. So "the PWA audience" and "the web audience" are already the same value in practice.
- **`JwtSettings.Issuer` defaults to `https://tenant.sorcha.io`** (`JwtAuthenticationExtensions.cs:45`). Any installation that never sets `Issuer` shares that issuer — a domain we don't own per-installation — which is "works by accident, insecure by default."

The cryptographic boundary that *actually* prevents cross-installation token use is the **per-installation symmetric HMAC signing key** (`SymmetricSecurityKey`, `JwtSettings.SigningKey`), reinforced by the validated `iss`. The audience contributes nothing to that today.

## 2. Goal

Promote the boundaries that already exist — and matter — into the `aud` claim, so cross-tier and cross-installation token use fails at **authentication/authorization**, not just by convention; and remove the shared-issuer default. Specifically:

1. A four-tier, installation-namespaced audience taxonomy.
2. A single rule for selecting a token's tier at issuance (cold login *and* context switch).
3. Every token-issuance path made tier-aware.
4. Precise per-tier claim sets.
5. Issuer hardening: no shared default; fail-closed; `InstallationName` as the single source of truth.

## 3. Non-goals (explicitly out of scope for Spec A)

- **Migration / back-compat.** Pre-release; n1 is dev/demo. Roll out as a coordinated config change — existing tokens simply expire and everyone re-authenticates. No shims.
- **Asymmetric signing.** We retain symmetric HMAC. Moving to an asymmetric scheme (issuer holds a private key; verifiers hold only the public key/JWKS, so only the issuer can mint) is a stronger defense-in-depth posture and is logged as a **separate future initiative**, not part of this spec.
- **Per-service audiences** (`wallet` vs `blueprint` vs `register`). A different axis (which API, not which principal); separate initiative.
- **The PWA auth/signup UX** (Spec B). This spec only guarantees B's dependencies (see §10).

## 4. Defense-in-depth layering (which layer is load-bearing)

| Layer | Isolates | Load-bearing? |
|-------|----------|---------------|
| **HMAC signing secret** (per-installation, `JwtSettings.SigningKey`) | Cross-installation forgery — A's token fails signature at B | **Primary** |
| **`iss`** (per-installation, validated) | Declares + checks origin installation | Secondary structural check |
| **Audience prefix** `{installation}:` | Cross-installation replay (redundant with `iss`; cheap belt-and-braces + log clarity) | Defense-in-depth |
| **Audience suffix** `:consumer / :platform / :service / :enrol-session` | **Trust tier** — the new, genuinely valuable separation | **Primary for tier** |
| **Roles** (user tokens) / **scopes** (service tokens) | Capability within a tier | Unchanged |

The audience is explicitly **not** marketed as the cross-installation boundary — the signing key is. The audience's job is (a) tier separation and (b) cheap installation-scoping on top of the key.

## 5. The audience taxonomy

Four tiers, each namespaced by installation:

```
{installation}:consumer        citizen / holder        web + PWA      separated by: (none — minimal)
{installation}:platform        admin/designer/auditor  web/admin      separated by: roles + org_id
{installation}:service         S2S / infrastructure    internal       separated by: scopes
{installation}:enrol-session   one-time device pairing  enrol redeem   separated by: scope:enrol + single-use JTI
```

`{installation}` is the configured `InstallationName`, default **`sorcha`**. So a default dev box issues `sorcha:consumer`, a white-label deployment issues `acme:consumer`, n1 issues `n1:consumer`, etc.

**Single source of truth.** A new helper in `Sorcha.ServiceDefaults` — `SorchaAudiences` — derives all four strings from the installation name and exposes:
- `SorchaAudiences.For(tier)` → the one audience string (used at issuance).
- `SorchaAudiences.All` → the four-string set (used as bearer `ValidAudiences`).
- A `Tier` enum: `Consumer | Platform | Service | EnrolSession`.

Both issuance and validation reference this helper; no service hand-builds audience strings.

## 6. Validation model — authenticate broad, authorize narrow

The current per-service `ValidAudiences` is too coarse: a single service (e.g. Tenant) hosts both consumer (`/me/*`) and platform (`/platform/*`) and internal (`/api/internal/*`) endpoints. So:

- **Authentication (bearer pipeline):** `ValidAudiences = SorchaAudiences.All` for the installation. A token authenticates iff it is signed by this installation's key, has this installation's `iss`, and its `aud` is one of the four installation tiers. This rejects cross-installation tokens (belt-and-braces with the key) and malformed audiences.
- **Authorization (per-endpoint policy):** the tier suffix is enforced by policy, mirroring how `RequireService` already works:
  - `RequireConsumerAudience` — `aud == {installation}:consumer`
  - `RequirePlatformAudience` — `aud == {installation}:platform`
  - `RequireService` — extended to also assert `aud == {installation}:service` (today it checks only `token_type == "service"`; we keep that and add the audience check).
  - `enrol-session` keeps its existing dedicated validation (single-use JTI).

Consequence — clean dual-role behaviour: a `:platform` token hitting a `RequireConsumerAudience` endpoint is **rejected** (the admin must switch to consumer context, which re-mints), and vice versa. The audience suffix becomes the tier gate; roles/scopes remain the capability gate *within* a tier.

**Endpoint classification.** Every authenticated endpoint is classified consumer / platform / service. The implementation plan enumerates them; the principle: wallet + citizen-facing (`/api/v1/wallet/*`, `/me/*` consumer reads, persona) → consumer; admin/designer/org-management/platform → platform; `/api/internal/*` → service. A documented default for anything unclassified (recommend: `RequirePlatformAudience` as the conservative default, so nothing silently accepts a consumer token).

## 7. Tier selection at issuance (the rule)

A single function decides the minted tier for **every** issuance path:

```
mintedTier = requestedTier  ∩  entitledTiers(user)
```

- **`requestedTier`** — derived from the auth entry, defaulting safely:
  - An explicit `tier`/`audience` request parameter on the auth call, **or**
  - the `returnTo` target (a `/wallet/...` or allow-listed consumer host ⇒ `Consumer`; an admin/designer surface ⇒ `Platform`), **or**
  - default **`Consumer`** when nothing is specified (the lowest-privilege tier — fail-safe).
- **`entitledTiers(user)`** — what the user is allowed to obtain:
  - `Consumer` — any authenticated human (everyone can hold a wallet).
  - `Platform` — only users with a platform role (`Administrator | Designer | Auditor | SystemAdmin`) **in the active org context**.
  - `Service` / `EnrolSession` — never selectable by this human-login path; minted only by their dedicated issuers.
- If `requestedTier` is not in `entitledTiers`, **reject** (don't silently downgrade — an explicit `:platform` request from a non-admin is an error, not a consumer login).

Context switch (`/api/auth/switch-org`) re-runs the same rule with the new context, re-minting the token at the appropriate tier. This is the dual-role path: the same human gets a `:consumer` token on the wallet and a `:platform` token when administering their org.

## 8. Per-tier claim sets

Minted by the resolver; each issuance path populates the same shape for its tier.

**`:consumer`** (citizen / holder)
```
sub, email, platform_user_id, token_type: "user", aud: {installation}:consumer
```
Deliberately **omits `org_id` and roles** — a consumer token is powerless against platform/admin surfaces by construction (that is the DiD point). `platform_user_id` is present (Wallet Service endpoints + `WalletHub` group assignment require it). A citizen who is *also* an org member still gets a clean consumer token here; their platform capabilities arrive only via a `:platform` token in platform context.

**`:platform`** (admin / designer / auditor / org operator) — the existing user-token shape
```
sub, email, platform_user_id, org_id, roles[], wallet_address?, token_type: "user", aud: {installation}:platform
```

**`:service`** — the existing service-token shape
```
client_id, service_name, scope[], delegated_user_id?, delegated_org_id?, token_type: "service", aud: {installation}:service
```

**`:enrol-session`** — unchanged
```
sub, scope: "enrol", pair_mode, aud: {installation}:enrol-session, single-use JTI
```

**Refresh tokens** carry the same tier as the access token they refresh, so a refresh can only re-mint within its tier.

## 9. Issuer hardening

- **Remove the `https://tenant.sorcha.io` default** from `JwtSettings.Issuer`.
- Resolution order:
  1. Explicit `JwtSettings:Issuer` → use it (operators who own a domain may set a URL).
  2. Else if `InstallationName` set → derive a **non-domain, installation-unique** identifier, default `urn:sorcha:{InstallationName}` (avoids implying a resolvable/owned domain — the footgun the user called out).
  3. Else → **fail-closed at startup** in Production/Staging (throw, mirroring the existing `SigningKey` requirement at `JwtAuthenticationExtensions.cs:160`). In Development, fall back to `urn:sorcha:dev-local` (clearly local, never a real domain).
- `ValidateIssuer` stays `true`; `ValidIssuer` = the resolved issuer.
- `InstallationName` thus drives **both** issuer and audience prefix — one knob, no way to set them inconsistently.

## 10. What this guarantees for Spec B (dependency contract)

Spec B (PWA signup/sign-in parity via server redirect) can rely on, without amending Spec A:

1. **Every** server-side auth flow can mint a `:consumer` token: `TokenService` (login / verify-2fa / refresh / switch-org), signup completion, `SocialCallback`, `OidcCallback`, and `EnrolSessionService`. (§7 makes them all tier-aware via the shared resolver.)
2. A `returnTo=/wallet/...` (or consumer host) entry yields `requestedTier = Consumer`, so the token coming back to the PWA is `:consumer`. (§7)
3. The consumer token carries `platform_user_id` and the claim set the Wallet Service + `WalletHub` need. (§8)
4. The Wallet Service and consumer web surfaces validate `{installation}:consumer`. (§6)

B remains purely PWA-side: the signup/sign-in landing UI, the fragment-token handler that lands the returned token in IndexedDB, the startup-gate decision, and reuse of the existing `Auth:ReturnToAllowlist`.

## 11. Components & boundaries

| Unit | Responsibility | Depends on |
|------|----------------|------------|
| `SorchaAudiences` (ServiceDefaults) | Derive the 4 audience strings + set + `Tier` enum from installation name | `JwtSettings.InstallationName` |
| `JwtSettings` + `JwtAuthenticationExtensions` | Resolve issuer (no default, fail-closed), set bearer `ValidAudiences = SorchaAudiences.All` | `SorchaAudiences` |
| `TierResolver` (Tenant.Service) | `requestedTier ∩ entitledTiers(user)` → minted `Tier`; reject on over-request | `UserRole`, active context |
| `TokenService` | Accept a `Tier`; stamp the per-tier claim set + audience | `SorchaAudiences`, `TierResolver` |
| `EnrolSessionService` | Mint `:consumer` access tokens on redeem; `:enrol-session` on mint | `SorchaAudiences` |
| `SocialCallback` / `OidcCallback` | Pass `requestedTier` (from `returnTo`) into `TokenService` | `TierResolver`, `TokenService` |
| Authorization policies | `RequireConsumerAudience`, `RequirePlatformAudience`, `RequireService` (+ audience) | `SorchaAudiences` |

## 12. Testing strategy

- **`SorchaAudiences`** — unit: prefix derivation, default `sorcha`, override.
- **Issuer resolution** — unit: explicit wins; InstallationName derives `urn:sorcha:{name}`; missing both throws in Production, dev-local in Development.
- **`TierResolver`** — unit: consumer for anyone; platform only with a platform role; over-request (`Platform` requested by a roleless user) rejected; default-to-consumer.
- **`TokenService`** — unit: each tier stamps the correct `aud` + claim set; consumer omits `org_id`/roles; refresh preserves tier.
- **Authorization policies** — unit/integration: `:consumer` token rejected at a `RequirePlatformAudience` endpoint and vice versa; `:service` required on an `/api/internal/*` endpoint; a user token rejected there even with a role.
- **Cross-installation** — unit: a token signed with installation A's key/`iss` fails validation under installation B's settings.
- **Issuance-path coverage** — integration: login, verify-2fa, social callback, OIDC callback, enrol redeem each produce a `:consumer` token when consumer is requested.

## 13. Security considerations

- **HMAC implication (accepted for now):** symmetric signing means every service in an installation holds the secret and could in principle *mint* tokens — any compromised service is a forgery surface. The audience/issuer work does not change this. Asymmetric signing is the real mitigation and is a logged future initiative (§3).
- **Fail-safe defaults:** unknown `requestedTier` → consumer (lowest privilege); unclassified endpoint → `RequirePlatformAudience` (most restrictive for human tokens); missing issuer → fail-closed.
- **No central IdP across installations.** Each installation self-issues. Org-SSO/OIDC is *inbound* federation (external IdP → an installation-scoped token); it never yields a cross-installation token. (The peer network shares ledger data over its own authenticated gRPC channel, not user JWTs.)

## 14. Open questions

- Concrete name for the requested-tier transport on the auth API — explicit `tier` body/query param vs deriving solely from `returnTo`. (Leaning: derive from `returnTo` with an optional explicit override; settle in the plan.)
- Final issuer default format — `urn:sorcha:{InstallationName}` proposed; confirm during plan if any consumer of `iss` expects a URL.
