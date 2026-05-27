# Quickstart: Verifying the Wallet Home "Bolder" Reskin

## Prerequisites

```bash
docker-compose up -d        # full stack
# Wallet PWA via gateway:   http://localhost/wallet
# Main web app:             http://localhost/app
# Aspire dashboard:         http://localhost:18888
```

A usable citizen account is required to reach the authenticated home (Participant + linked wallet for the `wallet_address` JWT claim). See the dev-citizen-account setup in project memory; or sign in with an existing citizen and use **Load demo credential** on Home to reach the populated state without the full issuance pipeline.

## Manual verification (maps to spec Success Criteria)

### Empty home (SC-001, US1)
1. Sign in as a citizen with **no** credentials → land on `/wallet`.
2. Confirm: gradient hero with "WELCOME" eyebrow + "Your wallet is empty" headline; the white-on-gradient header row (org pill, bell, scan); the **three-card ghost stack** with an "Add a credential" top card; **Present** de-emphasised/disabled and **Verify** active.
3. Tap the top ghost card → device-enrolment flow opens. Tap Verify → verify flow opens.

### Populated home (SC-002, US2)
1. On the empty home, click **Load demo credential** (or sign in as a citizen who holds credentials).
2. Confirm: hero flips to "ACTIVE WALLET" + "{n} credentials"; the **existing** credential cards render beneath the hero; **Present** is enabled and opens the present flow.
3. Confirm preserved bands still appear when their data exists: needs-attention, recent activity, other-context peek; the F124 waiting card (empty + pending notice) and first-credential welcome overlay still behave.

### Floating tab bar (SC-003, US3)
1. Confirm a floating pill bar (Home/Devices/Activity/Settings) floats above content on Home and on each destination.
2. Tap each tab → correct screen; active tab shows the gradient pill + label, others icon-only.
3. Scroll a long page → content is never hidden behind the bar.

### Dark mode (SC-005, US4)
1. Settings → set theme to **Dark**. Return to Home.
2. Confirm: near-black page (`#0a0b14`), dark surfaces, light text, dark-variant hero gradient (ends `#1a0d2e`). Set **Light** → light palette. Set **System** → follows OS.

### Responsive + a11y (SC-004, US5)
1. DevTools device toolbar — phone width (~375px) and tablet width (~768px): no horizontal scroll, all regions visible at default density.
2. OS/browser **reduce motion** on → ghost cards render static, button press has no scale animation.
3. Keyboard-tab through the home → each action button, ghost card, and nav tab is focusable with a descriptive accessible name.

## Automated verification

```bash
# E2E (Docker stack must be up)
dotnet test tests/Sorcha.UI.E2E.Tests --filter "Category=CitizenWallet"

# Component unit tests (if added)
dotnet test tests/Sorcha.UI.Core.Tests --filter "FullyQualifiedName~Wallet"

# CI gates that must stay green
pwsh scripts/check-no-snackbar.ps1     # no ISnackbar reintroduced
pwsh scripts/check-pwa-bundle.ps1      # bundle hygiene (no forbidden assemblies)
```

E2E covers (SC-006): empty/populated home renders with zero console errors + zero failed network calls (happy path); each tab navigates; Present disabled-when-empty / enabled-when-populated; dark + light render; phone + tablet viewports; accessible-name presence on actions and tabs. A render-sanity check confirms the shared components mount in `/app` at phone/tablet (FR-025).

## Web-host sanity (FR-025)
Open `http://localhost/app` at phone/tablet width and confirm no build/runtime error from the shared `Components/Wallet/` components being compiled into the web host (they need not be *placed* on a web page — this verifies the re-export + theme tokens don't break the web build/render).
