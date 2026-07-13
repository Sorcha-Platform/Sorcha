import { WebPlugin } from '@capacitor/core';

import type { ProbeResult, SorchaProximityPlugin } from './definitions';

/**
 * The browser has no BLE we can use.
 *
 * Web Bluetooth is not a route: it does not exist in iOS WKWebView at all, and is absent or unreliable in the
 * Android WebView Capacitor uses. This is the same class of gap that already blocks Android passkeys (no
 * `PublicKeyCredential` in the WebView), and the answer is the same — a native plugin.
 *
 * So this stub exists to make `probe()` answer honestly rather than to make anything work. Every other method
 * rejects loudly: a silent no-op would let a caller believe an exchange was under way when no radio had been
 * touched, which is worse than a clear failure.
 */
export class SorchaProximityWeb extends WebPlugin implements SorchaProximityPlugin {
  async probe(): Promise<ProbeResult> {
    return {
      supported: false,
      bluetoothEnabled: false,
      permissionGranted: false,
      reason: 'noBluetoothHardware',
    };
  }

  async startPeripheral(): Promise<void> {
    throw this.unavailable('In-person sharing needs the Sorcha app — a browser cannot act as a BLE peripheral.');
  }

  async connectCentral(): Promise<void> {
    throw this.unavailable('In-person reading needs the Sorcha app — a browser cannot act as a BLE central.');
  }

  async send(): Promise<void> {
    throw this.unavailable('No proximity transport in the browser.');
  }

  async stop(): Promise<void> {
    // Idempotent and safe from any state, including this one. Nothing to tear down.
  }
}
