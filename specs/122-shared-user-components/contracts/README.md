# Component Contracts — Feature 122

Feature 122 is a code-organisation feature, not an API feature. There are no REST/gRPC endpoints to specify. The equivalent of an API contract here is the **component contract** — the declared surface (parameters, callbacks, injected services) through which a shared Razor component communicates with its host shell.

This directory documents the contract surface for the headline components moving into `Sorcha.UI.Components.User`. Each contract document captures:

1. **Component name and source path** before and after migration.
2. **Parameters** the component exposes to host pages.
3. **Callbacks** the component raises.
4. **Injected services** the component depends on (host-registered or library-registered).
5. **Host responsibilities** — what each consuming shell (web app, PWA) must register / wrap to use the component correctly.

Five contracts cover the components whose host coupling is most material to the migration's success. Components whose contract is entirely "render-only with a single value parameter" do not get their own document — they're listed in `data-model.md` and migrate without surface analysis.

## Index

| Contract | Component family | Notes |
|----------|------------------|-------|
| `form-renderer.md` | `SorchaFormRenderer` + `ControlDispatcher` + `ReviewSummaryRenderer` + Controls/Layouts/Panels | Headline component; widest contract surface |
| `credential-card.md` | `CredentialCard`, `CredentialDetailView`, `CredentialAcceptCard`, `CredentialCardList` | Display + accept actions |
| `consent-sheet.md` | `ConsentSheet`, `CredentialPickerDialog`, `NoMatchingCredentialDialog` | Presentation consent UX (inverse migration from PWA into the library) |
| `persona-panel.md` | `PersonaFillSummary` + `IPersonaService` consumption | Feature 092 surface |
| `participant-picker.md` | `ParticipantList`, `ParticipantSearch`, `ParticipantForm`, related dialogs | Identity selection UX |
| `file-upload.md` | File-reference input control + camera-capture variant | Feature 085 + Feature 107 device-input surface |

## Contract verification

Each contract document includes a **Verification** section listing concrete checks that confirm the contract is honoured after migration. The checks are deliberately small and runnable as part of the regular test suite — they're not a separate validation effort.

Verification follows the same Given/When/Then style as the acceptance scenarios in `spec.md`. A passing contract verification is part of the evidence that the corresponding functional requirement (FR-005, FR-006, FR-007) is satisfied.
