# Contract: Device Profile Probe (client-side seam)

This feature exposes no REST/gRPC surface. Its only new contract is the **client-side seam** the
Present page depends on to classify the device, plus the **reused** JS scanner bridge contract.
These are the boundaries that tests assert against.

---

## C# seam: `IDeviceProfileProbe`

`Sorcha.Wallet.Pwa.Services.Device.IDeviceProfileProbe`

```csharp
/// <summary>
/// Classifies the holder's device for intake-layout selection on the Present page:
/// form factor (handheld vs desktop) and camera availability. Per-session, never persisted.
/// </summary>
public interface IDeviceProfileProbe
{
    /// <summary>
    /// Probes the current device and returns its <see cref="DeviceProfile"/>, from which the
    /// intake layout (<see cref="IntakeMode"/>) is derived. Must never throw for layout purposes:
    /// any probe failure resolves to a paste-safe profile (camera <c>Unavailable</c>).
    /// </summary>
    ValueTask<DeviceProfile> GetProfileAsync(CancellationToken ct = default);
}
```

**Contract guarantees**
- Returns a fully-populated `DeviceProfile` — both `FormFactor` and `CameraAvailability` set.
- Is **total**: never returns a profile whose `Mode` is undefined (FR-010 / SC-002).
- Is **non-throwing for layout selection**: a JS-interop error degrades to
  `CameraAvailability.Unavailable` (paste-safe), never an exception that blocks render (FR-007).
- Performs **no** camera permission prompt (does not call `getUserMedia`) — capability only (R2).

**Derivation (asserted by tests)** — see `data-model.md` mapping table:

| FormFactor | CameraAvailability | `DeviceProfile.Mode` |
|------------|--------------------|----------------------|
| Handheld | Usable | CameraFirst |
| Handheld | Unavailable | PasteOnly |
| Desktop | Usable | PasteWithScan |
| Desktop | Unavailable | PasteOnly |

---

## JS helper: `SorchaDeviceProfile` (`wwwroot/js/device-profile.js`)

Backs `DeviceProfileProbe`. Pure capability detection — no prompts, no side effects.

```js
// Returns { formFactor: "handheld" | "desktop", cameraApi: bool, hasVideoInput: bool|null }
SorchaDeviceProfile.detect() : object
```

| Field | Source | Notes |
|-------|--------|-------|
| `formFactor` | `matchMedia('(pointer: coarse)')` && `navigator.maxTouchPoints > 0` (+ viewport corroboration) → `"handheld"`, else `"desktop"` | R1 |
| `cameraApi` | `!!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia)` | mirrors `SorchaQrScanner.isSupported()` (R2) |
| `hasVideoInput` | optional `enumerateDevices()` count of `videoinput`; `null` when not determinable without permission | zero ⇒ treat as `Unavailable` on handheld |

**Mapping to C# `CameraAvailability`**: `Usable` iff `cameraApi === true` and `hasVideoInput !== 0`;
otherwise `Unavailable`.

---

## Reused contract: `SorchaQrScanner` (`wwwroot/js/qr-scanner-bridge.js`) — UNCHANGED

The camera lifecycle this feature drives. Listed here because the intake layouts are tested against
its behaviour; **no changes** are made to this bridge.

| Member | Signature | Behaviour |
|--------|-----------|-----------|
| `SorchaQrScanner.isSupported()` | `() → bool` | Synchronous `getUserMedia` capability check. |
| `SorchaQrScanner.start(videoElementId)` | `(string) → Promise<string>` | Starts camera, resolves with first QR payload; rejects on permission denial / no camera / cancel. |
| `SorchaQrScanner.stop()` | `() → void` | Halts active scan; idempotent. |

**Contract obligations on this feature (the caller side)**
- `start` is invoked at most once per viewfinder activation; auto on `CameraFirst`, on user action on
  `PasteWithScan` (FR-002/FR-004).
- Every resolution/rejection/transition is followed by `stop()` exactly once (FR-008).
- A rejection ⇒ paste fallback + plain-language message (FR-007); a resolved payload ⇒ set
  `_deepLink` and call `ParseAsync()` (FR-006).

---

## Downstream convergence point (reused, asserted) — UNCHANGED

All three layouts MUST converge here; tests assert the engine is reached identically regardless of
intake path (SC-005):

```csharp
// Present.razor — existing entry point, not modified by this feature
_request = Engine.Parse(_deepLink.Trim());            // IPresentationEngine
var creds = await Credentials.ListAsync();            // ICredentialCache
_matches = Engine.Match(_request, creds);
```
