# Quickstart: Validating the Unified Account Security Surface

Manual validation per delivery phase, against the Docker stack. Each phase is independently demonstrable (SC-009). Maps acceptance scenarios → success criteria.

## Prerequisites

```bash
docker-compose up -d
# Web app:  http://localhost/app
# PWA:      http://localhost/wallet
# Gateway:  http://localhost:80   | Aspire dashboard: http://localhost:18888
# Web login: admin@sorcha.local / Dev_Pass_2025!   (platform tier)
# A citizen test account for the PWA (consumer tier) per the dev-citizen-test-account note.
```

E2E (per the `sorcha-ui` skill):

```bash
dotnet test tests/Sorcha.UI.E2E.Tests --filter "Category=Security"
```

---

## Phase 1 (US1) — Consolidated Security home + floor rule + finished proofs  [MVP]

1. **Discoverability (SC-001, FR-001)**: sign in to `/app`, open the avatar menu → confirm a **Security** item sits **between** *My Profile* and *My Devices* → click it → lands on `…/app/security` (no Settings detour).
2. **Job-based IA (FR-003/FR-004)**: confirm three groups — *How you sign in*, *Two-factor authentication*, *Recovery* — and an assurance badge (Strongest/Strong/Basic) on each method row.
3. **No regression (SC-002)**: every action that worked under the old Settings *Accounts*/*Security* tabs works here (set/change/remove password, add/rename/remove passkey, link/unlink social, enable/disable TOTP). Old deep-links (`/settings?tab=…`) redirect to `/security`.
4. **Floor rule (SC-003, FR-007)**: as a user holding a passkey + (a Basic factor once Phase 2 lands; pre-Phase-2 use password vs passkey), attempt to remove the passkey → the step-up dialog offers **only** equal-or-stronger proofs; a Basic proof is not offered/accepted.
5. **Last-method floor (SC-008, FR-006)**: reduce to a single sign-in method → its Remove control is disabled with a `last-method` reason.
6. **Finished proofs (FR-012)**: start a sensitive change and choose **Passkey** then **Re-authenticate social** as the proof → both complete (no "coming soon" placeholder).
7. **Always-notify (SC-004, FR-009)**: perform any change → a bell-drawer entry appears **and** a Sorcha-branded email is received within a minute.
8. **PWA shell present (groundwork for US4)**: confirm the same `<SecurityHome/>` renders at `…/wallet/security` (full management lands in Phase 4, but the shared component is mounted).

## Phase 2 (US2) — Email OTP

1. **Enable (FR-013/FR-014)**: on the Security home, *Two-factor → Email code → Enable* → a confirmation code arrives by email → enter it → email shows as an active **Basic** factor with its badge.
2. **Login with email code (SC-005, FR-015)**: sign out → sign in with the first factor → prompted for an emailed code → enter it → reach the app.
3. **Single-use + expiry (SC-005, FR-016)**: reuse a code → rejected; wait past expiry → rejected.
4. **Rate-limit (FR-017)**: request codes rapidly → throttled with a clear message + `Retry-After`.
5. **Floor holds (FR-007)**: with email as the only 2FA, attempt to remove a passkey → blocked (Basic can't authorise Strongest).
6. **Template fidelity**: `dotnet test tests/Sorcha.Tenant.Service.Tests --filter "EmailTemplateSnapshotTests"` passes (regenerate intentionally with `UPDATE_EMAIL_FIXTURES=1`).

## Phase 3 (US3) — SMS OTP (config-gated)

1. **Absent when unconfigured (SC-006, FR-019)**: with no `Sms:*` config, confirm *SMS code* is **not** shown on the Security home and `GET /api/me/auth-methods` returns `smsAvailable: false`.
2. **Configure a provider**: set `Sms:AcsConnectionString` (or provider config) → restart Tenant → *SMS code* now appears.
3. **Verify phone + enable (FR-020/FR-021)**: enter an E.164 number → receive + enter the code → `PhoneVerifiedAt` set → enable SMS → active Basic factor.
4. **Login with SMS code (SC-006)**: sign out → first factor → SMS code prompt → complete.
5. **Per-number cap (FR-022)**: exceed the per-number send cap → throttled.
6. **De-configure mid-life (edge case)**: remove provider config → SMS prompts suppressed, user guided to another factor, never locked out.

## Phase 4 (US4) — PWA parity

1. **Same surface (SC-007, FR-025)**: as a citizen in the PWA, open *Security* → identical three-group home; add/remove a method behaves exactly as web (same floor rule, same notifications).
2. **Passkeys ≠ My Devices (FR-026)**: confirm *Security → Passkeys* and the wallet's *My Devices* are visibly distinct and never cross-referenced as the same thing.
3. **Social link round-trip**: link a social account from inside the PWA → complete the provider flow → return to `…/wallet/security` with the account linked.
4. **Base-relative nav (FR-027)**: every in-app link resolves under `/wallet/` (no origin-root 404s); the web equivalents resolve under `/app/`.

---

## Cross-phase regression

```bash
dotnet test tests/Sorcha.Tenant.Service.Tests --filter "FullyQualifiedName~AssurancePolicy|FullyQualifiedName~ServerSentOtp|FullyQualifiedName~AuthChallenge"
dotnet test tests/Sorcha.UI.Core.Tests --filter "FullyQualifiedName~Security"
dotnet test tests/Sorcha.UI.E2E.Tests --filter "Category=Security"
```

All green + every quickstart step above passing = the feature meets its Success Criteria.
