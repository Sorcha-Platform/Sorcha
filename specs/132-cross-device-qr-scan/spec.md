# Specification — Cross-device QR scan (wallet → council page)

**Feature:** 132
**Status:** Stub (post-v1)
**Roadmap:** Deferred — promoted from F125 follow-up bullet ("real QR/NFC scanners + `webcamera-bridge.js`") to its own spec for discoverability.
**Trigger to promote to Draft:** a customer or demo target that requires the citizen-on-couch flow — phone scans the council's screen (or paper QR) and the wallet decodes the OID4VP request URI without the citizen typing it.

## Problem statement

Today the platform **renders** OID4VP `openid4vp://…` request URIs as QR codes (F127 `CredentialGateComponent`, F126 `HybridQrAffordance`, F128 `PairingHandoffSurface`) and renders citizen-pairing QRs on council pages. **There is no wallet-side scanner.** The citizen either:

- Visits the URL directly on the same device as the council page, or
- Pastes the URL into the wallet PWA manually, or
- Uses the F128 6-digit short-code fallback.

For a real cross-device demo — citizen with a phone, council on a screen — the wallet must open the camera, decode the QR, and route into the existing F127 / F126 / F128 handlers. The OID4VP URI is already fully formed; the holder key is already on the device. **Only the input modality is missing.**

## In scope

| | |
|---|---|
| FR-A | A `<QrScanSurface>` component in `Sorcha.UI.Components.User` opens the device camera, decodes QR codes, and exposes `OnScanned(string payload)`. |
| FR-B | An `IQrScannerService` abstraction over `MediaDevices.getUserMedia` + a QR decoder library (decoder choice deferred — `@zxing/library` or `jsQR`; pick during research). |
| FR-C | A `webcamera-bridge.js` JS-interop module that the Razor component invokes for camera lifecycle + per-frame decode. |
| FR-D | Permission-denied / no-camera fallback: surfaces the F128 short-code paste path as the recovery affordance. |
| FR-E | Wired into the wallet PWA's `Present.razor` as a "Scan to present" entry point that accepts `openid4vp://` URIs and hands off to the existing presentation flow. |
| FR-F | Telemetry: `sorcha_qr_scan_total{outcome ∈ {success, permission-denied, no-camera, decode-timeout, user-cancel}}` on a new `Sorcha.UI.QrScan` meter. |

## Out of scope (this feature)

- **NFC.** `NDEFReader` is Android-Chromium-only; iOS is locked behind native MFi. F125's deferred list parks it; promote into its own spec when warranted.
- Scanning of non-OID4VP QR payloads (e.g. blueprint-design QR codes if those ever exist).
- Auto-rotate / multi-codec QR variants (Data Matrix, Aztec). One codec sufficient for v1 OID4VP.

## Architecture sketch

```
Sorcha.UI.Components.User/
├── Components/QrScan/
│   └── QrScanSurface.razor          # MudPaper with camera viewfinder + overlay
└── Services/User/QrScan/
    ├── IQrScannerService.cs         # OpenAsync / StopAsync / DecodedQr event
    └── QrScannerService.cs          # JS-interop over webcamera-bridge.js

Sorcha.Wallet.Pwa/wwwroot/js/
└── webcamera-bridge.js              # getUserMedia + per-frame decode + cleanup
```

Consumers (`Present.razor`, future F127 wallet-side, etc.) inject `IQrScannerService` and render `<QrScanSurface OnScanned="HandleQr" />`.

## Open design questions (resolve at promote-to-Draft)

| # | Question |
|---|---|
| Q1 | QR decoder library: `@zxing/library` (mature, ~150 KB) vs `jsQR` (~50 KB but less accurate)? Run a head-to-head on real council-screen photos before committing. |
| Q2 | Camera permission denied: bounce to F128 short-code paste, or in-line manual paste affordance directly on `QrScanSurface`? |
| Q3 | iOS Safari quirk: `getUserMedia` requires an explicit user gesture *and* the page must be in a PWA context for autoplay video to work. Does the existing F114 `EnrolGateComponent` already handle the gesture? If yes, reuse pattern. |
| Q4 | Continuous scan vs single-shot? OID4VP requests are one-and-done, so single-shot with explicit "scan again" affordance is simpler and lower battery cost. |

## Effort

Pre-research estimate: **~16h** core (camera bridge, scanner service, surface component, permission UX, F127 wiring, smoke test). NFC sibling spec: **~8h** more if pursued (Android-only payoff).

## Promote criteria

Move this from Stub to Draft when **any** of:

1. A demo target requires cross-device QR scanning (citizen-on-couch Blue Badge demo, public showcase, etc.).
2. v1 ships and Strathcarron Spec 5 (citizen-arc close) prioritises it as part of the verifier-DID resolution work.
3. A second consumer (beyond F127) needs cross-device handoff and short-codes alone don't fit.

## Related

- F125 `tasks.md` "real QR/NFC scanners + `webcamera-bridge.js`" bullet — superseded by this spec.
- F127 `CredentialGateComponent` — current QR display source; this spec adds the scan side.
- F126 `HybridQrAffordance` — same QR-display pattern, different consumer.
- F128 `PairingHandoffSurface` — short-code fallback that this spec preserves as the permission-denied affordance.
- `docs/superpowers/specs/2026-05-16-v1-release-roadmap.md` — section "Post-v1 (explicitly deferred) → Strathcarron citizen-arc follow-ups → F125 polish".
