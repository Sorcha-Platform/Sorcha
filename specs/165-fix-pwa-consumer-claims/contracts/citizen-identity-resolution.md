# Contract: Citizen identity / wallet resolution (backend)

**Feature**: 165 | **Type**: Behavioural contract for existing endpoints (no new routes)

Governs how the **Wallet Service** citizen endpoints turn a `ClaimsPrincipal` into the citizen's platform identity and wallet binding. The fix hardens `ResolveCitizenContext` (`CitizenWalletEndpoints.cs:587`) so legacy/degraded tokens **degrade, not error** (FR-007) by recovering the *correct* identity.

## Affected endpoints (existing — unchanged routes/signatures)

| Method + route | Handler | Resolution today | Required behaviour |
|----------------|---------|------------------|--------------------|
| `GET /api/v1/wallet/devices` | ListDevices (`:266`) | `ResolveCitizenContext` (no fallback) | Recover platform id on legacy token; return citizen's own list (empty list is success) |
| `PUT /api/v1/wallet/devices/{id}/label` | UpdateDeviceLabel (`:300`) | `ResolveCitizenContext` | Same recovery; act on the citizen's own device |
| `DELETE /api/v1/wallet/devices/{id}` | RevokeDevice (`:319`) | `ResolveCitizenContext` | Same recovery |
| `POST /api/v1/wallet/devices/enrol` | EnrolDevice (`:495`) | `ResolveCitizenContextAsync` (wallet-by-owner fallback) | Keep owner fallback; ensure device registration uses the recovered platform id |

## Resolution precedence (normative)

`platformUserId` resolves in order:

1. **`platform_user_id` claim** → parse GUID → use. *(common path; no I/O)*
2. **`sub` (`ClaimTypes.NameIdentifier`) → identity-registry lookup** → use `UserIdentity.PlatformUserId`. *(legacy/degraded path — replaces the current direct `sub`-as-platform-id)*
3. **Unresolved** → for Devices/Add-phone present the citizen empty/guidance state; only return 401 when the principal is genuinely unidentifiable.

### MUST
- M-1: When `platform_user_id` is present, behaviour is byte-for-byte unchanged from today.
- M-2: When absent but `sub` resolves to a known `UserIdentity`, the endpoint MUST use that identity's `PlatformUserId` — **never** the raw `sub` — as the device-lookup key. (FR-007; fixes the silent mis-bind)
- M-3: A wallet-less but identifiable citizen MUST get an empty/guidance response, not 401. (spec edge case)
- M-4: Resolution MUST NOT widen access — it only ever resolves to the caller's own identity; no cross-citizen lookup.

### MUST NOT
- MN-1: MUST NOT accept a platform/admin token as a citizen (audience guard unchanged).
- MN-2: MUST NOT embed or require `wallet_address` in the consumer token (FR-005).

## Reference implementation to mirror

`PlatformUserDeviceEndpoints.ResolvePlatformUserIdAsync` (`src/Services/Sorcha.Tenant.Service/Endpoints/PlatformUserDeviceEndpoints.cs:215-234`) already implements precedence 1→2→registry-recovery. The Wallet Service `ResolveCitizenContext` should adopt the same shape (it will need the identity repository injected, as the Tenant endpoint does).

## Observability
- O-1: Emit a structured-log breadcrumb on the precedence-2 (recovery) branch, mirroring `TokenService.cs:313-324`, so degraded-token traffic on `n1` is measurable until legacy tokens age out.

## Test matrix
| Case | Expect |
|------|--------|
| Token with valid `platform_user_id` | Resolves to that id; device list scoped correctly |
| Legacy token (no claim), valid `sub` mapped to a `UserIdentity` | Recovers correct `PlatformUserId`; list loads (not empty-by-misbind) |
| Token with neither resolvable | Devices → empty/guidance; not a 500 |
| Citizen with no wallet | Empty/guidance state, not 401 |
| Platform token at citizen route | Rejected by audience guard |
