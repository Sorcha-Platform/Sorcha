// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

import Capacitor
import CoreBluetooth
import Foundation

/// The Capacitor bridge for ISO 18013-5 BLE proximity.
///
/// **This class moves opaque bytes and nothing else.** No CBOR, no mdoc, no credentials, no session state —
/// every byte of ISO protocol lives in C# (`Sorcha.Mdoc`), written once and shared by the holder and the
/// reader. Protocol logic here would be logic the C# test suite cannot reach, implemented twice, in two
/// languages, with two chances to get the tag-24 rules wrong.
///
/// Bytes cross as **base64 strings**, matching every other JS bridge in this wallet.
@objc(SorchaProximityPlugin)
public class SorchaProximityPlugin: CAPPlugin, CAPBridgedPlugin {

    public let identifier = "SorchaProximityPlugin"
    public let jsName = "SorchaProximity"

    public let pluginMethods: [CAPPluginMethod] = [
        CAPPluginMethod(name: "probe", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "startPeripheral", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "connectCentral", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "send", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "stop", returnType: CAPPluginReturnPromise)
    ]

    private var peripheral: ProximityPeripheral?
    private var central: ProximityCentral?

    // MARK: - probe

    /// Reports capability. **Never rejects** — an unsupported device resolves with `supported: false` and a
    /// reason, so the UI hides the affordance rather than offering something broken.
    @objc func probe(_ call: CAPPluginCall) {
        let authorization = CBManager.authorization

        switch authorization {
        case .denied, .restricted:
            call.resolve([
                "supported": false,
                "bluetoothEnabled": false,
                "permissionGranted": false,
                "reason": "permissionDenied"
            ])

        case .notDetermined:
            // Not a failure — nobody has asked yet. Reported distinctly so the UI can ask rather than
            // dead-end the citizen with "unsupported".
            call.resolve([
                "supported": true,
                "bluetoothEnabled": true,
                "permissionGranted": false,
                "reason": "permissionNotYetRequested"
            ])

        case .allowedAlways:
            call.resolve([
                "supported": true,
                "bluetoothEnabled": true,
                "permissionGranted": true
            ])

        @unknown default:
            call.resolve([
                "supported": false,
                "bluetoothEnabled": false,
                "permissionGranted": false,
                "reason": "permissionDenied"
            ])
        }
    }

    // MARK: - holder

    @objc func startPeripheral(_ call: CAPPluginCall) {
        guard let uuidString = call.getString("serviceUuid"),
              let uuid = UUID(uuidString: uuidString) else {
            call.reject("serviceUuid is required and must be a UUID.")
            return
        }

        stopAll()

        let peripheral = ProximityPeripheral()
        peripheral.onMessage = { [weak self] data in self?.emitReceived(data) }
        peripheral.onDisconnected = { [weak self] reason in self?.emitDisconnected(reason) }
        peripheral.onError = { [weak self] message in self?.emitError(message) }

        self.peripheral = peripheral
        peripheral.start(serviceUuid: CBUUID(nsuuid: uuid))

        call.resolve()
    }

    // MARK: - reader

    @objc func connectCentral(_ call: CAPPluginCall) {
        guard let uuidString = call.getString("serviceUuid"),
              let uuid = UUID(uuidString: uuidString) else {
            call.reject("serviceUuid is required and must be a UUID.")
            return
        }

        stopAll()

        let central = ProximityCentral()
        central.onMessage = { [weak self] data in self?.emitReceived(data) }
        central.onDisconnected = { [weak self] reason in self?.emitDisconnected(reason) }
        central.onError = { [weak self] message in self?.emitError(message) }

        self.central = central
        central.connect(serviceUuid: CBUUID(nsuuid: uuid))

        call.resolve()
    }

    // MARK: - transport

    @objc func send(_ call: CAPPluginCall) {
        guard let base64 = call.getString("dataBase64"),
              let data = Data(base64Encoded: base64) else {
            call.reject("dataBase64 is required and must be valid base64.")
            return
        }

        if let peripheral {
            peripheral.send(data)
        } else if let central {
            central.send(data)
        } else {
            call.reject("No proximity session is active.")
            return
        }

        call.resolve()
    }

    /// Idempotent and safe from any state — sessions are abandoned from arbitrary points (screen lock, the
    /// citizen walking away), and tearing down must never itself be a failure.
    @objc func stop(_ call: CAPPluginCall) {
        stopAll()
        call.resolve()
    }

    private func stopAll() {
        peripheral?.stop()
        peripheral = nil
        central?.stop()
        central = nil
    }

    // MARK: - events

    private func emitReceived(_ data: Data) {
        notifyListeners("received", data: ["dataBase64": data.base64EncodedString()])
    }

    private func emitDisconnected(_ reason: String) {
        notifyListeners("disconnected", data: ["reason": reason])
    }

    private func emitError(_ message: String) {
        // Surfaced as a disconnect, because that is what it is from the protocol's point of view: the
        // channel is not usable. The message goes to the log rather than into the event, since the C# layer
        // maps reasons to citizen-facing copy and must not be handed raw CoreBluetooth prose.
        CAPLog.print("[SorchaProximity] transport error: \(message)")
        notifyListeners("disconnected", data: ["reason": "transportError"])
    }
}
