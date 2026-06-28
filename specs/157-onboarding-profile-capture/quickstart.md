# Quickstart & Validation: Onboarding Profile Capture

Runnable validation for Feature 157. Proves the three user stories end-to-end. See
[data-model.md](./data-model.md) and [contracts/](./contracts/) for field-level detail.

## Prerequisites

```bash
# .NET 10 SDK + Docker Desktop
dotnet restore && dotnet build

# Start the platform (Tenant Service + UI behind the gateway)
docker-compose up -d         # gateway :80, UI at /app, Aspire dashboard :18888
# — or — Aspire for breakpoint debugging:
# dotnet run --project src/Apps/Sorcha.AppHost
```

A fresh user account (no persona, no wallet) is required for the onboarding scenarios. Register a new
public-org user via the sign-up flow, or use a test fixture user.

---

## US3 — EmailVerified on `/api/auth/me` (P3, smallest; validate first)

```bash
# Verified user token → emailVerified: true
curl -s http://localhost/api/auth/me \
  -H "Authorization: Bearer $VERIFIED_USER_TOKEN" | jq '.emailVerified'
# expect: true

# Unverified user token → emailVerified: false
curl -s http://localhost/api/auth/me \
  -H "Authorization: Bearer $UNVERIFIED_USER_TOKEN" | jq '.emailVerified'
# expect: false
```

**Expected**: field present and correct for both; existing fields unchanged. (FR-010, FR-011, SC-004.)

Integration test:
```bash
dotnet test --filter "FullyQualifiedName~AuthApiTests"
```

---

## US2 — Wallet defaults during onboarding (P2)

Manual (web UI):
1. As the fresh user, land on the dashboard (`/app`) → it routes first-run users to the wallet wizard.
2. Confirm the wizard URL carries `wallets/create?wizard=true&name=…&words=24`.
3. **Expected**: recovery-phrase length shows **24 words** and the wallet **name** is pre-filled
   (FR-006, FR-007, SC-003).
4. Change the word count to 12 and the name; navigate back a step and return.
   **Expected**: your chosen values are preserved, not reset (FR-008, Edge Cases). Create the wallet —
   it is created with your overrides.

Regression (standalone, FR-009 / SC-005):
1. Navigate directly to `wallets/create` (no query string).
2. **Expected**: word count defaults to **12**, name empty — unchanged from before this feature.

E2E:
```bash
# Playwright wallet-wizard onboarding-defaults spec (Docker test infra)
dotnet test --filter "FullyQualifiedName~CreateWallet"
```

---

## US1 — Complete your profile (P1, core)

Manual (web UI), as the fresh user, after wallet creation:
1. Reach the **"Complete your profile"** step in the first-run journey.
2. **Pre-fill check**: the name field is seeded from your sign-up display name (FR-003).
3. Enter/confirm name and (optionally) one contact value; leave optional fields blank and continue
   (FR-001 #3).
4. **Expected**: onboarding advances; values are saved. Verify:
   ```bash
   curl -s http://localhost/api/me/persona \
     -H "Authorization: Bearer $USER_TOKEN" | jq '{given:.givenName, family:.familyName}'
   ```
   (SC-001, SC-002.)
5. **Re-entry / update-in-place (FR-004)**: re-enter onboarding (or revisit the step), amend a value,
   submit again. Confirm `GET /api/me/persona` reflects the amendment and there is exactly one persona
   (no duplicate).
6. **View on profile surface**: open `/profile` → the onboarding-entered values are present (acceptance
   scenario 4).

Validation & failure paths:
1. **Invalid input (FR-005)**: submit a malformed email/phone → field-level error, step stays, nothing
   persisted (`GET /api/me/persona` unchanged).
2. **Save failure (Edge Cases)**: simulate a transient backend failure (or a `409` if no wallet) → inline
   error via `IInlineFeedback`, entered values retained, onboarding does **not** silently advance.

E2E:
```bash
# Playwright onboarding profile-step spec (save, pre-fill, skip, re-entry update, invalid-input)
dotnet test --filter "FullyQualifiedName~Onboarding"
```

---

## Full suite

```bash
dotnet test
# All new tests green; no regression in wallet creation or auth.
```

## Acceptance ↔ scenario map

| Story | Validates | Requirements / Criteria |
|-------|-----------|-------------------------|
| US1 | Profile capture, pre-fill, skip, update-in-place, validation, failure | FR-001..FR-005, FR-012, SC-001, SC-002 |
| US2 | 24-word + name defaults in onboarding; override; standalone unchanged | FR-006..FR-009, SC-003, SC-005 |
| US3 | EmailVerified reported for verified + unverified | FR-010, FR-011, SC-004 |
