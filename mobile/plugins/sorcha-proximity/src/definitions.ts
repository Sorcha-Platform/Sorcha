/**
 * BLE transport for ISO 18013-5 proximity credential presentation.
 *
 * ## The one rule this plugin lives by
 *
 * **It moves opaque bytes and knows nothing else.** No CBOR, no mdoc, no credentials, no session state.
 * Every byte of ISO 18013-5 protocol lives in C# (`Sorcha.Mdoc`), written once and shared by the holder and
 * the reader.
 *
 * That is deliberate, and it is the whole reason the C# test suite can prove the protocol without a phone
 * (`LoopbackProximityTransport` substitutes for this plugin). Protocol logic in here would be protocol logic
 * the tests cannot reach — implemented twice, in two languages, with two chances to get the tag-24 rules
 * wrong. If you find yourself wanting to parse something here, that is the signal you are in the wrong file.
 *
 * ## Bytes cross as base64
 *
 * Not `Uint8Array`, not typed arrays. That matches every other JS bridge in this wallet
 * (`SorchaWebCrypto`, `SorchaIndexedDb`) and avoids the marshalling pitfalls of `byte[]` over `IJSRuntime`.
 */

import type { PluginListenerHandle } from '@capacitor/core';

/**
 * Why in-person sharing is unavailable.
 *
 * The distinction between these matters to the citizen: "your phone can't do this" is a dead end, while
 * "turn Bluetooth on" or "grant permission" is one tap from working. Collapsing them into a single
 * "unsupported" would be a bad experience masquerading as a simple one.
 */
export type ProximityUnsupportedReason =
  /** No Bluetooth radio at all. */
  | 'noBluetoothHardware'
  /** Bluetooth is off. Recoverable — the citizen can turn it on. */
  | 'bluetoothOff'
  /** Permission was refused. Recoverable via system settings. */
  | 'permissionDenied'
  /** Permission has not been asked for yet. Recoverable — just ask. */
  | 'permissionNotYetRequested';

/** Why a channel ended. */
export type DisconnectReason =
  /** The peer went away — walked off, locked their screen, moved out of range. */
  | 'peerDisconnected'
  /** Nothing happened for too long. */
  | 'timeout'
  /** The transport itself failed. */
  | 'transportError'
  /** We closed it. */
  | 'localStop';

/** What this device can do — asked BEFORE any UI offers in-person sharing. */
export interface ProbeResult {
  /** Whether an in-person exchange is possible here at all. */
  supported: boolean;
  /** Whether the radio is currently on. */
  bluetoothEnabled: boolean;
  /** Whether the necessary permissions are granted. */
  permissionGranted: boolean;
  /** Why not — present only when `supported` is false. Drives what the UI tells the citizen. */
  reason?: ProximityUnsupportedReason;
}

export interface StartPeripheralOptions {
  /**
   * The service UUID to advertise, taken from the `DeviceEngagement`.
   *
   * Freshly random per session — never a fixed, well-known UUID. A stable one would let a passive observer
   * correlate a citizen's presentations across time and place, which is exactly the tracking property
   * proximity presentation is meant to avoid.
   */
  serviceUuid: string;
}

export interface ConnectCentralOptions {
  /** The service UUID to scan for, read from the holder's scanned `DeviceEngagement`. */
  serviceUuid: string;
}

export interface SendOptions {
  /** One whole message, base64-encoded. Chunking across the MTU is this plugin's problem, not the caller's. */
  dataBase64: string;
}

export interface ReceivedEvent {
  /** One whole message, reassembled. The caller never sees a partial CBOR item. */
  dataBase64: string;
}

export interface DisconnectedEvent {
  reason: DisconnectReason;
}

export interface SorchaProximityPlugin {
  /**
   * Reports what this device can do. **Never rejects** — an unsupported device resolves with
   * `supported: false` and a reason, so the UI can hide the affordance rather than offer something broken.
   */
  probe(): Promise<ProbeResult>;

  /**
   * Holder role: advertise the service and accept **one** connection.
   *
   * Implements the ISO 18013-5 *mdoc peripheral server mode* GATT profile. Pairing-free — 18013-5 forbids
   * bonding, and requesting one would both break interop and leave a durable trace of the encounter on both
   * devices.
   */
  startPeripheral(options: StartPeripheralOptions): Promise<void>;

  /** Reader role: scan for the advertised service and connect. */
  connectCentral(options: ConnectCentralOptions): Promise<void>;

  /** Sends one whole message. Arrives at the peer's `received` whole or not at all. */
  send(options: SendOptions): Promise<void>;

  /** Tears down and stops advertising/scanning. **Idempotent** and safe from any state. */
  stop(): Promise<void>;

  /** One whole message received from the peer. */
  addListener(
    eventName: 'received',
    listenerFunc: (event: ReceivedEvent) => void,
  ): Promise<PluginListenerHandle>;

  /** The channel ended. **Terminal** — the transport is not reusable afterwards. */
  addListener(
    eventName: 'disconnected',
    listenerFunc: (event: DisconnectedEvent) => void,
  ): Promise<PluginListenerHandle>;

  removeAllListeners(): Promise<void>;
}
