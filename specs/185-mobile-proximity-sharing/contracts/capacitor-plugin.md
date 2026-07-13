# Contract — `sorcha-proximity` Capacitor plugin (the native seam)

**Location**: `mobile/plugins/sorcha-proximity/`
**Consumed by**: `CapacitorProximityTransport` (both apps), via `wwwroot/js/proximity-bridge.js`.

**This is the repo's first native plugin.** `mobile/wallet` today has zero plugins (`@capacitor/core|ios|android`
and nothing else) and its only hand-edited native file is `App.entitlements`. Everything here is net-new
Swift and Kotlin.

The surface is deliberately tiny — five methods and two events, moving **base64 strings** — because it is the
one part of the system implemented twice (Swift *and* Kotlin) and therefore the one part where a bug can hide
from the C# test suite.

## TypeScript definition

```ts
export interface SorchaProximityPlugin {
  probe(): Promise<ProbeResult>;
  startPeripheral(options: { serviceUuid: string }): Promise<void>;
  connectCentral(options: { serviceUuid: string }): Promise<void>;
  send(options: { dataBase64: string }): Promise<void>;
  stop(): Promise<void>;

  addListener(event: 'received',     cb: (e: { dataBase64: string }) => void): Promise<PluginListenerHandle>;
  addListener(event: 'disconnected', cb: (e: { reason: DisconnectReason }) => void): Promise<PluginListenerHandle>;
}

export interface ProbeResult {
  supported: boolean;
  bluetoothEnabled: boolean;
  permissionGranted: boolean;
  reason?: 'noBluetoothHardware' | 'bluetoothOff' | 'permissionDenied' | 'permissionNotYetRequested';
}

export type DisconnectReason = 'peerDisconnected' | 'timeout' | 'transportError' | 'localStop';
```

## Marshalling rules (follow the existing conventions — do not invent new ones)

| Rule | Precedent in this repo |
|---|---|
| **Bytes cross as base64 strings**, never as `byte[]` or typed arrays. | `WebCryptoDeviceKeyService` (base64url) and `SorchaIndexedDb` both do this. `byte[]` marshalling across `IJSRuntime` is avoided throughout the wallet. |
| **Push from native → C# via `DotNetObjectReference` + `[JSInvokable]`.** | `BrowserConnectivity`/`SorchaConnectivity` — the only push-from-JS pattern in the wallet, and therefore the one to copy. |
| **The C# wrapper is an interface** with an in-memory sibling. | `IPasskeyInterop`'s own XML doc records the reason: *"generic `InvokeAsync<T>` is brittle to mock — F114 lesson."* |
| **Capability-probe, degrade gracefully.** `globalThis.Capacitor?.Plugins?.SorchaProximity` is `undefined` in a plain browser. | `IPasskeyInterop.IsSupportedAsync()`, `SorchaQrScanner.isSupported()`. Note `capacitor.config.json` uses `server.url` (remote-hosted PWA), so the very same build **does** run in a plain browser — this probe is not theoretical. |

## Native behaviour

**Both platforms**

- Implement the ISO 18013-5 **mdoc peripheral server mode** GATT profile: the State, Client2Server,
  Server2Client and Ident characteristics under the service UUID carried in the `DeviceEngagement`.
- **MTU-aware chunking** with a leading continuation byte (`0x01` = more follows, `0x00` = last).
  Reassembly is the plugin's job — C# sees whole messages only.
- **Pairing-free.** 18013-5 forbids bonding. Do not request or accept a bond.
- **Never log payload bytes.**

**iOS** — `CBPeripheralManager` (holder) / `CBCentralManager` (reader). Requires
`NSBluetoothAlwaysUsageDescription` in `Info.plist`. The peripheral role is restricted in the **background**;
this is **not a constraint here** because an in-person presentation is always foreground.

**Android** — `BluetoothLeAdvertiser` + `BluetoothGattServer` (holder) / `BluetoothLeScanner` +
`BluetoothGatt` (reader). Manifest: `BLUETOOTH_ADVERTISE`, `BLUETOOTH_CONNECT`, `BLUETOOTH_SCAN` with
`usesPermissionFlags="neverForLocation"`. Runtime permission prompts are mandatory on Android 12+ and
`probe()` must report `permissionNotYetRequested` distinctly from `permissionDenied` so the UI can ask rather
than dead-end (FR-021).

## Build integration

Both `mobile/wallet` and `mobile/verifier` take the plugin as a local dependency and `cap sync`. The plugin
builds on the existing Mac build node; the fastlane lanes (`android_adhoc`, `android_internal`, `ios_adhoc`,
`ios_beta`) need no change beyond the plugin being present in `package.json`. Android lanes build on the CI
runner; **iOS lanes must run via the SSH/user-session path** (the runner daemon has no keychain) — an existing,
documented constraint, unchanged by this feature.

## Test strategy for the native layer

The plugin is the **only** component the C# suite cannot reach, so it gets its own narrow bar:

1. **Byte-echo test** — a debug harness sends a known payload from device A to device B and asserts it arrives
   byte-identical, including payloads larger than one MTU (proving the chunker).
2. Everything else is proven in C# over `LoopbackProximityTransport`. The plugin is not permitted to contain
   protocol logic, so there is nothing else in it to test.
