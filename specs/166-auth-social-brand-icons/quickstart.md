# Quickstart: Validate Social Provider Brand Icons

Runnable validation scenarios proving the feature works end-to-end on all three surfaces. See
[`data-model.md`](./data-model.md) for the provider→mark table and [`contracts/icon-resolution.md`](./contracts/icon-resolution.md)
for the resolver behaviour being validated. No implementation code is duplicated here.

## Prerequisites

- .NET 10 SDK, Docker Desktop (for the running services + Playwright infra).
- At least one (ideally all four) social providers configured with non-empty ClientId/ClientSecret so
  buttons render. For local validation, configure Google + GitHub at minimum (one multi-colour, one
  monochrome — exercises both legibility paths).

## 1. Unit test — web icon resolver (fastest signal)

```bash
dotnet test tests/Sorcha.Tenant.Service.Tests \
  --filter "FullyQualifiedName~SocialProviderBrandIcon"
```

**Expect**: all green. Covers — every supported key (mixed casing) returns non-empty `<svg`-markup with
`aria-hidden="true"`; unknown/null/empty returns the neutral fallback; no input throws.

## 2. Web login & signup — visual + behavioural

Start the stack (or run the Tenant Service) and open the auth pages:

```bash
docker-compose up -d
# Login:  http://localhost/<tenant-auth>/auth/login
# Signup: http://localhost/<tenant-auth>/auth/signup  (Social tab)
```

**Expect**:
- Each configured social button shows the provider's brand icon to the **left** of its label
  (FR-001/FR-002, SC-001).
- Google/Microsoft render in brand colour; GitHub/Apple marks are clearly visible (not vanished).
- Toggle light/dark presentation → GitHub/Apple marks remain legible (track text colour) (FR-008, SC-006).
- An unconfigured provider does **not** appear; no broken-image placeholders anywhere (FR-005, FR-007, SC-002).
- Icon + "Continue with Google" stays the single accessible name (inspect: SVG is `aria-hidden`) (FR-010, SC-005).
- Click a social button → the **same** sign-in flow runs (same redirect/callback) (FR-006, SC-004).
- Login vs signup show the **same** icon for the same provider (US2 scenario 2).

## 3. Citizen wallet PWA — visual + behavioural

Open the PWA sign-in screen (`Sorcha.Wallet.Pwa`, route `/signin`) with providers configured.

**Expect**:
- Each social `MudButton` shows the brand icon as a **leading** `StartIcon` (FR-003).
- Icon size/alignment/spacing matches the passkey button's leading `Fingerprint` icon (US3 scenario 2).
- Narrow mobile width: icon + label does not overflow/clip/wrap awkwardly (edge case).
- Click a social button → existing PWA social flow runs unchanged (FR-006, US3 scenario 3, SC-004).

## 4. End-to-end (existing Playwright Docker infra)

Run the auth-surface E2E checks (web + PWA) — assert each social button has a leading icon element, no
broken images, and that initiating sign-in still routes to the existing provider flow.

```bash
# via the repo's standard Playwright/Docker test entry point (see playwright skill)
```

**Expect**: green — leading icon present on every configured social button across all three surfaces;
no authentication outcome change vs. baseline (SC-004).

## Done when

- [ ] Resolver unit tests pass (step 1).
- [ ] Web login + signup show correct, legible, fallback-safe icons in light & dark (step 2).
- [ ] PWA sign-in shows leading brand icons matching the passkey treatment (step 3).
- [ ] E2E confirms no broken images and unchanged auth flow on all three surfaces (step 4).
