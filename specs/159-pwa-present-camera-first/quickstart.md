# Quickstart & Validation: PWA Present — Camera-First Intake

Validation guide proving the three intake layouts and their fallbacks work end-to-end. References
[data-model.md](./data-model.md) (state) and [contracts/device-profile.md](./contracts/device-profile.md)
(seams) rather than repeating them.

## Prerequisites

- .NET 10 SDK, Docker Desktop (for E2E against the Dockerised PWA).
- Build: `dotnet restore && dotnet build`.
- Relevant projects:
  - App: `src/Apps/Sorcha.Wallet.Pwa` (page: `Pages/Present.razor`; served at `/wallet/present`).
  - Unit/bUnit tests: `tests/Sorcha.Wallet.Pwa.Tests`.
  - E2E: `tests/Sorcha.UI.E2E.Tests`.

## Run the unit + component tests

```bash
# Device classification + IntakeMode derivation (total mapping)
dotnet test tests/Sorcha.Wallet.Pwa.Tests \
  --filter "FullyQualifiedName~DeviceProfileProbeTests"

# Three intake layouts + camera-fallback branches (bUnit, mocked probe + JS interop)
dotnet test tests/Sorcha.Wallet.Pwa.Tests \
  --filter "FullyQualifiedName~PresentIntakeLayoutTests"
```

**Expected**: all green. The bUnit suite injects a fake `IDeviceProfileProbe` to force each
`IntakeMode` and asserts the rendered intake surface; JS interop (`SorchaQrScanner.*`) is mocked.

## Run the E2E layout-variant tests

```bash
dotnet test tests/Sorcha.UI.E2E.Tests \
  --filter "FullyQualifiedName~PresentIntakeLayoutTests"
```

**Expected**: per-variant Playwright checks pass (handheld emulation = camera-first, desktop =
paste-with-scan, no-camera = paste-only).

## Manual validation scenarios

Map directly to the spec's user stories, acceptance scenarios, and edge cases.

### US1 — Camera-first on handheld (P1)
1. Open `/wallet/present` on a handheld (or browser device-emulation with touch + coarse pointer)
   with a permitted camera.
2. **Expect**: live viewfinder active on arrival, **zero taps** (SC-001), plus a visible
   "Paste a link instead" control (FR-002/FR-003).
3. Point at a valid verifier QR → **expect** advance to credential matching (pick/consent/no-match),
   identical to a pasted link (FR-006/SC-005).
4. Tap "Paste a link instead" → **expect** camera stops (FR-008) and the paste field + Continue show.

### US2 — Paste-with-scan on desktop (P2)
1. Open `/wallet/present` on a desktop-class device with a camera.
2. **Expect**: paste field is the default; a "Scan with camera" control is offered; camera is
   **not** active (FR-004).
3. Activate "Scan with camera" → viewfinder starts; a successful scan advances to matching.
4. Alternatively paste a valid `openid4vp://` link + Continue → advances to matching (FR-011).

### US3 — Paste-only on no-camera (P2)
1. Open `/wallet/present` on a device with no usable camera (no camera API, or no `videoinput`).
2. **Expect**: only the paste field + Continue; **no scan control anywhere** (FR-005).
3. Paste a valid link + Continue → advances to matching (FR-011).

### Edge cases
- **Permission denied while auto-scanning** (handheld): refuse the camera prompt → **expect**
  graceful fallback to the paste field with a plain-language note; never a blank/stuck viewfinder
  (FR-007/SC-003).
- **No camera hardware on handheld**: API present but no camera → **expect** paste-only for the
  session with a plain-language note.
- **Returning after a denied permission**: re-open intake after refusing → **expect** paste remains
  reachable; not re-trapped in a failing viewfinder (R3).
- **Mid-scan navigation away**: leave the page while the viewfinder is live → **expect** the camera
  is stopped/released (FR-008).
- **Unreadable / non-verifier QR**: scan an invalid code → **expect** a recoverable
  "couldn't read that / not a valid request" message; intake stays usable — retry or paste (FR-009).
- **Ambiguous form factor** (large tablet / touchscreen laptop): **expect** exactly one of the three
  layouts renders; never an empty/broken intake (FR-010/SC-002).

## Success criteria coverage

| Criterion | Validated by |
|-----------|--------------|
| SC-001 zero-tap handheld scan | US1 manual + handheld E2E |
| SC-002 every device → exactly one layout | `DeviceProfileProbeTests` (total mapping) + ambiguous edge |
| SC-003 always reach paste, no dead ends | denied / no-camera / no-API edge cases |
| SC-004 desktop paste-default + 1-action scan | US2 manual + desktop E2E |
| SC-005 identical downstream outcomes | convergence assert (`ParseAsync()`), reused `PresentationEngineTests` |
