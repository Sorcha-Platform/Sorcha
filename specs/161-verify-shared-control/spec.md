# Feature Specification: Shared verify control + transport seam + config presets (PR B2)

**Feature Branch**: `161-verify-shared-control`
**Created**: 2026-06-25
**Status**: Ready for implementation
**Parent design**: `docs/superpowers/specs/2026-06-25-verify-unification-design.md` (stage B2)
**Builds on**: PR B1 (#1044).

## Summary
Today three verify surfaces diverge: the desk `Sorcha.Verifier` app (preset question → OID4VP QR →
4-layer verdict trail), the PWA paste-based `VerifyFlow`, and HAIP's server validation. This wave
extracts the desk verifier's experience into a **shared Blazor control set** in
`Sorcha.UI.Components.User`, behind two seams — a **transport** (`IVerificationTransport`) and a
**config-driven preset catalogue** (`IVerificationPresetCatalogue`) — and moves the rich-verdict
pieces (`VerdictViewModel`, `IRegisterAnchorClient`) so the **verdict is computed client-side** on
both hosts. This wave lands the machinery + tests **without rewiring either host** (that is PR B3).

## User Scenarios & Testing
1. **Verifier picks what to verify** — a verifier sees a catalogue of preset questions (e.g. "Age
   over 18?", "Confirm identity") plus a custom option, and starts a verification. The preset
   catalogue is **editable via configuration without an application rewrite**.
2. **Rich verdict is shared** — the 4-layer verdict trail (live presentation, issuer signature,
   revocation, register anchor) and the register-anchor cross-check render from one shared control,
   so both the desk verifier and (later) the PWA show the identical verdict.
3. **No behaviour change yet** — because no host is rewired in this wave, the live desk verifier and
   PWA behave exactly as before; this wave only introduces the shared building blocks.

### Acceptance scenarios
- **Given** no preset config, **when** the catalogue is read, **then** it returns the builtin preset
  set (bundled fallback).
- **Given** a `VerifierPresets` config section, **when** the catalogue is read, **then** it returns
  the configured presets (override) — proving presets are editable without code change.
- **Given** a verification outcome, **when** the verdict view model is built, **then** it exposes the
  4 layers, disclosed vs withheld claims, and the register-anchor id for the layer-4 cross-check.
- **Given** the solution builds, **when** the existing `Sorcha.Verifier` and UI test suites run,
  **then** they remain green (no regression from the type moves).

## Functional Requirements
- **FR-001**: The preset catalogue MUST be provided behind `IVerificationPresetCatalogue`, with a
  config-bound implementation and a builtin bundled fallback (editable without a rewrite/redeploy of
  app code).
- **FR-002**: `QuestionPreset` and `VerdictViewModel` MUST live in the shared
  `Sorcha.UI.Components.User` library so both hosts can use them.
- **FR-003**: `IRegisterAnchorClient` (+ impl) MUST live in a shared library both hosts reference, so
  the register-anchor (layer 4) cross-check runs client-side on either host.
- **FR-004**: A `IVerificationTransport` seam MUST define start-session / poll / get-verdict so a host
  can wire its own backend (HAIP) in PR B3 without changing the shared components.
- **FR-005**: The shared components (question selection, QR + poll, verdict trail) MUST render from
  `Sorcha.UI.Components.User`.
- **FR-006**: This wave MUST NOT change the live behaviour of the desk verifier or PWA (no host
  rewiring); the existing test suites MUST stay green.

## Key Entities
- **QuestionPreset** — `{ Key, Label, Purpose, RequiredVct, RequiredClaims[], OptionalClaims[], KnownCredentialClaims[] }` (moved to shared).
- **VerdictViewModel** — overall pass + headline + issuer + disclosed/withheld claims + 4 `ValidationLayerResult`s + register-anchor id (moved to shared).
- **VerifierPresetsOptions** — config section binding the editable preset catalogue.

## Success Criteria
- **SC-001**: Presets can be changed by editing configuration only (no code change) — proven by a test.
- **SC-002**: The verdict view model and register-anchor client compile + run in a WASM-referencing
  shared library (client-side capable).
- **SC-003**: Solution build + `Sorcha.Verifier` + UI test suites green (no regression).

## Assumptions
- `Sorcha.Verifier.Engine` (validator + outcome/layer models) is WASM-safe and already referenced by
  both hosts (confirmed in the design analysis).
- The live HAIP-backed transport implementation + verifier-tier auth land in PR B3; B2 ships the
  interface (and a test/desk adapter) only.

## Out of scope
- Rewiring the PWA `/wallet/verify` and desk `Sorcha.Verifier` pages to the shared control; retiring
  the old paste `VerifyFlow` / `PresentationRequestBuilder` / `InMemoryVerifierSessionStore` (→ B3).
- Any change to HAIP's server-side validation.
