// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

import CoreBluetooth
import Foundation

/// The **holder** side of the radio: advertises, accepts one reader, and moves opaque bytes.
///
/// iOS restricts the peripheral role in the *background*. That is not a constraint here: a proximity
/// presentation is always foreground — the citizen is physically holding the phone up to a reader.
final class ProximityPeripheral: NSObject {

    private var manager: CBPeripheralManager?
    private var serviceUuid: CBUUID?

    private var server2Client: CBMutableCharacteristic?
    private var subscribedCentral: CBCentral?

    private let assembler = MessageAssembler()
    private var outbound: [Data] = []

    /// Whole messages received from the reader.
    var onMessage: ((Data) -> Void)?

    /// The channel ended.
    var onDisconnected: ((String) -> Void)?

    /// The manager reached a terminal error state before we could advertise.
    var onError: ((String) -> Void)?

    private var pendingStart: (() -> Void)?

    func start(serviceUuid: CBUUID) {
        self.serviceUuid = serviceUuid
        assembler.reset()
        outbound.removeAll()

        // Advertising can only begin once the manager reports .poweredOn, which is asynchronous. Queue the
        // work rather than racing it.
        pendingStart = { [weak self] in self?.publishService() }

        manager = CBPeripheralManager(delegate: self, queue: nil)
    }

    func send(_ message: Data) {
        guard let central = subscribedCentral, let characteristic = server2Client else {
            onError?("No reader is connected.")
            return
        }

        // maximumUpdateValueLength is the real ATT payload budget for this central — not a guess, and not
        // the 20-byte legacy default. Chunking to anything larger silently truncates.
        let mtu = central.maximumUpdateValueLength
        outbound.append(contentsOf: MdocGatt.chunk(message, mtu: mtu))
        drain()
    }

    /// Sends queued chunks until the transmit queue is full, then waits to be called back.
    private func drain() {
        guard let manager, let characteristic = server2Client else { return }

        while !outbound.isEmpty {
            let chunk = outbound[0]
            let sent = manager.updateValue(chunk, for: characteristic, onSubscribedCentrals: nil)

            if !sent {
                // The queue is full. CoreBluetooth will call peripheralManagerIsReady, and we resume there.
                // Dropping the chunk here would silently corrupt the message.
                return
            }

            outbound.removeFirst()
        }
    }

    func stop() {
        manager?.stopAdvertising()
        manager?.removeAllServices()
        manager = nil
        subscribedCentral = nil
        server2Client = nil
        outbound.removeAll()
        assembler.reset()
    }

    private func publishService() {
        guard let manager, let serviceUuid else { return }

        let state = CBMutableCharacteristic(
            type: MdocGatt.state,
            properties: [.write, .writeWithoutResponse, .notify],
            value: nil,
            permissions: [.writeable])

        let client2Server = CBMutableCharacteristic(
            type: MdocGatt.client2Server,
            properties: [.write, .writeWithoutResponse],
            value: nil,
            permissions: [.writeable])

        let server2Client = CBMutableCharacteristic(
            type: MdocGatt.server2Client,
            properties: [.notify],
            value: nil,
            permissions: [.readable])

        self.server2Client = server2Client

        let service = CBMutableService(type: serviceUuid, primary: true)
        service.characteristics = [state, client2Server, server2Client]

        manager.add(service)

        manager.startAdvertising([
            CBAdvertisementDataServiceUUIDsKey: [serviceUuid]
        ])
    }
}

extension ProximityPeripheral: CBPeripheralManagerDelegate {

    func peripheralManagerDidUpdateState(_ peripheral: CBPeripheralManager) {
        switch peripheral.state {
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

    func peripheralManager(
        _ peripheral: CBPeripheralManager,
        central: CBCentral,
        didSubscribeTo characteristic: CBCharacteristic
    ) {
        guard characteristic.uuid == MdocGatt.server2Client else { return }

        subscribedCentral = central

        // One session at a time: once a reader is talking to us, stop inviting others.
        peripheral.stopAdvertising()
    }

    func peripheralManager(
        _ peripheral: CBPeripheralManager,
        central: CBCentral,
        didUnsubscribeFrom characteristic: CBCharacteristic
    ) {
        guard characteristic.uuid == MdocGatt.server2Client else { return }

        subscribedCentral = nil
        onDisconnected?("peerDisconnected")
    }

    func peripheralManager(
        _ peripheral: CBPeripheralManager,
        didReceiveWrite requests: [CBATTRequest]
    ) {
        for request in requests {
            guard let value = request.value else { continue }

            switch request.characteristic.uuid {
            case MdocGatt.client2Server:
                if let message = assembler.accept(value) {
                    onMessage?(message)
                }

            case MdocGatt.state:
                if value.first == MdocGatt.stateEnd {
                    onDisconnected?("peerDisconnected")
                }

            default:
                break
            }
        }

        // Acknowledge, or the central stalls waiting on the write response.
        if let first = requests.first {
            peripheral.respond(to: first, withResult: .success)
        }
    }

    func peripheralManagerIsReady(toUpdateSubscribers peripheral: CBPeripheralManager) {
        // The transmit queue drained. Resume where we left off — this is the callback that makes a message
        // larger than the queue actually arrive intact.
        drain()
    }
}
