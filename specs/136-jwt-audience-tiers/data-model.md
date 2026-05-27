# Phase 1 Data Model: Tiered-Audience JWT Identity Model

This feature introduces **no new persistent (database) entities**. Its "model" is a set of token shapes, configuration values, and an in-process derivation. They are documented here as the authoritative structures the implementation must produce and validate.

## Entity: `Tier` (enum)

The principal class a token belongs to.

| Value | Meaning | Principal | Wire suffix |
|-------|---------|-----------|-------------|
| `Consumer` | Citizen / wallet holder | human | `:consumer` |
| `Platform` | Org admin / designer / auditor / operator | human | `:platform` |
| `Service` | Service-to-service / infrastructure | machine | `:service` |
| `EnrolSession` | One-time device pairing | transient | `:enrol-session` |

- Ordering for fail-safe defaults: Consumer is the lowest-privilege human tier.

## Entity: `SorchaAudiences` (derivation, single source of truth)

- **Input**: `JwtSettings.InstallationName` (string, default `"sorcha"`).
- **Derives**:
  - `For(Tier)` → `"{InstallationName}:{suffix}"` (e.g. `sorcha:consumer`, `acme:platform`).
  - `All` → the set of all four tier audiences for the installation (used as bearer `ValidAudiences`).
- **Invariants**: deterministic; identical result wherever called; no other code constructs an audience string.

## Entity: `JwtSettings` (configuration — changed shape)

| Field | Before | After |
|-------|--------|-------|
| `InstallationName` | optional, drove derived issuer/audience defaults | **the single source of truth** for issuer + audience namespace; default `"sorcha"` |
| `Issuer` | default `"https://tenant.sorcha.io"` | **no shared default** — explicit, else `urn:sorcha:{InstallationName}`, else fail-closed (prod) / `urn:sorcha:dev-local` (dev) |
| `Audiences` | configured array (per-origin + dead `sorcha:citizen-wallet`) | **derived** = `SorchaAudiences.All` (not hand-configured) |
| `SigningKey` | required in prod (HMAC) | unchanged |
| `ValidateIssuer` / `ValidateAudience` / `ValidateIssuerSigningKey` | true | unchanged (still true) |

## Entity: Access token claim sets (per tier)

Common to all: `sub`, `iss` (resolved issuer), `aud` (`SorchaAudiences.For(tier)`), `exp`/`iat`/`nbf`, signature (HMAC).

| Tier | `token_type` | Additional claims | Notably absent |
|------|--------------|-------------------|----------------|
| **Consumer** | `user` | `email`, `platform_user_id` | **`org_id`, roles** (omitted by design — inert on platform surfaces) |
| **Platform** | `user` | `email`, `platform_user_id`, `org_id`, `roles[]`, `wallet_address?` | — |
| **Service** | `service` | `client_id`, `service_name`, `scope[]`, `delegated_user_id?`, `delegated_org_id?` | human identity claims |
| **EnrolSession** | (n/a) | `scope: "enrol"`, `pair_mode`, single-use JTI | general-access claims |

## Entity: Refresh token (changed shape)

- Adds a `tier` claim. Existing claims (`sub`, `org_id?`, `platform_user_id`, `token_use: "refresh"`) unchanged.
- **Rule**: refresh re-mints an access token of the **same** `tier`. No DB column added — `tier` is a JWT claim.

## Entity: `TierEntitlement` (derived, in-process)

`entitledTiers(user, activeContext)`:

- `Consumer` — every authenticated human.
- `Platform` — iff the user holds a platform role (`Administrator | Designer | Auditor | SystemAdmin`) in the **active org context**.
- `Service` / `EnrolSession` — never selectable via a human-login path; minted only by their dedicated issuers.

## Entity: Tier-selection result

`mintedTier(requestedTier, user, activeContext)`:

```
requested = requestedTier ?? Consumer            // default lowest-privilege
if requested ∉ entitledTiers(user, ctx): REJECT  // no silent downgrade
return requested
```

- Inputs at issuance: `requestedTier` (from auth entry, §contracts/auth-entry-tier-request), `user`, `activeContext` (org).
- Output: the `Tier` stamped into the access token's `aud` and used to shape the claim set.

## State transitions

- **Context switch** (switch-org): re-runs `mintedTier` with the new active context → may move Platform↔Consumer; re-mints access (and refresh) at the new tier.
- **Refresh**: preserves tier (no transition).
- **Enrol redeem**: issues a `Consumer` access token (transition from `EnrolSession` one-time token to a `Consumer` session).

## Authorization policy → tier (validation side)

| Policy | Grants when `aud` == | Applied to |
|--------|----------------------|------------|
| `RequireConsumerAudience` | `{installation}:consumer` | consumer/wallet endpoints |
| `RequirePlatformAudience` | `{installation}:platform` | admin/designer/org/platform endpoints + unclassified default |
| `RequireService` (extended) | `{installation}:service` **and** `token_type==service` | `/api/internal/*` |
| (enrol-session validation) | `{installation}:enrol-session` + single-use JTI | enrol redeem only |
