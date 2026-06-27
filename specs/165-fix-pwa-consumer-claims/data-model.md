# Phase 1 Data Model: Fix PWA Consumer-Token Claims

**Feature**: 165 | **Date**: 2026-06-26

This feature changes **no persisted schema**. The "data" here is the **claim set of the consumer-tier access token** and the **identity-resolution precedence** the backend applies to it. Both are derived from the spec's Key Entities and grounded in the code read during Phase 0.

---

## Entity 1: Consumer-tier access token (claim set)

The credential a citizen presents from the PWA. Minted by `TokenService.GenerateUserTokenAsync` at `Tier.Consumer`.

| Claim | Wire name | Source (mint) | Present on consumer token? | Notes |
|-------|-----------|---------------|----------------------------|-------|
| Subject | `sub` | `UserIdentity.Id` (`TokenService.cs:105`) | ✅ Always | **Org-scoped** user id, **not** the platform user id. Do not use as the device key. |
| Email | `email` | `UserIdentity.Email` | ✅ Always | |
| JWT id | `jti` | new GUID | ✅ Always | Revocation tracking. |
| Display name | `name` | `UserIdentity.DisplayName` | ✅ Always | |
| Token type | `token_type` | `"user"` | ✅ Always | `TokenClaimConstants.TokenTypeUser`. |
| **Platform identity** | **`platform_user_id`** | **`platformUserId` arg (`TokenService.cs:110`)** | **✅ Always (post-fix build)** | **The stable cross-org identity. The correct device-lookup key.** Absent only on legacy pre-fix tokens. |
| Org id | `org_id` | `Organization.Id` | ✅ Always | Citizen's home/public org (F136 refinement — kept for the citizen's own org-scoped ops). |
| Org name | `org_name` | `Organization.Name` | ✅ Always | |
| Audience | `aud` | `SorchaAudiences.For(Consumer)` = `{installation}:consumer` | ✅ Always | The **tier boundary**. Refused at platform surfaces. |
| Roles | `role` | — | ❌ Never (consumer) | Platform-only (`TokenService.cs:123-131`). Their absence is part of the tier boundary. |
| Wallet binding | `wallet_address` | — | ❌ Never (consumer) | Platform-only privilege marker. Resolved server-side from identity, never embedded (FR-005). |

**Invariants** (assert in tests):
- INV-1: A `Tier.Consumer` token **always** carries `platform_user_id` (FR-001) — for every issuance path (FR-002) and on refresh (FR-003).
- INV-2: A `Tier.Consumer` token **never** carries `role` or `wallet_address` (FR-006).
- INV-3: A `Tier.Consumer` token's `aud` is exactly `{installation}:consumer` (tier boundary; FR-006, SC-005).
- INV-4: On refresh, the re-emitted consumer token carries the **same** `platform_user_id` as the original (FR-003, SC-004).

---

## Entity 2: Platform identity (`platform_user_id`)

The citizen's stable, cross-organisation identifier; the key the wallet backend uses to recognise the citizen and locate their wallet/devices.

- **Canonical source**: `UserIdentity.PlatformUserId` (a.k.a. `PlatformUser.Id`) in the Tenant identity registry.
- **Relationship to `sub`**: distinct. `sub` = `UserIdentity.Id` (org-scoped). A given platform user may have multiple org-scoped `UserIdentity` rows; `platform_user_id` is the one stable handle across them.
- **Recovery (legacy tokens)**: when the claim is absent, the real value is recoverable by looking up the `UserIdentity` row for `sub` and reading its `PlatformUserId` (the mechanism already used by `PlatformUserDeviceEndpoints.ResolvePlatformUserIdAsync`).

---

## Entity 3: Wallet binding

Association between a citizen and their wallet (and that wallet's devices). **Resolved by the backend, never embedded in a consumer token** (FR-005).

- Wallet ownership key: `Wallet.Owner` — post-#878 wallets stamp `Owner = PlatformUser.Id`; legacy wallets carry `Owner = UserIdentity.Id`. Both eras coexist (`ResolveCitizenContextAsync`, `CitizenWalletEndpoints.cs:610-643`).
- Device key: device records are keyed by **platform user id** (`deviceClient.ListAsync(platformUserId, …)`). → reinforces that resolution MUST yield the platform user id, not `sub`.

---

## Entity 4: Citizen identity-resolution precedence (the behavioural change)

The ordered strategy the **Wallet Service** applies to turn a `ClaimsPrincipal` into `(platformUserId, walletAddress, organizationId)`. The fix re-orders/extends the precedence so a missing `platform_user_id` recovers the **correct** identity.

**Resolution precedence for `platformUserId`** (post-fix):

1. Read `platform_user_id` claim → if a valid GUID, use it. **(common path)**
2. Else read `sub` (`ClaimTypes.NameIdentifier`) → look up the `UserIdentity` for that id in the identity registry → use its `PlatformUserId`. **(legacy/degraded path — NEW; replaces the current direct `sub`-as-platform-id mis-bind)**
3. Else → unresolved → return the citizen-appropriate empty/guidance state (Devices/Add-phone) or unauthorized only where genuinely unidentifiable (not for a merely wallet-less citizen — FR / edge case "citizen with no wallet yet").

**State transitions** (token → surface outcome):

| Token state | Old behaviour | New behaviour (post-fix) |
|-------------|---------------|--------------------------|
| Has valid `platform_user_id` | ✅ Resolves correctly | ✅ Resolves correctly (unchanged) |
| Missing claim, valid `sub` (legacy) | ❌ Mis-binds to org-scoped id → empty/blank | ✅ Recovers true platform id via registry → loads (degrade, not error) — FR-007 |
| No wallet provisioned yet | ⚠️ Blank/unauthorized | ✅ Empty/guidance state, not an error |
| Platform/admin token at citizen surface | n/a | n/a — orthogonal; tier boundary unchanged |

**Validation rules**:
- VR-1: Resolution must prefer the claim and only hit the registry on the fallback path (performance — one indexed read, legacy-only).
- VR-2: The fallback must resolve to a **platform user id**, never an org-scoped `UserIdentity.Id`, when used as a device-lookup key.
- VR-3: Emit a structured-log breadcrumb on the fallback path (observability; mirror `TokenService.cs:313-324`).

---

## Out of scope (explicitly, per spec Assumptions)

- No new claim names or token formats.
- No embedding of wallet binding into the consumer token.
- No PWA page rewrite — pages are verified, not redesigned.
- No persisted-schema migration.
