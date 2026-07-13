// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

package dev.sorcha.proximity

import android.Manifest
import android.bluetooth.BluetoothManager
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.util.Base64
import androidx.core.content.ContextCompat
import com.getcapacitor.JSObject
import com.getcapacitor.Plugin
import com.getcapacitor.PluginCall
import com.getcapacitor.PluginMethod
import com.getcapacitor.annotation.CapacitorPlugin
import com.getcapacitor.annotation.Permission
import com.getcapacitor.annotation.PermissionCallback
import java.util.UUID

/**
 * The Capacitor bridge for ISO 18013-5 BLE proximity.
 *
 * **This class moves opaque bytes and nothing else.** No CBOR, no mdoc, no credentials, no session state —
 * every byte of ISO protocol lives in C# (`Sorcha.Mdoc`), written once and shared by the holder and the
 * reader. Protocol logic here would be logic the C# test suite cannot reach, implemented twice, in two
 * languages, with two chances to get the tag-24 rules wrong.
 *
 * Bytes cross as **base64 strings**, matching every other JS bridge in this wallet.
 */
@CapacitorPlugin(
    name = "SorchaProximity",
    permissions = [
        Permission(
            alias = SorchaProximityPlugin.BLUETOOTH,
            strings = [
                Manifest.permission.BLUETOOTH_ADVERTISE,
                Manifest.permission.BLUETOOTH_CONNECT,
                Manifest.permission.BLUETOOTH_SCAN
            ]
        )
    ]
)
class SorchaProximityPlugin : Plugin() {

    companion object {
        const val BLUETOOTH = "bluetooth"
    }

    private var peripheral: ProximityPeripheral? = null
    private var central: ProximityCentral? = null

    // ---- probe -------------------------------------------------------------------------------

    /**
     * Reports capability. **Never rejects** — an unsupported device resolves with `supported: false` and a
     * reason, so the UI hides the affordance rather than offering something broken.
     */
    @PluginMethod
    fun probe(call: PluginCall) {
        val manager = context.getSystemService(Context.BLUETOOTH_SERVICE) as? BluetoothManager
        val adapter = manager?.adapter

        val result = JSObject()

        if (adapter == null) {
            result.put("supported", false)
            result.put("bluetoothEnabled", false)
            result.put("permissionGranted", false)
            result.put("reason", "noBluetoothHardware")
            call.resolve(result)
            return
        }

        val granted = hasBluetoothPermissions()

        result.put("supported", true)
        result.put("bluetoothEnabled", adapter.isEnabled)
        result.put("permissionGranted", granted)

        if (!adapter.isEnabled) {
            // Recoverable: the citizen can turn it on. Reported distinctly from "denied" so the UI can say so.
            result.put("supported", false)
            result.put("reason", "bluetoothOff")
        } else if (!granted) {
            // "Not yet asked" is NOT the same as "refused" — one is a prompt away, the other needs Settings.
            // Collapsing them would dead-end a citizen who has simply never been asked.
            result.put("supported", false)
            result.put("reason", "permissionNotYetRequested")
        }

        call.resolve(result)
    }

    private fun hasBluetoothPermissions(): Boolean {
        // The runtime BLE permissions only exist from Android 12 (S). Below that they are install-time.
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S) return true

        return listOf(
            Manifest.permission.BLUETOOTH_ADVERTISE,
            Manifest.permission.BLUETOOTH_CONNECT,
            Manifest.permission.BLUETOOTH_SCAN
        ).all {
            ContextCompat.checkSelfPermission(context, it) == PackageManager.PERMISSION_GRANTED
        }
    }

    // ---- holder ------------------------------------------------------------------------------

    @PluginMethod
    fun startPeripheral(call: PluginCall) {
        if (!hasBluetoothPermissions()) {
            requestPermissionForAlias(BLUETOOTH, call, "permissionsCallback")
            return
        }
        startPeripheralGranted(call)
    }

    private fun startPeripheralGranted(call: PluginCall) {
        val uuid = call.parseServiceUuid() ?: return

        stopAll()

        peripheral = ProximityPeripheral(
            context,
            onMessage = { emitReceived(it) },
            onDisconnected = { emitDisconnected(it) },
            onError = { emitError(it) }
        ).also { it.start(uuid) }

        call.resolve()
    }

    // ---- reader ------------------------------------------------------------------------------

    @PluginMethod
    fun connectCentral(call: PluginCall) {
        if (!hasBluetoothPermissions()) {
            requestPermissionForAlias(BLUETOOTH, call, "permissionsCallback")
            return
        }
        connectCentralGranted(call)
    }

    private fun connectCentralGranted(call: PluginCall) {
        val uuid = call.parseServiceUuid() ?: return

        stopAll()

        central = ProximityCentral(
            context,
            onMessage = { emitReceived(it) },
            onDisconnected = { emitDisconnected(it) },
            onError = { emitError(it) }
        ).also { it.connect(uuid) }

        call.resolve()
    }

    @PermissionCallback
    private fun permissionsCallback(call: PluginCall) {
        if (!hasBluetoothPermissions()) {
            call.reject("Bluetooth permission is needed to share in person.")
            return
        }

        // Resume whichever role was being started when we had to stop and ask.
        when (call.methodName) {
            "startPeripheral" -> startPeripheralGranted(call)
            "connectCentral" -> connectCentralGranted(call)
            else -> call.reject("Unexpected permission callback for ${call.methodName}.")
        }
    }

    // ---- transport ---------------------------------------------------------------------------

    @PluginMethod
    fun send(call: PluginCall) {
        val base64 = call.getString("dataBase64")
        if (base64 == null) {
            call.reject("dataBase64 is required.")
            return
        }

        val data = try {
            Base64.decode(base64, Base64.NO_WRAP)
        } catch (e: IllegalArgumentException) {
            call.reject("dataBase64 is not valid base64.")
            return
        }

        val holder = peripheral
        val reader = central

        when {
            holder != null -> holder.send(data)
            reader != null -> reader.send(data)
            else -> {
                call.reject("No proximity session is active.")
                return
            }
        }

        call.resolve()
    }

    /**
     * Idempotent and safe from any state — sessions are abandoned from arbitrary points (screen lock, the
     * citizen walking away), and tearing down must never itself be a failure.
     */
    @PluginMethod
    fun stop(call: PluginCall) {
        stopAll()
        call.resolve()
    }

    private fun stopAll() {
        peripheral?.stop()
        peripheral = null
        central?.stop()
        central = null
    }

    // ---- events ------------------------------------------------------------------------------

    private fun emitReceived(data: ByteArray) {
        val event = JSObject()
        event.put("dataBase64", Base64.encodeToString(data, Base64.NO_WRAP))
        notifyListeners("received", event)
    }

    private fun emitDisconnected(reason: String) {
        val event = JSObject()
        event.put("reason", reason)
        notifyListeners("disconnected", event)
    }

    private fun emitError(message: String) {
        // Surfaced as a disconnect, because from the protocol's point of view that is what it is: the channel
        // is not usable. The raw message goes to the log — the C# layer maps reasons to citizen-facing copy
        // and must not be handed raw Android BLE prose.
        android.util.Log.w("SorchaProximity", "transport error: $message")

        val event = JSObject()
        event.put("reason", "transportError")
        notifyListeners("disconnected", event)
    }

    private fun PluginCall.parseServiceUuid(): UUID? {
        val raw = getString("serviceUuid")
        if (raw == null) {
            reject("serviceUuid is required.")
            return null
        }
        return try {
            UUID.fromString(raw)
        } catch (e: IllegalArgumentException) {
            reject("serviceUuid must be a UUID.")
            null
        }
    }
}
