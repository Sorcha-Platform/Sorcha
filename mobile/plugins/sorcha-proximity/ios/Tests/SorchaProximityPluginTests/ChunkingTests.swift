// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

import XCTest
@testable import SorchaProximityPlugin

/// The chunker is the ONE piece of logic in this plugin, and therefore the one thing worth testing natively.
///
/// Everything else is protocol, and protocol lives in C# where the whole exchange is already proven over a
/// loopback transport with no radio. What the radio owns is: a whole message in, the same whole message out,
/// across an MTU boundary. That is what these tests pin.
final class ChunkingTests: XCTestCase {

    func testRoundTripsAcrossAnMtuBoundary() {
        // Deliberately not a multiple of the payload budget — off-by-one in the final chunk is the classic
        // framing bug, and a message that divides evenly would hide it.
        let message = Data((0..<1000).map { UInt8($0 % 256) })
        let mtu = 23   // the BLE default; the smallest we will ever see

        let chunks = MdocGatt.chunk(message, mtu: mtu)
        XCTAssertGreaterThan(chunks.count, 1, "a 1000-byte message must not fit in one 23-byte packet")

        let assembler = MessageAssembler()
        var reassembled: Data?

        for chunk in chunks {
            XCTAssertLessThanOrEqual(chunk.count, mtu, "a chunk must never exceed the MTU")
            if let message = assembler.accept(chunk) {
                reassembled = message
            }
        }

        XCTAssertEqual(reassembled, message, "the message must survive chunking byte-for-byte")
    }

    func testMessageIsSurfacedOnlyWhenComplete() {
        let message = Data(repeating: 0xAB, count: 500)
        let chunks = MdocGatt.chunk(message, mtu: 23)

        let assembler = MessageAssembler()

        for chunk in chunks.dropLast() {
            XCTAssertNil(assembler.accept(chunk), "a partial message must NEVER be surfaced — the protocol layer would see half a CBOR item")
        }

        XCTAssertEqual(assembler.accept(chunks.last!), message)
    }

    func testFitsInASingleChunkWhenSmall() {
        let message = Data([0x01, 0x02, 0x03])
        let chunks = MdocGatt.chunk(message, mtu: 512)

        XCTAssertEqual(chunks.count, 1)
        XCTAssertEqual(chunks[0].first, MdocGatt.chunkLast)
        XCTAssertEqual(MessageAssembler().accept(chunks[0]), message)
    }

    func testEmptyMessageStillProducesOneChunk() {
        // Otherwise the peer waits forever for something we never send.
        let chunks = MdocGatt.chunk(Data(), mtu: 23)

        XCTAssertEqual(chunks.count, 1)
        XCTAssertEqual(MessageAssembler().accept(chunks[0]), Data())
    }
}
