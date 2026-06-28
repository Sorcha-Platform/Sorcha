# Phase 1 Data Model: Onboarding Profile Capture

This feature introduces **no new persisted entities and no schema migration**. It reads/writes existing
models and adds one field to a read-only DTO. Entities below are documented as they are *touched*.

---

## 1. User Persona (existing — Feature 092/103/125)

The "self-asserted profile" captured during onboarding. Personal-context scope only for this feature.

**Write model** — `PersonaAttributesV1`
(`src/Common/Sorcha.Tenant.Models/Persona/PersonaAttributesV1.cs`)

| Field | Type | Notes / Validation |
|-------|------|--------------------|
| `GivenName` | `string?` | Given/first name. |
| `MiddleName` | `string?` | Optional. |
| `FamilyName` | `string?` | Family/last name. |
| `FullName` | `string?` | Fallback when given+family both null. |
| `DateOfBirth` | `DateOnly?` | Optional. |
| `Emails` | `IReadOnlyList<PersonaEmail>` | 0..5; exactly one `IsDefault` if non-empty; RFC-5322 shape. |
| `Phones` | `IReadOnlyList<PersonaPhone>` | 0..5; exactly one default; E.164. |
| `Addresses` | `IReadOnlyList<PersonaAddress>` | 0..5; exactly one default. |
| `Nationalities` | `IReadOnlyList<string>` | 0..5; ISO 3166-1 alpha-2. |

**Read model** — `PersonaReadModelV1`
(`src/Common/Sorcha.Tenant.Models/Persona/PersonaReadModelV1.cs`): each scalar wrapped in
`PersonaAttribute<T>(Value, Source, VerifiedBy, LastUpdated)` carrying provenance. New users get a fully
empty instance (all null / empty lists), **never a 404**.

**Persistence** — `PlatformUserPersona`
(`src/Services/Sorcha.Tenant.Service/Models/PlatformUserPersona.cs`, table `PlatformUserPersonas`):
encrypted blob (`CiphertextBlob` + `Nonce`, XChaCha20-Poly1305), keyed by `(PlatformUserId, ContextOrgId)`.
`ContextOrgId == Guid.Empty` ⇒ Personal context (this feature). `WrappedKeyRef` = primary wallet address.
**Upsert** on the composite key ⇒ re-entry updates in place (FR-004), no duplicate.

**Onboarding scope for FR-001..FR-005**: the step captures a minimal subset — name (`GivenName` /
`FamilyName` or `FullName`) plus optional basic contact (a default email/phone). Pre-fill (FR-003) seeds
`GivenName`/`FamilyName`/`FullName` from the known display name and any existing persona read. Validation
(FR-005) reuses the write-model invariants — invalid input is rejected with field-level feedback and
nothing is persisted.

**State transitions**:
```
(no persona row)  --PUT /api/me/persona-->  (persona row, encrypted)        # FR-002 create
(existing row)    --PUT /api/me/persona-->  (same row, replaced/updated)    # FR-004 update-in-place
(no wallet)       --PUT /api/me/persona-->  409 (wallet not provisioned)    # ordering constraint
```

---

## 2. Current-User Information (modified — DTO field added)

`CurrentUserResponse` (`src/Services/Sorcha.Tenant.Service/Models/Dtos/AuthDtos.cs:235`) — read-only
projection of the authenticated session, returned by `GET /api/auth/me`.

| Field | Type | Status |
|-------|------|--------|
| `UserId` | `string?` | existing |
| `Email` | `string?` | existing |
| `DisplayName` | `string?` | existing |
| `OrganizationId` | `string?` | existing |
| `OrganizationName` | `string?` | existing |
| `Roles` | `string[]` | existing |
| `TokenType` | `string` | existing |
| `Scopes` | `string[]` | existing |
| `AuthMethod` | `string?` | existing |
| **`EmailVerified`** | **`bool`** | **NEW — FR-010/FR-011.** Default `false`. `true` only when the `email_verified` claim is present and true. Non-nullable so "unknown" reads unambiguously as `false` (not verified). |

Populated in `GetCurrentUser` from the `email_verified` claim (Decision 4). Claim is minted from
`PlatformUser.EmailVerified` (`src/Services/Sorcha.Tenant.Service/Models/PlatformUser.cs:37`).

**Client mirror**: any client model deserializing `/api/auth/me` (e.g. the current-user model consumed in
`Sorcha.UI.Core` / `Sorcha.UI.Components.User`) gains the matching `EmailVerified` property so the UI can
read it (US3 enabling data point).

---

## 3. Wallet Creation Request (existing — defaults seeded, no model change)

`CreateWalletRequest` (`src/Common/Sorcha.ServiceClients.Http/Wallet/Models/CreateWalletRequest.cs`):
`WordCount` default stays **12** (FR-009/SC-005). `Name` and `WordCount` are seeded for the onboarding
context via the existing `CreateWallet.razor` query parameters (`?name=`, `?words=24`) — **no change to the
request model or its global defaults**. The user may override both (FR-008); chosen values are bound to the
request and survive back-navigation.

---

## Relationships

```
PlatformUser (1) ──< (0..1 per context) PlatformUserPersona      # persona owned by user, per-context
PlatformUser.EmailVerified ──projected──> email_verified claim ──> CurrentUserResponse.EmailVerified
PlatformUser primary wallet ──keys──> PlatformUserPersona.WrappedKeyRef   # encryption dependency (ordering)
```

No new foreign keys, no new tables, no migration.
