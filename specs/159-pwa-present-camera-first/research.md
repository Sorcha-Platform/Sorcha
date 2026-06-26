# Phase 0 Research: PWA Present — Camera-First Intake

No open `NEEDS CLARIFICATION` items remained after the spec (the device-classification heuristic is
explicitly an implementation choice — see spec Assumptions). The decisions below resolve the
implementation-level unknowns surfaced while grounding the plan in the existing PWA code.

---

## R1. Device form-factor classification (handheld vs desktop)

- **Decision**: Classify in client-side JS (`device-profile.js`) using a combination of signals,
  not a single one: treat the device as **handheld** when it reports a coarse primary pointer
  (`matchMedia('(pointer: coarse)')`) **and** touch capability (`navigator.maxTouchPoints > 0`),
  optionally corroborated by a narrow viewport. Otherwise treat it as **desktop**. The result is
  surfaced to C# as a `FormFactor` enum so the page never branches on raw media queries.
- **Rationale**: `pointer: coarse` + `maxTouchPoints` is the most reliable, widely-supported way to
  distinguish a held-and-pointed device from a laptop/desktop without brittle user-agent sniffing.
  It degrades sensibly for ambiguous devices (large tablets, touchscreen laptops) — the spec only
  requires that every device lands in exactly one of the three layouts (FR-010), not perfect
  taxonomy.
- **Alternatives considered**:
  - *User-agent string parsing* — rejected: fragile, frequently spoofed/frozen, and discouraged by
    browsers.
  - *Viewport width alone* — rejected: a narrow desktop window or a large tablet misclassifies; not
    a reliable proxy for "held and pointed".
  - *MudBlazor `IBreakpointService` alone* — rejected as the primary signal for the same reason
    (viewport ≠ form factor), though breakpoint may corroborate.

## R2. Camera availability ("usable camera") detection

- **Decision**: Reuse the existing `SorchaQrScanner.isSupported()` bridge call as the baseline
  capability probe (it checks `navigator.mediaDevices.getUserMedia`). Treat the camera as
  **unavailable** when the API is absent. Do **not** call `getUserMedia` eagerly just to detect a
  camera — actual permission/hardware failure is handled at start time (R3). Optionally use
  `enumerateDevices()` (labels hidden pre-permission) to detect a total absence of `videoinput`
  devices on handheld, classifying that session as paste-only per the edge case.
- **Rationale**: Avoids triggering a permission prompt during a passive capability check, and
  reuses the already-CSP-compliant bridge. The spec defines "usable camera" as a working capture
  capability the wallet can drive; API-absence and zero `videoinput` devices both mean "no usable
  camera" for layout purposes.
- **Alternatives considered**:
  - *Eagerly call `getUserMedia` on load to confirm a camera* — rejected: prompts for permission
    before the user has chosen to scan (bad on desktop, premature on paste-only), and a refusal
    would wrongly be read as "no camera".

## R3. Camera auto-start and graceful fallback

- **Decision**: On the **camera-first** layout, call the existing `StartScanAsync()` path
  automatically after first render (`OnAfterRenderAsync` first pass), guarded so it runs once. Wrap
  the `SorchaQrScanner.start(...)` call in try/catch; on **any** failure (permission denied, no
  camera found, other `JSException`) switch the page to the paste field and show a plain-language
  note (FR-007). Record a per-session "camera refused/failed" flag so re-entry does not re-trap the
  holder in a failing viewfinder (edge case: returning after a denied permission).
- **Rationale**: Reuses the proven start/stop/dispose lifecycle already in `Present.razor`; the only
  new behaviour is *who triggers start* (auto vs button) and the fallback transition. Keeps paste
  reachable at all times (FR-010).
- **Alternatives considered**:
  - *Retry auto-start on every render* — rejected: causes prompt loops and a stuck viewfinder after
    refusal.

## R4. Camera lifecycle / release on transition

- **Decision**: Reuse the existing `CancelScanAsync()` (`SorchaQrScanner.stop()`) and
  `DisposeAsync()` cleanup. Ensure every transition out of the live viewfinder — choosing paste,
  navigating away, cancelling, or a successful scan — calls stop exactly once. The "Paste a link
  instead" control invokes the same stop path before showing the paste field.
- **Rationale**: FR-008 requires the camera be released on all exits; the lifecycle already exists
  and is idempotent (`stop()` is documented idempotent in the bridge). This is wiring, not new
  machinery.

## R5. Unreadable / non-verifier QR codes

- **Decision**: Keep the existing scan-result handling: a scanned payload still flows through
  `IPresentationEngine.Parse`. A payload that is unreadable (scanner error) or parses to a
  non-verifier request produces a recoverable inline message and leaves the intake usable (retry
  scan or switch to paste), per FR-009. No change to parsing/validation — only ensure the error
  surface keeps both retry and paste reachable.
- **Rationale**: Reuses existing parse/validation; the requirement is about recoverability of the
  intake surface, which is a UI-state concern in the page.

## R6. Single downstream integration point

- **Decision**: All three layouts set the captured request string into the existing `_deepLink`
  field (or pass it directly) and invoke the existing `ParseAsync()`. No layout calls the engine,
  credential cache, or response POST directly.
- **Rationale**: Guarantees SC-005 / FR-006 — every intake path produces identical downstream
  behaviour because they converge before parse. Confirmed entry point:
  `Present.razor → ParseAsync() → Engine.Parse(...) / Credentials.ListAsync() / Engine.Match(...)`.

## R7. Testability seam

- **Decision**: Extract device classification behind `IDeviceProfileProbe` (returns a
  `DeviceProfile`: form factor + camera availability, with a derived `IntakeMode`). The page injects
  the probe, so bUnit tests can supply a fake probe to exercise each of the three layouts and the
  fallback without a real browser/camera. JS-interop calls are mocked in bUnit.
- **Rationale**: Constitution IV (deterministic, isolated tests) — camera and media queries are not
  available in the bUnit/headless context, so the classification decision must be injectable. Models
  the existing `IWebCameraService.IsCameraSupportedAsync()` pattern in `Sorcha.UI.Components.User`.
- **Alternatives considered**:
  - *Inline media-query/JS calls directly in `Present.razor`* — rejected: untestable in bUnit and
    couples layout logic to interop.

---

## Resolved decisions summary

| Topic | Decision |
|-------|----------|
| Form factor | JS probe: `pointer: coarse` + `maxTouchPoints` (+ viewport corroboration) → `FormFactor` enum |
| Camera availability | Reuse `SorchaQrScanner.isSupported()`; no eager `getUserMedia`; optional `enumerateDevices` for zero-camera handheld |
| Auto-start | Once, on first render of camera-first layout; try/catch → paste fallback + session flag |
| Lifecycle | Reuse `stop()`/`DisposeAsync()`; stop on every exit (FR-008) |
| Bad QR | Reuse `Parse`; recoverable inline message, retry or paste (FR-009) |
| Convergence | All layouts → `_deepLink` → `ParseAsync()` (FR-006/SC-005) |
| Testability | `IDeviceProfileProbe` seam, injectable + mockable |
