// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

package dev.sorcha.proximity

import android.annotation.SuppressLint
import android.bluetooth.BluetoothGatt
import android.bluetooth.BluetoothGattCallback
import android.bluetooth.BluetoothGattCharacteristic
import android.bluetooth.BluetoothGattDescriptor
import android.bluetooth.BluetoothManager
import android.bluetooth.le.ScanCallback
import android.bluetooth.le.ScanFilter
import android.bluetooth.le.ScanResult
import android.bluetooth.le.ScanSettings
import android.content.Context
import android.os.ParcelUuid
import java.util.UUID
import java.util.concurrent.ConcurrentLinkedQueue

/**
 * The **reader** side of the radio: scans for the holder's advertised service, connects, and moves opaque bytes.
 */
@SuppressLint("MissingPermission") // The plugin probes and requests permissions before ever calling in here.
class ProximityCentral(
    private val context: Context,
    private val onMessage: (ByteArray) -> Unit,
    private val onDisconnected: (String) -> Unit,
    private val onError: (String) -> Unit
) {

    private val manager = context.getSystemService(Context.BLUETOOTH_SERVICE) as BluetoothManager

    private var gatt: BluetoothGatt? = null
    private var client2Server: BluetoothGattCharacteristic? = null
    private var stateCharacteristic: BluetoothGattCharacteristic? = null

    private val assembler = MessageAssembler()
    private val outbound = ConcurrentLinkedQueue<ByteArray>()

    @Volatile
    private var mtu = 23

    @Volatile
    private var writing = false

    private var serviceUuid: UUID? = null
    private var scanCallback: ScanCallback? = null

    fun connect(serviceUuid: UUID) {
        this.serviceUuid = serviceUuid
        assembler.reset()
        outbound.clear()

        val adapter = manager.adapter
        if (adapter == null || !adapter.isEnabled) {
            onError("Bluetooth is switched off.")
            return
        }

        // Scan only for this session's service. The UUID is random per exchange, so this is both the
        // narrowest possible scan and the one that cannot pick up somebody else's presentation.
        val filter = ScanFilter.Builder()
            .setServiceUuid(ParcelUuid(serviceUuid))
            .build()

        val settings = ScanSettings.Builder()
            .setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY)
            .build()

        val callback = object : ScanCallback() {
            override fun onScanResult(callbackType: Int, result: ScanResult) {
                // First match wins — the service UUID is unique to this exchange, so there is nothing to
                // choose between.
                stopScanning()
                gatt = result.device.connectGatt(context, false, gattCallback)
            }

            override fun onScanFailed(errorCode: Int) {
                onError("Could not scan for the holder (code $errorCode).")
            }
        }
        scanCallback = callback

        adapter.bluetoothLeScanner?.startScan(listOf(filter), settings, callback)
            ?: onError("This device cannot scan for BLE.")
    }

    fun send(message: ByteArray) {
        val characteristic = client2Server
        if (characteristic == null) {
            onError("Not connected to a holder.")
            return
        }

        outbound.addAll(MdocGatt.chunk(message, mtu))
        drain()
    }

    /**
     * Writes one chunk and waits for onCharacteristicWrite before the next.
     *
     * Android allows exactly one outstanding GATT write; queueing them in a loop drops all but the first, and
     * does so intermittently.
     */
    @Synchronized
    private fun drain() {
        if (writing) return

        val characteristic = client2Server ?: return
        val chunk = outbound.poll() ?: return

        writing = true

        @Suppress("DEPRECATION")
        characteristic.value = chunk
        @Suppress("DEPRECATION")
        characteristic.writeType = BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT
        @Suppress("DEPRECATION")
        gatt?.writeCharacteristic(characteristic)
    }

    private fun stopScanning() {
        scanCallback?.let { manager.adapter?.bluetoothLeScanner?.stopScan(it) }
        scanCallback = null
    }

    fun stop() {
        stopScanning()
        gatt?.disconnect()
        gatt?.close()
        gatt = null
        client2Server = null
        stateCharacteristic = null
        outbound.clear()
        assembler.reset()
    }

    private val gattCallback = object : BluetoothGattCallback() {

        override fun onConnectionStateChange(gatt: BluetoothGatt, status: Int, newState: Int) {
            when (newState) {
                BluetoothGatt.STATE_CONNECTED ->
                    // Ask for a bigger MTU before anything else: at the 23-byte default, a DeviceResponse
                    // would be hundreds of packets.
                    gatt.requestMtu(517)

                BluetoothGatt.STATE_DISCONNECTED ->
                    onDisconnected("peerDisconnected")
            }
        }

        override fun onMtuChanged(gatt: BluetoothGatt, newMtu: Int, status: Int) {
            // The ATT header eats 3 bytes of every packet.
            mtu = newMtu - 3
            gatt.discoverServices()
        }

        override fun onServicesDiscovered(gatt: BluetoothGatt, status: Int) {
            val service = serviceUuid?.let { gatt.getService(it) }
            if (service == null) {
                onDisconnected("transportError")
                return
            }

            client2Server = service.getCharacteristic(MdocGatt.CLIENT_2_SERVER)
            stateCharacteristic = service.getCharacteristic(MdocGatt.STATE)

            val notify = service.getCharacteristic(MdocGatt.SERVER_2_CLIENT)
            if (notify == null || client2Server == null) {
                onDisconnected("transportError")
                return
            }

            // Subscribing is what tells the holder a reader has arrived. Both halves are required: the local
            // enable, AND the CCC descriptor write that actually reaches the peer.
            gatt.setCharacteristicNotification(notify, true)

            val ccc = notify.getDescriptor(MdocGatt.CCC_DESCRIPTOR)
            if (ccc != null) {
                @Suppress("DEPRECATION")
                ccc.value = BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE
                @Suppress("DEPRECATION")
                gatt.writeDescriptor(ccc)
            }
        }

        override fun onDescriptorWrite(gatt: BluetoothGatt, descriptor: BluetoothGattDescriptor, status: Int) {
            // Notifications are live. Announce the start explicitly, so the holder is not left inferring it.
            stateCharacteristic?.let {
                @Suppress("DEPRECATION")
                it.value = byteArrayOf(MdocGatt.STATE_START)
                @Suppress("DEPRECATION")
                gatt.writeCharacteristic(it)
            }
        }

        override fun onCharacteristicWrite(
            gatt: BluetoothGatt,
            characteristic: BluetoothGattCharacteristic,
            status: Int
        ) {
            if (characteristic.uuid != MdocGatt.CLIENT_2_SERVER) return

            synchronized(this@ProximityCentral) {
                writing = false
            }
            drain()   // the previous chunk is away; send the next
        }

        @Suppress("DEPRECATION")
        override fun onCharacteristicChanged(
            gatt: BluetoothGatt,
            characteristic: BluetoothGattCharacteristic
        ) {
            if (characteristic.uuid != MdocGatt.SERVER_2_CLIENT) return
            assembler.accept(characteristic.value)?.let(onMessage)
        }
    }
}
