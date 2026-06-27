# Quickstart / Validation Guide: PWA Shared Persona/Profile Editor

This guide proves the feature end-to-end. It maps each Success Criterion to a runnable check.
Implementation details live in `tasks.md` (Phase 2) — this is a validation/run guide only.

## Prerequisites

- .NET 10 SDK, Docker Desktop.
- Platform running locally: `docker-compose up -d` (Gateway on `http://localhost`, UI at
  `http://localhost/app`, Aspire dashboard at `:18888`). PWA served under the wallet host.
- An **enrolled citizen** account with a provisioned wallet (for the happy-path), plus the ability to
  reach a citizen account **without** a provisioned wallet (for the 409 path).

## Build & test

```bash
dotnet build
# Shared-editor component tests
dotnet test --filter "FullyQualifiedName~PersonaEditorTests"
# PWA DI activation guard (the regression this feature fixes)
dotnet test --filter "FullyQualifiedName~PersonaDiActivationTests"
# PWA bundle hygiene (no UI.Core / Diagrams / YamlDotNet leak)
pwsh scripts/check-pwa-bundle.ps1   # (run via your PowerShell host)
```

Expected: all persona tests green; bundle check passes.

## Scenario validation

### SC-001 — End-to-end save from the PWA (the headline fix)
1. Sign in to the **PWA** as an enrolled citizen with a provisioned wallet.
2. Open **My Profile** (`/profile`). Confirm a real editable form renders (not the old
   "Per-context profile editing arrives soon" placeholder).
3. Change a field (e.g. add a phone number, edit family name) and **Save**.
4. Expect a success confirmation. Reload `/profile`.
5. **Pass**: the change persisted (was impossible before this feature).

### SC-002 — Single shared source of truth (PWA ↔ web)
1. After SC-001, open the **web** app My Profile (`/app` → `/profile`) as the same citizen.
2. **Pass**: the exact values entered on the PWA are shown. Then change a value on web, save, reload
   the PWA → the web change appears on the PWA.

### SC-003 — Identical editor on both surfaces
1. Compare the rendered field set, field order, and validation messages on web `/profile` and PWA
   `/profile`.
2. **Pass**: identical — because both render the same `PersonaEditor` component. (Field-by-field; zero
   surface-specific divergence.)

### SC-004 — Specific, inline, recoverable rejections
1. **Validation (400)**: enter a malformed email (or a 6th entry in a list, or two defaults) and save.
   **Pass**: an inline, field-relevant message appears and all other entered data is preserved.
2. **Wallet not provisioned (409)**: as a citizen without a provisioned wallet, open `/profile` and
   save. **Pass**: a *distinct* provisioning-specific message (not a generic error).
3. **Network/server failure**: simulate offline / a 5xx and save. **Pass**: a "save did not complete,
   retry" state with entered data preserved.

### SC-005 — Automated coverage + PWA activation
1. `PersonaEditorTests` cover load / save-success / validation-rejection / provisioning-rejection.
2. `PersonaDiActivationTests` resolves `IPersonaService` and renders `PersonaEditor` under a
   PWA-configured container. **Pass**: both suites green — proving the editor activates on the PWA host
   (guards the "works on web, broken on PWA" regression).

## References

- Component & state flow: [data-model.md](./data-model.md)
- Endpoint + client contract: [contracts/persona-api.md](./contracts/persona-api.md)
- Design decisions (incl. the missing `ILocalStorageService` on PWA): [research.md](./research.md)
