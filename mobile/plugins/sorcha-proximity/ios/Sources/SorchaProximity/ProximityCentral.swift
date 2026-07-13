// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

import CoreBluetooth
import Foundation

/// The **reader** side of the radio: scans for the holder's advertised service, connects, and moves opaque bytes.
final class ProximityCentral: NSObject {

    private var manager: CBCentralManager?
    private var serviceUuid: CBUUID?

    private var peripheral: CBPeripheral?
    private var client2Server: CBCharacteristic?

    private let assembler = MessageAssembler()

    /// Whole messages received from the holder.
    var onMessage: ((Data) -> Void)?

    /// The channel ended.
    var onDisconnected: ((String) -> Void)?

    /// The manager reached a terminal error state before we could connect.
    var onError: ((String) -> Void)?

    private var pendingStart: (() -> Void)?

    func connect(serviceUuid: CBUUID) {
        self.serviceUuid = serviceUuid
        assembler.reset()

        pendingStart = { [weak self] in
            guard let self, let serviceUuid = self.serviceUuid else { return }
            // Scan only for this session's service. The UUID is random per exchange, so this is both the
            // narrowest possible scan and the one that cannot pick up somebody else's presentation.
            self.manager?.scanForPeripherals(withServices: [serviceUuid])
        }

        manager = CBCentralManager(delegate: self, queue: nil)
    }

    func send(_ message: Data) {
        guard let peripheral, let characteristic = client2Server else {
            onError?("Not connected to a holder.")
            return
        }

        // .withResponse: the ATT layer then flow-controls for us, so a large message cannot outrun the link.
        let mtu = peripheral.maximumWriteValueLength(for: .withResponse)

        for chunk in MdocGatt.chunk(message, mtu: mtu) {
            peripheral.writeValue(chunk, for: characteristic, type: .withResponse)
        }
    }

    func stop() {
        if let peripheral {
            manager?.cancelPeripheralConnection(peripheral)
        }
        manager?.stopScan()
        manager = nil
        peripheral = nil
        client2Server = nil
        assembler.reset()
    }
}

extension ProximityCentral: CBCentralManagerDelegate {

    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        switch central.state {
        case .poweredOn:
            pendingStart?()
            pendingStart = nil

        case .unauthorized:
            onError?("Bluetooth permission has not been granted.")

        case .poweredOff:
            onError?("Bluetooth is switched off.")

        case .unsupported:
            onError?("This device has no Bluetooth radio.")

        default:
            break
        }
    }

    func centralManager(
        _ central: CBCentralManager,
        didDiscover peripheral: CBPeripheral,
        advertisementData: [String: Any],
        rssi RSSI: NSNumber
    ) {
        // First match wins. The service UUID is unique to this exchange, so there is nothing to choose between.
        central.stopScan()

        self.peripheral = peripheral
        peripheral.delegate = self
        central.connect(peripheral)
    }

    func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        guard let serviceUuid else { return }
        peripheral.discoverServices([serviceUuid])
    }

    func centralManager(
        _ central: CBCentralManager,
        didDisconnectPeripheral peripheral: CBPeripheral,
        error: Error?
    ) {
        onDisconnected?("peerDisconnected")
    }

    func centralManager(
        _ central: CBCentralManager,
        didFailToConnect peripheral: CBPeripheral,
        error: Error?
    ) {
        onDisconnected?("transportError")
    }
}

extension ProximityCentral: CBPeripheralDelegate {

    func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
        guard error == nil, let service = peripheral.services?.first else {
            onDisconnected?("transportError")
            return
        }

        peripheral.discoverCharacteristics(
            [MdocGatt.state, MdocGatt.client2Server, MdocGatt.server2Client],
            for: service)
    }

    func peripheral(
        _ peripheral: CBPeripheral,
        didDiscoverCharacteristicsFor service: CBService,
        error: Error?
    ) {
        guard error == nil, let characteristics = service.characteristics else {
            onDisconnected?("transportError")
            return
        }

        for characteristic in characteristics {
            switch characteristic.uuid {
            case MdocGatt.client2Server:
                client2Server = characteristic

            case MdocGatt.server2Client:
                // Subscribing is what tells the holder a reader has arrived — it is the holder's
                // didSubscribeTo callback, and therefore the real start of the session.
                peripheral.setNotifyValue(true, for: characteristic)

            case MdocGatt.state:
                // Announce the start explicitly, so the holder is not left inferring it.
                peripheral.writeValue(Data([MdocGatt.stateStart]), for: characteristic, type: .withResponse)

            default:
                break
            }
        }
    }

    func peripheral(
        _ peripheral: CBPeripheral,
        didUpdateValueFor characteristic: CBCharacteristic,
        error: Error?
    ) {
        guard characteristic.uuid == MdocGatt.server2Client,
              let value = characteristic.value else { return }

        if let message = assembler.accept(value) {
            onMessage?(message)
        }
    }
}
