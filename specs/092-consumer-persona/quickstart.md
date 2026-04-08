# Quickstart — Consumer Persona & Nav Tidy

**Feature**: 092 Consumer Persona and Nav Tidy
**Branch**: `092-consumer-persona`

This quickstart walks through the feature end-to-end on a local Docker Compose stack. It is the reference flow used by `Sorcha.UI.E2E.Tests/PersonaAutofillTests.cs` and the manual UAT script.

---

## Prerequisites

- .NET 10 SDK installed
- Docker Desktop running
- The branch `092-consumer-persona` checked out
- `dotnet restore && dotnet build` succeeds at the repo root

---

## 1. Start the stack

```bash
docker-compose up -d
```

Verify:
- API Gateway at http://localhost:80 returns 200 on `/healthz`
- Sorcha UI at http://localhost/app loads the dashboard (after sign-in)
- Aspire dashboard at http://localhost:18888 shows all services green

If you prefer to run with breakpoints, use Aspire instead:

```bash
dotnet run --project src/Apps/Sorcha.AppHost
```

---

## 2. Sign in and provision a wallet (if needed)

1. Navigate to http://localhost/app and sign in with the seeded public-org test user.
2. If your test user does not yet have a wallet, open **Wallets → Create** and create one using the default derivation path. (The persona cannot be saved without a wallet — this is FR-005.)

---

## 3. Open the new "My Profile" page

1. Click the **avatar icon** in the top-right app bar.
2. Observe the menu now contains **My Profile** above **Settings**.
3. Click **My Profile**.
4. You should land on `/app/profile` with two empty sections: *Identity* and *Contact*.

**Expected**: The page header shows your display name and a toggle labelled **"Autofill forms from my profile"** — switched **on** by default.

---

## 4. Fill and save the persona

Enter the following test data:

| Field | Value |
|---|---|
| Given name | Jane |
| Family name | Smith |
| Date of birth | 1987-03-14 |
| Email | jane.smith@example.com (mark as default, label "Personal") |
| Phone | +353 87 123 4567 (mark as default, Mobile) |
| Address | 12 Oak Lane / Dublin / D04 X2Y1 / IE (mark as default, "Home") |
| Nationality | IE (mark as default) |

Click **Save**. You should see a success confirmation and the page should re-render with a "Last updated just now" hint next to each attribute.

**Behind the scenes**:
- The client posts plaintext to `PUT /api/me/persona`.
- Tenant Service validates the invariants, forwards the plaintext to Wallet Service `POST /api/v1/wallets/{address}/persona/encrypt`.
- Wallet Service derives the key under `sorcha:persona-vault` and returns ciphertext + nonce.
- Tenant Service upserts the `platform_user_personas` row.
- An activity log entry is written.

---

## 5. Verify the nav tidy

1. Open the side drawer.
2. Confirm there is **no "Navigation" header text** at the top of the drawer.
3. Confirm the side drawer contains **no "Settings" link** and **no "Notifications" link**.
4. Click the avatar menu again — **Settings** should still be present there.
5. Open **Settings** from the avatar menu.
6. Verify there is a **Notifications tab** containing the notification-preference controls.

---

## 6. Exercise autofill on a consumer form

1. Open the healthcare walkthrough (or any blueprint action form with identity fields) via **New Submission → Healthcare Disclosure**.
2. When the form renders, observe:
   - **Full name**, **Date of birth**, **Home address**, **Phone**, and **Email** fields are pre-filled with the persona values.
   - Each autofilled field has a **cream-tinted background** and a small **"self" tick**.
   - A one-line summary at the top reads **"5 fields filled from your profile"** with **Review** and **Clear all** actions.
3. Tab through the form with a screen reader (or inspect the accessibility tree in DevTools). Each autofilled field announces **"filled from your profile"** as part of its description.
4. Edit the **Phone** field to change the last digit. Observe:
   - The cream tint disappears.
   - The "self" tick is removed.
   - The summary decrements to **"4 fields filled from your profile"**.
5. Click **Review**. A compact popover lists each remaining autofilled field, the attribute it came from, and the current value. Each row has a clear action.
6. Click **Clear all**. All cream-tinted fields are cleared. The field you edited is left alone.

---

## 7. Exercise the off-state of the global toggle

1. Return to **My Profile**.
2. Toggle **Autofill forms from my profile** to **off** and Save.
3. Re-open the same healthcare form.
4. Expected: the form renders with **no autofill applied**. A **"Fill from profile"** button is visible at the top of the form.
5. Click the button. The same cream-tinted autofill appears as if the toggle were on.
6. Re-toggle the preference back to on before moving to the next step.

---

## 8. Exercise the latency target (manual timing)

1. Clear your browser cache and local storage.
2. Navigate fresh to the healthcare form URL.
3. Open browser DevTools → Performance and record a trace.
4. Observe that the form becomes interactive quickly, and persona autofill visibly applies to the matching fields within **500 ms** of first paint (SC-006a).
5. Open the form a second time without reloading — persona is now cached in the session, and the fill is indistinguishable from initial render.

---

## 9. Exercise the empty-state cases

- **New user, no persona saved**: Open any consumer form. Expected — no summary line, no "Fill from profile" button, form renders normally.
- **Form with no matching fields**: Open an admin-only blueprint form with none of the identity fields. Expected — no summary line, no visual change.
- **Persona with only one email**: Open a form asking for email. Expected — that single email is used regardless of whether `IsDefault` was set (service promotes the first entry on write).

---

## 10. Exercise the error paths

### 10.1 Wallet Service temporarily down
1. `docker-compose stop wallet`
2. Refresh `/profile`. Expected — a non-blocking notice "Profile unavailable — forms will be empty". The profile page is visible but values are not displayed.
3. Open a form. Expected — no autofill, but the form is fully functional.
4. `docker-compose start wallet` and refresh — everything returns to normal.

### 10.2 User has no wallet
1. Create a fresh test user with no wallet provisioned.
2. Open `/profile`. Expected — empty state (read succeeds with 200).
3. Try to Save. Expected — inline error "A wallet is required before saving your profile" (409 `wallet_not_provisioned`).

### 10.3 Account deletion
1. As an admin, delete the test user.
2. Inspect the `platform_user_personas` table. Expected — the user's row is gone (cascade delete enforced by FR-007a).

---

## 11. Clean up

```bash
docker-compose down
```

---

## What "done" looks like

All of the following must hold for the feature to be considered complete:

- Every step of this quickstart passes on a fresh clone of the branch.
- `dotnet test` is green (or any failure is a pre-existing known flake, not introduced by this feature).
- `Sorcha.UI.E2E.Tests/PersonaAutofillTests.cs` and `NavTidyTests.cs` pass in the Docker-backed Playwright suite.
- Scalar API docs render the new endpoints with summaries and descriptions (constitution III).
- `CLAUDE.md` contains a "Consumer Persona API" section following the Feature 079 / 085 / 091 pattern.
- The EF schema change is folded into the existing Tenant Service initial setup migration. No new migration file exists under `src/Services/Sorcha.Tenant.Service/Data/Migrations/`.
