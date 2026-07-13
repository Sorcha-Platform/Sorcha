// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

package dev.sorcha.proximity

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * The chunker is the ONE piece of logic in this plugin, and therefore the one thing worth testing natively.
 *
 * Everything else is protocol, and protocol lives in C# where the whole exchange is already proven over a
 * loopback transport with no radio. What the radio owns is: a whole message in, the same whole message out,
 * across an MTU boundary. That is what these tests pin — and they must agree with the Swift ones, because a
 * holder on one platform will meet a reader on the other.
 */
class ChunkingTest {

    @Test
    fun `round trips across an MTU boundary`() {
        // Deliberately not a multiple of the payload budget — off-by-one in the final chunk is the classic
        // framing bug, and a message that divides evenly would hide it.
        val message = ByteArray(1000) { (it % 256).toByte() }
        val mtu = 23   // the BLE default; the smallest we will ever see

        val chunks = MdocGatt.chunk(message, mtu)
        assertTrue("a 1000-byte message must not fit in one 23-byte packet", chunks.size > 1)

        val assembler = MessageAssembler()
        var reassembled: ByteArray? = null

        for (chunk in chunks) {
            assertTrue("a chunk must never exceed the MTU", chunk.size <= mtu)
            assembler.accept(chunk)?.let { reassembled = it }
        }

        assertArrayEquals("the message must survive chunking byte-for-byte", message, reassembled)
    }

    @Test
    fun `surfaces a message only when it is complete`() {
        val message = ByteArray(500) { 0xAB.toByte() }
        val chunks = MdocGatt.chunk(message, 23)

        val assembler = MessageAssembler()

        for (chunk in chunks.dropLast(1)) {
            assertNull(
                "a partial message must NEVER be surfaced — the protocol layer would see half a CBOR item",
                assembler.accept(chunk)
            )
        }

        assertArrayEquals(message, assembler.accept(chunks.last()))
    }

    @Test
    fun `fits in a single chunk when small`() {
        val message = byteArrayOf(0x01, 0x02, 0x03)
        val chunks = MdocGatt.chunk(message, 512)

        assertEquals(1, chunks.size)
        assertEquals(MdocGatt.CHUNK_LAST, chunks[0][0])
        assertArrayEquals(message, MessageAssembler().accept(chunks[0]))
    }

    @Test
    fun `an empty message still produces one chunk`() {
        // Otherwise the peer waits forever for something we never send.
        val chunks = MdocGatt.chunk(ByteArray(0), 23)

        assertEquals(1, chunks.size)
        assertArrayEquals(ByteArray(0), MessageAssembler().accept(chunks[0]))
    }
}
