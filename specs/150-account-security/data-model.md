# Phase 1 Data Model: Unified Account Security Surface

Scope: the persisted and computed entities this feature adds or extends. The relational change is **squashed into the Tenant Service's existing initial migration** (pre-release policy — NO incremental migrations) + Redis (ephemeral OTP, no migration) + static computed policy. Everything else reuses existing Feature-116 entities (passkey credentials, social links, password hash, TOTP secret, auth-challenge tokens).

---

## 1. Persisted (Tenant Postgres — squashed into the existing initial migration)

### 1.1 `PlatformUser` (extended)

| New field | Type | Notes |
|-----------|------|-------|
| `PhoneNumber` | `string?` (E.164) | Null until the user enables SMS. Plaintext (sender needs cleartext; see R-011). |
| `PhoneVerifiedAt` | `DateTimeOffset?` | Set when the number is verified; cleared if the number changes. SMS 2FA cannot activate while null. |

Invariants: a non-null `PhoneVerifiedAt` implies a non-null `PhoneNumber`. Changing `PhoneNumber` resets `PhoneVerifiedAt` to null and (if SMS was enabled) disables SMS 2FA + fires always-notify.

### 1.2 `PlatformUserTwoFactor` (new, 1:1 with `PlatformUser`)

Explicit per-channel enablement state, so "which 2FA channels are on" is first-class rather than inferred.

| Field | Type | Notes |
|-------|------|-------|
| `PlatformUserId` | `Guid` (PK, FK) | 1:1, cascade delete from `PlatformUser`. |
| `TotpEnabled` | `bool` | Mirrors existing TOTP state (single source after migration). |
| `EmailOtpEnabled` | `bool` | Requires `PlatformUser.EmailVerified`. |
| `SmsOtpEnabled` | `bool` | Requires `PhoneVerifiedAt != null` **and** an operator-configured SMS provider. |
| `UpdatedAt` | `DateTimeOffset` | Audit. |

Invariants: `SmsOtpEnabled` can only be `true` when `PhoneVerifiedAt != null`. Disabling a channel never violates the last-method floor because 2FA channels are *second* factors, not sign-in methods — but disabling is still always-notified.

> The existing TOTP secret/backup-code storage is unchanged; `TotpEnabled` here is the canonical enablement flag the unified surface reads.

---

## 2. Ephemeral (Redis — no migration)

### 2.1 `OtpChallenge` (server-sent code state)

Key: `sorcha:otp:{purpose}:{platformUserId}` (one live challenge per purpose per user). Single-use via GETDEL; TTL = expiry.

| Field | Type | Notes |
|-------|------|-------|
| `CodeHash` | `string` | Hash of the 6-digit code (never the plaintext). |
| `Channel` | enum `{ Email, Sms }` | Delivery channel. |
| `Purpose` | enum `{ Login2fa, StepUp, PhoneVerify, EmailEnable }` | Scopes the challenge; a code minted for one purpose can't satisfy another. |
| `ExpiresAt` | `DateTimeOffset` | 10-min default (R-003). |
| `Attempts` | `int` | Incremented per verify; invalidate at 5. |
| `Destination` | `string` | The email/number the code was sent to (bind-check on verify). |

Registered via F113 `IStorageRegistrationLog` as cache-style (not fail-fast audited).

---

## 3. Computed (static — no storage)

### 3.1 `AuthAssuranceTier`

`enum { Basic = 1, Strong = 2, Strongest = 3 }` (ordinal enables `>=` comparison).

### 3.2 `AssurancePolicy` (method → tier + floor rule)

Pure functions in the Tenant Service:

- `TierOf(AuthMethodKind) → AuthAssuranceTier`:
  - `Passkey → Strongest`
  - `Totp → Strong`
  - `EmailOtp | SmsOtp | BackupCode → Basic`
  - `Password`, `Social` → not second factors; participate in the floor rule as **targets** with these target tiers: Password target = Strong-equivalent (changing/removing the password requires a Strong+ proof), Social removal target = Basic (a linked social is a delegated sign-in; removal is a Basic-target op) — see the policy table.
- `RequiredProofTierFor(operation, targetMethod) → AuthAssuranceTier`: the minimum proof tier to authorise the op (full table in `contracts/floor-rule-policy.md`).
- `CanRemove(user, method) → bool`: `true` iff removal would not breach the last-method floor **and** the user currently holds at least one proof method of tier `>= RequiredProofTierFor(Remove, method)`.

These feed the extended `/api/me/auth-methods` aggregate as per-row `CanRemove` + `RequiredProofTier`.

---

## 4. Extended DTOs (wire)

### 4.1 `AuthMethodRow` (in the existing aggregate response)

| New field | Type | Notes |
|-----------|------|-------|
| `AssuranceTier` | `"Basic" \| "Strong" \| "Strongest" \| null` | null for non-tiered sign-in rows where a badge isn't shown. |
| `RequiredProofTier` | `"Basic" \| "Strong" \| "Strongest"` | Min proof tier to remove/downgrade this row. |
| `CanRemove` | `bool` | Already present; now assurance-aware. |
| `Role` | `"SignIn" \| "SecondFactor" \| "Recovery"` | Drives the job-based grouping. |

### 4.2 2FA channel DTOs

- `EnableChannelResult { Status: "code-sent" | "already-enabled" | "not-available", ChallengeId }`
- `VerifyChannelRequest { Code }` → `VerifyChannelResult { Status: "enabled" | "invalid-code" | "expired" | "too-many-attempts" }`
- `SetPhoneRequest { PhoneNumber (E.164) }` → returns `code-sent`; `VerifyPhoneRequest { Code }` → sets `PhoneVerifiedAt`.

### 4.3 Step-up (`ChallengeMethod` extended)

`ChallengeMethod` gains `EmailOtp`, `SmsOtp`. `initiate` for these triggers a `ServerSentOtpService` send; `verify` checks the code. Passkey + ReOAuth `initiate`/`verify` are completed (R-009).

---

## 5. Notifications (reuses F118 + F112)

- **Inbox**: `TenantSecurityInboxWriter` entry per change (thin-signal: opaque id + change kind + timestamp).
- **Email**: `SecurityChangeDispatch` → `security-change` template. Both fired by `SecurityChangeNotifier`, try/log/swallow (FR-011).

---

## 6. State transitions

**Email 2FA**: `Disabled → (enable: send code) → PendingConfirm → (verify) → Enabled`. `Enabled → (disable + notify) → Disabled`.

**SMS 2FA**: `Disabled → (set phone: send code) → PhonePendingVerify → (verify) → PhoneVerified → (enable: send code) → PendingConfirm → (verify) → Enabled`. Provider de-configured → `Enabled → Suppressed` (option hidden; user guided to another factor; never locked out). Phone changed → `PhoneVerifiedAt` cleared → SMS `Enabled → Disabled` + notify.

**Method removal (any)**: `Requested → (server: CanRemove? floor + last-method) → StepUpRequired(minTier) → (proof tier >= minTier) → Removed + notify` | `Blocked(reason)`.
