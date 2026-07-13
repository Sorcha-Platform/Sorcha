// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

package dev.sorcha.proximity

import android.annotation.SuppressLint
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothGatt
import android.bluetooth.BluetoothGattCharacteristic
import android.bluetooth.BluetoothGattDescriptor
import android.bluetooth.BluetoothGattServer
import android.bluetooth.BluetoothGattServerCallback
import android.bluetooth.BluetoothGattService
import android.bluetooth.BluetoothManager
import android.bluetooth.le.AdvertiseCallback
import android.bluetooth.le.AdvertiseData
import android.bluetooth.le.AdvertiseSettings
import android.content.Context
import android.os.ParcelUuid
import java.util.UUID
import java.util.concurrent.ConcurrentLinkedQueue

/**
 * The **holder** side of the radio: advertises, accepts one reader, and moves opaque bytes.
 */
@SuppressLint("MissingPermission") // The plugin probes and requests permissions before ever calling in here.
class ProximityPeripheral(
    private val context: Context,
    private val onMessage: (ByteArray) -> Unit,
    private val onDisconnected: (String) -> Unit,
    private val onError: (String) -> Unit
) {

    private val manager = context.getSystemService(Context.BLUETOOTH_SERVICE) as BluetoothManager

    private var server: BluetoothGattServer? = null
    private var server2Client: BluetoothGattCharacteristic? = null
    private var connected: BluetoothDevice? = null

    private val assembler = MessageAssembler()
    private val outbound = ConcurrentLinkedQueue<ByteArray>()

    /**
     * Android negotiates the MTU and tells us on onMtuChanged. Until then, assume the BLE default (23) —
     * assuming larger and being wrong silently truncates every packet.
     */
    @Volatile
    private var mtu = 23

    @Volatile
    private var sending = false

    private var advertiseCallback: AdvertiseCallback? = null

    fun start(serviceUuid: UUID) {
        assembler.reset()
        outbound.clear()

        val adapter = manager.adapter
        if (adapter == null || !adapter.isEnabled) {
            onError("Bluetooth is switched off.")
            return
        }

        val gattServer = manager.openGattServer(context, serverCallback)
        if (gattServer == null) {
            onError("Could not open a GATT server.")
            return
        }
        server = gattServer

        val state = BluetoothGattCharacteristic(
            MdocGatt.STATE,
            BluetoothGattCharacteristic.PROPERTY_WRITE or
                BluetoothGattCharacteristic.PROPERTY_WRITE_NO_RESPONSE or
                BluetoothGattCharacteristic.PROPERTY_NOTIFY,
            BluetoothGattCharacteristic.PERMISSION_WRITE
        )

        val client2Server = BluetoothGattCharacteristic(
            MdocGatt.CLIENT_2_SERVER,
            BluetoothGattCharacteristic.PROPERTY_WRITE or
                BluetoothGattCharacteristic.PROPERTY_WRITE_NO_RESPONSE,
            BluetoothGattCharacteristic.PERMISSION_WRITE
        )

        val notify = BluetoothGattCharacteristic(
            MdocGatt.SERVER_2_CLIENT,
            BluetoothGattCharacteristic.PROPERTY_NOTIFY,
            BluetoothGattCharacteristic.PERMISSION_READ
        ).apply {
            // Without the CCC descriptor the central cannot subscribe, and notifications silently never
            // arrive. This is the classic Android BLE omission.
            addDescriptor(
                BluetoothGattDescriptor(
                    MdocGatt.CCC_DESCRIPTOR,
                    BluetoothGattDescriptor.PERMISSION_READ or BluetoothGattDescriptor.PERMISSION_WRITE
                )
            )
        }
        server2Client = notify

        val service = BluetoothGattService(serviceUuid, BluetoothGattService.SERVICE_TYPE_PRIMARY).apply {
            addCharacteristic(state)
            addCharacteristic(client2Server)
            addCharacteristic(notify)
        }
        gattServer.addService(service)

        val settings = AdvertiseSettings.Builder()
            .setAdvertiseMode(AdvertiseSettings.ADVERTISE_MODE_LOW_LATENCY)
            .setConnectable(true)
            .setTimeout(0)
            .build()

        val data = AdvertiseData.Builder()
            .setIncludeDeviceName(false)   // the citizen's device name is not the reader's business
            .addServiceUuid(ParcelUuid(serviceUuid))
            .build()

        val callback = object : AdvertiseCallback() {
            override fun onStartFailure(errorCode: Int) {
                onError("Could not start advertising (code $errorCode).")
            }
        }
        advertiseCallback = callback

        adapter.bluetoothLeAdvertiser?.startAdvertising(settings, data, callback)
            ?: onError("This device cannot advertise over BLE.")
    }

    fun send(message: ByteArray) {
        val device = connected
        val characteristic = server2Client
        if (device == null || characteristic == null) {
            onError("No reader is connected.")
            return
        }

        outbound.addAll(MdocGatt.chunk(message, mtu))
        drain()
    }

    /**
     * Sends one chunk and waits for onNotificationSent before the next.
     *
     * Android will silently drop a notification queued while another is in flight, so firing them in a loop
     * corrupts any message larger than one packet — and does so intermittently, which is the worst kind of bug.
     */
    @Synchronized
    private fun drain() {
        if (sending) return

        val device = connected ?: return
        val characteristic = server2Client ?: return
        val chunk = outbound.poll() ?: return

        sending = true

        @Suppress("DEPRECATION")
        characteristic.value = chunk
        @Suppress("DEPRECATION")
        server?.notifyCharacteristicChanged(device, characteristic, false)
    }

    fun stop() {
        advertiseCallback?.let {
            manager.adapter?.bluetoothLeAdvertiser?.stopAdvertising(it)
        }
        advertiseCallback = null

        connected?.let { server?.cancelConnection(it) }
        server?.close()
        server = null
        server2Client = null
        connected = null
        outbound.clear()
        assembler.reset()
    }

    private val serverCallback = object : BluetoothGattServerCallback() {

        override fun onConnectionStateChange(device: BluetoothDevice, status: Int, newState: Int) {
            when (newState) {
                BluetoothGatt.STATE_CONNECTED -> {
                    connected = device
                    // One session at a time: once a reader is talking to us, stop inviting others.
                    advertiseCallback?.let {
                        manager.adapter?.bluetoothLeAdvertiser?.stopAdvertising(it)
                    }
                }

                BluetoothGatt.STATE_DISCONNECTED -> {
                    connected = null
                    onDisconnected("peerDisconnected")
                }
            }
        }

        override fun onMtuChanged(device: BluetoothDevice, newMtu: Int) {
            // The ATT header eats 3 bytes of every packet — chunking to the full MTU truncates.
            mtu = newMtu - 3
        }

        override fun onCharacteristicWriteRequest(
            device: BluetoothDevice,
            requestId: Int,
            characteristic: BluetoothGattCharacteristic,
            preparedWrite: Boolean,
            responseNeeded: Boolean,
            offset: Int,
            value: ByteArray
        ) {
            when (characteristic.uuid) {
                MdocGatt.CLIENT_2_SERVER ->
                    assembler.accept(value)?.let(onMessage)

                MdocGatt.STATE ->
                    if (value.isNotEmpty() && value[0] == MdocGatt.STATE_END) {
                        onDisconnected("peerDisconnected")
                    }
            }

            if (responseNeeded) {
                server?.sendResponse(device, requestId, BluetoothGatt.GATT_SUCCESS, 0, null)
            }
        }

        override fun onDescriptorWriteRequest(
            device: BluetoothDevice,
            requestId: Int,
            descriptor: BluetoothGattDescriptor,
            preparedWrite: Boolean,
            responseNeeded: Boolean,
            offset: Int,
            value: ByteArray
        ) {
            // The central subscribing to Server2Client — this is the real start of the session.
            if (responseNeeded) {
                server?.sendResponse(device, requestId, BluetoothGatt.GATT_SUCCESS, 0, null)
            }
        }

        override fun onNotificationSent(device: BluetoothDevice, status: Int) {
            synchronized(this@ProximityPeripheral) {
                sending = false
            }
            drain()   // the previous chunk is away; send the next
        }
    }
}
