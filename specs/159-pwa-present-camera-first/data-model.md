# Phase 1 Data Model: PWA Present — Camera-First Intake

This feature introduces **no persisted entities** and **no API/schema changes**. The "data model"
here is the small set of client-side, per-session value types that drive intake-layout selection on
`Present.razor`. The downstream request/match/consent types (`ParsedPresentationRequest`,
`CachedCredential`, `CredentialMatch` in `Sorcha.UI.Core.Models.Presentation`) are reused unchanged.

---

## Entity: DeviceProfile

The holder's device as classified for the intake-layout decision (spec "Device profile").
Constructed once per page load by `IDeviceProfileProbe`; never persisted.

| Field | Type | Notes |
|-------|------|-------|
| `FormFactor` | `DeviceFormFactor` enum | `Handheld` or `Desktop` (R1 classification). |
| `CameraAvailability` | `CameraAvailability` enum | `Usable` or `Unavailable` (R2). `Unavailable` when the camera API is absent or no `videoinput` device exists. |

- **Derived**: `IntakeMode Mode` — computed from the two fields (see decision table below). This is
  the single value `Present.razor` branches on.
- **Validation / invariants**: Every `(FormFactor, CameraAvailability)` pair MUST map to exactly one
  `IntakeMode` (FR-010 / SC-002). The derivation is total — no undefined combination.

## Enum: DeviceFormFactor

| Value | Meaning |
|-------|---------|
| `Handheld` | Device the holder typically holds and points at a QR code (phone / small tablet). |
| `Desktop` | Laptop/desktop-class device. |

Ambiguous devices (large tablet, touchscreen laptop) resolve deterministically to one of these two
via the R1 heuristic — never an undefined third state.

## Enum: CameraAvailability

| Value | Meaning |
|-------|---------|
| `Usable` | Wallet can drive a working camera capture capability. |
| `Unavailable` | Camera API absent, no camera hardware, or otherwise not drivable for layout purposes. (Runtime permission denial is handled at start time, transitioning the layout to paste — see CameraStartOutcome.) |

## Enum: IntakeMode (the spec "Intake layout")

| Value | Rendered surface |
|-------|------------------|
| `CameraFirst` | Live viewfinder auto-started + "Paste a link instead" control. |
| `PasteWithScan` | Paste field default + "Scan with camera" control (camera off until requested). |
| `PasteOnly` | Paste field only; no scan control anywhere. |

### IntakeMode derivation (total mapping)

| FormFactor | CameraAvailability | → IntakeMode |
|------------|--------------------|--------------|
| `Handheld` | `Usable` | `CameraFirst` |
| `Handheld` | `Unavailable` | `PasteOnly` |
| `Desktop` | `Usable` | `PasteWithScan` |
| `Desktop` | `Unavailable` | `PasteOnly` |

This table is the executable form of FR-001/FR-005/FR-010 and SC-002.

## Value: CameraStartOutcome (runtime, not a layout input)

Represents the result of actually starting the viewfinder (auto on `CameraFirst`, on-demand on
`PasteWithScan`). Not a stored entity — a transient signal that drives the fallback transition.

| Value | Resulting page behaviour |
|-------|--------------------------|
| `Scanned(payload)` | Camera stopped; `payload` set into `_deepLink`; `ParseAsync()` invoked (FR-006). |
| `PermissionDenied` | Camera stopped; switch to paste field + plain-language note; set session "camera refused" flag (FR-007, R3). |
| `Unsupported / NoCamera / Error` | Same fallback as `PermissionDenied` (FR-007). |
| `Cancelled` | Camera stopped; return to the layout's paste-reachable state (FR-008). |
| `Unreadable / NotAVerifierRequest` | Recoverable inline message; intake stays usable — retry scan or switch to paste (FR-009). |

## Page state additions (Present.razor)

The existing `enum Phase { AwaitingDeepLink, NoMatch, PickCredential, Consent, Done }` is unchanged.
The intake-layout state lives *within* the `AwaitingDeepLink` phase:

| State | Type | Purpose |
|-------|------|---------|
| `_intakeMode` | `IntakeMode` | Selected layout for this load (from `DeviceProfile.Mode`). |
| `_showPaste` | `bool` | Whether the paste field is currently shown instead of the viewfinder (e.g. after "Paste a link instead", or after a fallback). |
| `_cameraRefusedThisSession` | `bool` | Set when camera start failed/was refused; suppresses re-trapping in a failing viewfinder on re-entry (R3 edge case). |
| `_intakeMessage` | `string?` | Plain-language recoverable message (denied permission, unreadable QR), if any. |

### State transitions (AwaitingDeepLink intake)

```
load → probe → _intakeMode
  CameraFirst   → auto StartScan
                    ├─ Scanned        → _deepLink set → ParseAsync()
                    ├─ Denied/Error   → _showPaste=true, _cameraRefusedThisSession=true, _intakeMessage set
                    └─ "Paste instead"→ stop camera, _showPaste=true
  PasteWithScan → _showPaste=true (default)
                    └─ "Scan with camera" → StartScan → (Scanned | Denied/Error → message | Cancelled)
  PasteOnly     → _showPaste=true, no scan control

(any) paste + Continue → _deepLink set → ParseAsync()   # unchanged paste path (FR-011)
```

All exits from a live viewfinder call `SorchaQrScanner.stop()` (FR-008). No transition leaves the
holder without a reachable paste field (FR-010).
