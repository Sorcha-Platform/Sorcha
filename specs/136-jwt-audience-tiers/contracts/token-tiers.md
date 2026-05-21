# Contract: Token Tiers — audiences & per-tier claim sets

The wire contract every issuance path produces and every validator/consumer relies on.

## Audience strings

`aud = "{InstallationName}:{suffix}"`, `InstallationName` default `"sorcha"`.

| Tier | `aud` (default install) | `aud` (white-label `acme`) |
|------|-------------------------|----------------------------|
| Consumer | `sorcha:consumer` | `acme:consumer` |
| Platform | `sorcha:platform` | `acme:platform` |
| Service | `sorcha:service` | `acme:service` |
| EnrolSession | `sorcha:enrol-session` | `acme:enrol-session` |

The full set `{ *:consumer, *:platform, *:service, *:enrol-session }` is the bearer `ValidAudiences` for the installation.

## Per-tier claim sets

Common: `iss`, `aud`, `sub`, `iat`, `exp`, `nbf`, HMAC signature.

### Consumer (`token_type: "user"`, `aud: *:consumer`)
```json
{ "sub": "...", "email": "...", "platform_user_id": "...", "token_type": "user", "aud": "sorcha:consumer" }
```
MUST NOT contain `org_id` or `roles`. (Inert against platform surfaces by construction.)

### Platform (`token_type: "user"`, `aud: *:platform`)
```json
{ "sub": "...", "email": "...", "platform_user_id": "...", "org_id": "...", "roles": ["Administrator"], "wallet_address": "ws1...?", "token_type": "user", "aud": "sorcha:platform" }
```

### Service (`token_type: "service"`, `aud: *:service`)
```json
{ "client_id": "service-blueprint", "service_name": "...", "scope": ["wallets:sign"], "delegated_user_id": "...?", "delegated_org_id": "...?", "token_type": "service", "aud": "sorcha:service" }
```

### EnrolSession (`aud: *:enrol-session`)
```json
{ "sub": "...", "scope": "enrol", "pair_mode": "...", "aud": "sorcha:enrol-session" }
```
Single-use (JTI consumed at redeem). Not a general access token.

## Refresh token
Carries a `tier` claim equal to the access token's tier; refresh re-mints the same tier. `{ "sub": "...", "platform_user_id": "...", "token_use": "refresh", "tier": "consumer" }`.

## Spec B dependency (consumer-token contract)
Downstream PWA-auth (Spec B) relies on: every server-side auth flow can emit the **Consumer** shape above; the consumer token carries `platform_user_id` (required by Wallet Service endpoints + `WalletHub` group assignment); and the Wallet Service + consumer web host validate `{installation}:consumer`.
