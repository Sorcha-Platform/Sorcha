// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

import CoreBluetooth
import Foundation

/// The ISO 18013-5 GATT profile for *mdoc peripheral server mode*, plus the framing that rides on it.
///
/// Kept in one place, and mirrored exactly by the Kotlin side (`MdocGatt.kt`). These are the only constants
/// that MUST be identical across the two native implementations — everything else that has to agree lives in
/// C# and is therefore written once.
enum MdocGatt {

    /// The characteristic UUID base for mdoc BLE.
    ///
    /// The service UUID itself is **not** in here: it is random per session and arrives in the
    /// `DeviceEngagement`. A fixed service UUID would let a passive observer correlate a citizen's
    /// presentations across time and place — precisely the tracking property proximity presentation exists
    /// to avoid.
    private static let base = "-A123-48CE-896B-4C76973373E6"

    /// State (`0x0001`) — the reader writes here to start and to end the session.
    static let state = CBUUID(string: "00000001\(base)")

    /// Client2Server (`0x0002`) — reader → holder. The reader WRITEs; the holder receives.
    static let client2Server = CBUUID(string: "00000002\(base)")

    /// Server2Client (`0x0003`) — holder → reader. The holder NOTIFIEs; the reader subscribes.
    static let server2Client = CBUUID(string: "00000003\(base)")

    /// Written to `state` to begin the session.
    static let stateStart: UInt8 = 0x01

    /// Written to `state` to end the session.
    static let stateEnd: UInt8 = 0x02

    // MARK: - Framing

    /// First byte of every chunk: more chunks follow.
    static let chunkMore: UInt8 = 0x01

    /// First byte of every chunk: this is the last chunk of the message.
    static let chunkLast: UInt8 = 0x00

    /// Splits a whole message into MTU-sized chunks, each prefixed with a continuation byte.
    ///
    /// The caller above this plugin deals in whole messages only. Reassembly is our problem, and getting it
    /// wrong would hand the protocol layer half a CBOR item — which fails in a way that looks like a crypto
    /// bug and is not one.
    static func chunk(_ message: Data, mtu: Int) -> [Data] {
        // One byte of every packet is the continuation flag, so the payload budget is MTU - 1.
        let payloadSize = max(1, mtu - 1)

        var chunks: [Data] = []
        var offset = 0

        while offset < message.count {
            let end = min(offset + payloadSize, message.count)
            let isLast = end == message.count

            var chunk = Data([isLast ? chunkLast : chunkMore])
            chunk.append(message.subdata(in: offset..<end))
            chunks.append(chunk)

            offset = end
        }

        // An empty message still needs one (last) chunk, or the peer waits forever for something we never send.
        if chunks.isEmpty {
            chunks.append(Data([chunkLast]))
        }

        return chunks
    }
}

/// Reassembles chunked messages, surfacing each one only when it is complete.
final class MessageAssembler {
    private var buffer = Data()

    /// Feeds one received chunk. Returns the whole message once the last chunk arrives, else `nil`.
    /// Returns `nil` (and drops the chunk) if the chunk is malformed — a truncated packet is not a message.
    func accept(_ chunk: Data) -> Data? {
        guard let flag = chunk.first else { return nil }

        buffer.append(chunk.dropFirst())

        guard flag == MdocGatt.chunkLast else { return nil }

        let message = buffer
        buffer = Data()
        return message
    }

    func reset() {
        buffer = Data()
    }
}
