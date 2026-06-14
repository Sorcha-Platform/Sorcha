# Quickstart: PWA Dual-Tier / Org-Role Work

**Feature**: 153-dual-tier-org-role | **Date**: 2026-06-14

## What this adds

A citizen who is also an org member can switch into their organisation on the phone, do their
org-role workflow actions, and switch back to personal — reusing the existing context switcher.

## Where the code lives

```
src/Apps/Sorcha.Wallet.Pwa/
├── Services/IAccessTokenStore.cs            (modify — home-token slot: Get/Set/ClearHomeAsync)
├── Services/Context/IUserContext.cs         (modify — snapshot on leave Personal; restore on return)
├── MainLayout.razor                         (modify — refresh inbox/badge on context switch; capacity label already exists)
├── Pages/Actions.razor                      (modify — "acting as <Org>" banner + refresh on OnContextChanged)
└── (reuse) ContextChipSwitcher, IUserOrgMembershipsClient, ApplicationInstance, A's inbox
```

## Build & test

```bash
dotnet build src/Apps/Sorcha.Wallet.Pwa/Sorcha.Wallet.Pwa.csproj
dotnet test  tests/Sorcha.Wallet.Pwa.Tests/Sorcha.Wallet.Pwa.Tests.csproj
```

## Manual verification (Docker / n1) — includes the PRIMARY live-validation

1. Sign in as a person who is both a citizen and a member of an org (e.g. the AssuredIdentity
   verification analyst).
2. Confirm the capacity indicator shows **Personal** and personal wallet/credentials work.
3. Switch to the org via the chip → indicator shows **acting as <Org>**.
4. Open the inbox → the org-role action (e.g. analyst "Action 2") is listed, framed as org work.
5. **Open + submit it → it is accepted** (this is the primary org-context-at-execute validation).
6. Switch back to **Personal** → indicator returns to Personal **and personal wallet/credentials
   still work** (no 403 — home token restored).
7. Switch capacity with the inbox open → inbox + count refresh to the new capacity, no reload.
8. As a person with no org memberships → only Personal is offered.

## Guardrails

- **Never** elevate capacity client-side; rely on the server (`switch-org` 403). Show the failure.
- After returning to Personal the active token MUST be consumer (or signed-out) — never a residual
  platform token.
- Base-relative nav; no `ISnackbar`; no backend change.
