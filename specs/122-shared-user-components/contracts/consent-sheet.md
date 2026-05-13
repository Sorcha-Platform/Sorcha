# Contract — Consent Sheet Family

This contract is the only **inverse migration** in the feature — three components originate in `Sorcha.Citizen.Wallet/Components/` and move INTO the shared library. The web app's existing presentation flow gains access to the same consent UX it currently lacks.

## Components

| Component | Source path | Target path |
|-----------|-------------|-------------|
| `ConsentSheet.razor` | `Sorcha.Citizen.Wallet/Components/` | `Sorcha.UI.Components.User/Components/Presentation/` |
| `CredentialPickerDialog.razor` | same source | same target |
| `NoMatchingCredentialDialog.razor` | same source | same target |

## Parameters (preserved from wallet origins)

- `ConsentSheet.PresentationRequest` — the verifier's request describing what's being asked for
- `ConsentSheet.MatchingCredentials` — `IReadOnlyList<CredentialModel>` of credentials that satisfy the request
- `ConsentSheet.OnAccepted` — callback invoked with the chosen credential when the user consents
- `ConsentSheet.OnDeclined` — callback invoked when the user declines
- `CredentialPickerDialog.Candidates` — `IReadOnlyList<CredentialModel>` to choose from
- `CredentialPickerDialog.OnPicked` — callback with the chosen credential
- `NoMatchingCredentialDialog.PresentationRequest` — for context-aware messaging
- `NoMatchingCredentialDialog.OnDismissed` — close callback

## Injected services

The wallet versions today inject only framework services (`IJSRuntime`, `IDialogService`). No library-internal services are needed. The migration preserves this lean profile.

## Host responsibilities

1. Maintain the presentation request lifecycle (Feature 111 — the `PresentationInitiated` → `PresentationOutcome` chain). The consent sheet is a UX layer over an already-initiated request.
2. Provide candidate credentials filtered against the request's `credentialRequirements` — the host's wallet/credential cache feeds `MatchingCredentials`.
3. Translate `OnAccepted` into the appropriate downstream submission (host-side: PWA submits to verifier via OID4VP cross-device; web app submits to the in-page presentation handler).
4. The signing of the resulting `vp_token` is the host's concern — see Feature 114 / 119 specs for PWA flow, existing web flow for the desk-bound path.

## Web-shell adoption requirement

The web app currently lacks a unified consent sheet — its presentation flow is partly implemented in `PresentationSubmitDialog` from the credentials family. Adopting `ConsentSheet` in the web shell is **out of scope for Feature 122** (this feature just relocates the components). A follow-up phase wires the web shell to use the shared consent sheet in place of its existing dialog. The relocation in this feature simply makes that adoption possible without further refactoring.

## Out of contract

- The OID4VP wire protocol details (request resolution, `direct_post` ingest). Owned by Feature 114 (PWA) and the verifier service.
- `vp_token` / KB-JWT construction. Owned by Wallet Service and Feature 114 server-side code.
- Cross-device transport (QR scanning, deep linking). Wallet-specific; remains in the wallet's pages, not in this component.

## Verification

1. **Given** a presentation request with a single matching credential, **When** `ConsentSheet` renders, **Then** the matching credential appears as selectable and the user can accept — verified by bUnit interaction test (new test in `Sorcha.UI.Components.User.Tests`).
2. **Given** a presentation request with no matching credential, **When** the host invokes the no-match flow, **Then** `NoMatchingCredentialDialog` renders with the request context — verified by bUnit test.
3. **Given** the existing `Sorcha.Citizen.Wallet.Tests` suite, **When** wallet-side tests for the three migrated components run after the inverse migration, **Then** all tests pass with `@using` updates only.
