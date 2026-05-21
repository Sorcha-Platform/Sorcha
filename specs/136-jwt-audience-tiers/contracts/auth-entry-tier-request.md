# Contract: How an auth entry conveys the requested tier

The tier a sign-in *requests* is derived from the authentication entry; the tier actually *minted* is `requestedTier ∩ entitlement` (see data-model).

## Derivation (no mandatory parameter)

`requestedTier` is resolved in this order:

1. **Explicit override** — an optional `tier` hint on the auth call (`consumer` | `platform`), used by callers that already know. Validated against the enum; unknown value → treated as absent.
2. **`returnTo` destination** — reusing the existing `Auth:ReturnToAllowlist` plumbing:
   - target path under the wallet mount (`/wallet/...`) or an allow-listed **consumer host** ⇒ `Consumer`
   - target an admin / designer / platform surface (`/admin/...`, `/platform/...`, designer routes) ⇒ `Platform`
3. **Default** — `Consumer` (lowest privilege) when neither an override nor a classifiable `returnTo` is present.

## Entitlement gate (server-side, non-negotiable)

```
minted = requested ?? Consumer
if minted ∉ entitledTiers(user, activeContext): 4xx REJECT   // never silently downgrade
```

- `entitledTiers`: `Consumer` for any authenticated human; `Platform` only with a platform role in the active org context.
- A `:platform` request from a user with no platform role is an **error**, not a consumer login.

## Applies uniformly to every issuance path

`TokenService` (interactive login, verify-2fa, refresh*, switch-org), signup completion, `SocialCallback`, `OidcCallback`, `EnrolSessionService` redeem (always `Consumer`).
*Refresh does not re-derive — it preserves the refresh token's `tier` claim (no `returnTo` at refresh time).

## Spec B usage

Spec B's PWA bounces an unauthenticated user to the server auth pages with `returnTo=/wallet/...`; by rule (2) the returned token is `Consumer`, lands in the PWA, and is accepted by the wallet. No new client contract is required of Spec B beyond setting `returnTo`.

## Validation / errors

- `tier` override: validated against the enum (FluentValidation / DataAnnotations on the request boundary per Constitution II).
- Over-request: HTTP 403 (or equivalent) with a non-leaky message; recorded on `sorcha_tier_request_rejected_total{requested,reason}`.
- `returnTo`: validated by the existing allowlist (open-redirect fail-closed) before tier derivation.
