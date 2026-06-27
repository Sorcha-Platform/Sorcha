# Contract: Persona usage from the onboarding profile step

**No new endpoints.** The onboarding "Complete your profile" step consumes the **existing** persona
contract (Feature 092/103/125). This document pins how the step uses it (FR-001..FR-005, FR-012).

## Endpoints used (existing — Tenant Service `PersonaEndpoints`)

### `GET /api/me/persona`  → `200 PersonaReadModelV1`
- Personal context (omit `?context=`). New users get an **empty** model (never 404).
- Used to **pre-fill** the step (FR-003): seed name fields from existing persona, falling back to the
  display name from `/api/auth/me` when the persona is empty.

### `PUT /api/me/persona`  (body `PersonaAttributesV1`)  → `200 PersonaReadModelV1`
- Upserts on `(PlatformUserId, Personal context)` ⇒ create on first run (FR-002), update-in-place on
  re-entry (FR-004). No duplicate row.
- **Validation (FR-005)**: server enforces `PersonaAttributesV1` invariants — list caps (≤5), exactly one
  default per list, RFC-5322 email, E.164 phone, ISO-3166 nationality/country. Invalid input → `400`
  validation problem; nothing persisted. The step surfaces field-level errors and stays put.
- **`409` (wallet not provisioned)**: returned when the user has no primary wallet yet (the blob is
  encrypted via the Wallet Service keyed on the wallet address). Onboarding sequences wallet creation
  before the profile step; if `409` occurs the step surfaces a clear message and allows retry without
  losing entered values (Edge Cases).
- **`401` / `403`**: standard auth / context guards.

## Client surface used

`IPersonaService` (`Sorcha.UI.Components.User/Services/User/Persona/IPersonaService.cs`):
- `GetAsync(options, ct)` → `PersonaReadModelV1` (session-cached).
- `UpdateAsync(PersonaAttributesV1, ct)` → `PersonaReadModelV1`.

The step calls `GetAsync` on render (pre-fill) and `UpdateAsync` on continue. On success it invalidates the
cache so `/profile` reflects the seeded values immediately (SC-002).

## Onboarding step UI contract

| Concern | Contract |
|---------|----------|
| Fields shown | Minimal subset: name (given/family or full) + optional one email and/or phone. |
| Pre-fill | From `GetAsync`; fallback to `/api/auth/me` display name (FR-003). |
| Skip optional | User may continue with only provided fields saved (FR-001 #3, SC-001). |
| Save | Single `UpdateAsync`; success → continue onboarding (FR-012). |
| Failure | Inline error via `IInlineFeedback` (Pattern #12, no snackbar); values retained; no silent advance. |
| Reachability | Part of the standard first-run journey, after wallet provisioning. |
