# Contract: Shared verify components (UI contracts)

The three components are the externally-consumed surface of this wave. Each contract below is the
parameter/event/render contract a host (B3) or a bUnit test binds to. Namespaces stay subject-level
(`Sorcha.UI.Core` root namespace per the library convention); folder is `Components/Verify/`.

---

## C1 — `QuestionSelectionPanel` (FR-001)

**Injects**: `IVerificationPresetCatalogue`.

**Parameters**: none required (catalogue is the source of presets).

**Events**:
- `[Parameter] EventCallback<VerificationPreset> OnQuestionSelected` — raised with the chosen preset
  (a built/looked-up `VerificationPreset`) when the operator picks a preset **or** confirms a valid
  custom question (built via `catalogue.BuildCustom(...)`).

**Render contract**:
- Renders every `catalogue.GetAll()` preset as a selectable option (label = `preset.Label`).
- Renders a custom-question affordance alongside the presets.
- With no configured presets, falls back to the builtin set (catalogue behaviour) and still renders.

**Acceptance mapping**: US1 scenarios 1–3.

---

## C2 — `VerificationSessionQr` (FR-002, FR-004, FR-007)

**Injects**: `IVerificationTransport`.

**Parameters**:
- `[Parameter, EditorRequired] VerificationPreset Question` — the question to start a session for.
- `[Parameter] CancellationToken CancellationToken = default` — host-supplied cancellation, linked into
  the component's own CTS.

**Events**:
- `[Parameter] EventCallback<string> OnCompleted` — raised once with the presentation token
  (`VerificationSessionPoll.VpToken`) when polling reports `IsComplete == true`.

**Lifecycle / render contract**:
1. **Not-configured** (default stub): if `StartSessionAsync` returns an empty `SessionId`/`QrDeepLink`
   (the `NotConfiguredVerificationTransport` sentinel), render an explicit "verification is not yet wired
   here" state; **do not** poll, throw, or raise `OnCompleted`.
2. **Active**: with a real transport, render the QR (QRCoder) + deep-link for `QrDeepLink`.
3. **Pending**: while `PollSessionAsync` returns `IsComplete == false`, show a waiting state and continue
   polling at a fixed interval.
4. **Complete**: on `IsComplete == true`, stop polling and raise `OnCompleted(VpToken)`.
5. **Error/retry**: a terminal transport error (real transport throws/faults) stops polling and renders an
   error/retry state (Edge Case "Transport unreachable").
6. **Disposal** (`IAsyncDisposable`): cancel the linked CTS, await the in-flight poll task (swallowing
   `OperationCanceledException`), dispose the CTS; no post-disposal `StateHasChanged`, no unobserved
   exception.

**Acceptance mapping**: US2 scenarios 1–5; Edge Cases "No transport wired", "Transport unreachable",
"disposed mid-poll".

---

## C3 — `VerdictTrailPanel` (FR-003)

**Injects**: `IRegisterAnchorClient` (for the on-demand layer-4 check).

**Parameters**:
- `[Parameter, EditorRequired] VerdictViewModel Verdict` — built client-side (see di-extension /
  data-model) from a `VerificationPreset` + `VerificationOutcome`.

**Events** (optional):
- `[Parameter] EventCallback OnAnchorChecked` — raised after the layer-4 check completes (for hosts that
  want to react).

**Render contract**:
1. Headline (`Verdict.Headline`, `Verdict.OverallPass`), issuer identity, portrait/age chips when present.
2. Disclosed vs withheld claim split (`Verdict.Disclosed` / `Verdict.Withheld`).
3. The three offline layers (LivePresentation, IssuerSignature, Revocation) render from `Verdict.Layers`
   with **no** network call on first display.
4. A layer-4 (RegisterAnchor) affordance: when triggered, call
   `IRegisterAnchorClient.CheckAsync(Verdict.RegisterAnchorId, Verdict.CredentialId, ct)`, then
   append/replace the RegisterAnchor `ValidationLayerResult` in `Verdict.Layers` and re-render with the
   anchor status (anchored / proof-invalid / unverified).
5. No register-anchor reference (`RegisterAnchorId` null) → show layer-4 as "unverified / not applicable";
   never fail the overall verdict on an unverified/absent anchor.

**Acceptance mapping**: US3 scenarios 1–3; Edge Cases "no anchor reference", "register unreachable".

---

## Cross-cutting

- **XML docs** (FR-015): every public member of all three components, the stub transport, the relocated
  types, and the DI extension carries `/// <summary>`. Zero new build warnings.
- **No host rewiring** (FR-012): these components are added but **not** wired into `/wallet/verify` or the
  desk verifier pages in this wave.
- **UI conventions**: MudBlazor; inline feedback (no `ISnackbar`) per CLAUDE.md Pattern #12.
