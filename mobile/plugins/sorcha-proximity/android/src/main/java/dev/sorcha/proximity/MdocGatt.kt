// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

package dev.sorcha.proximity

import java.util.UUID

/**
 * The ISO 18013-5 GATT profile for *mdoc peripheral server mode*, plus the framing that rides on it.
 *
 * These constants MUST match `MdocGatt.swift` exactly. They are the only thing that has to agree across the
 * two native implementations — everything else that has to agree lives in C# and is therefore written once.
 */
object MdocGatt {

    /**
     * The characteristic UUID base for mdoc BLE.
     *
     * The service UUID itself is deliberately NOT here: it is random per session and arrives in the
     * `DeviceEngagement`. A fixed service UUID would let a passive observer correlate a citizen's
     * presentations across time and place — precisely the tracking property proximity presentation exists to
     * avoid.
     */
    private const val BASE = "-a123-48ce-896b-4c76973373e6"

    /** State (`0x0001`) — the reader writes here to start and to end the session. */
    val STATE: UUID = UUID.fromString("00000001$BASE")

    /** Client2Server (`0x0002`) — reader → holder. The reader WRITEs; the holder receives. */
    val CLIENT_2_SERVER: UUID = UUID.fromString("00000002$BASE")

    /** Server2Client (`0x0003`) — holder → reader. The holder NOTIFIEs; the reader subscribes. */
    val SERVER_2_CLIENT: UUID = UUID.fromString("00000003$BASE")

    /** The Client Characteristic Configuration descriptor. Without it, notifications never arrive. */
    val CCC_DESCRIPTOR: UUID = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb")

    /** Written to `state` to begin the session. */
    const val STATE_START: Byte = 0x01

    /** Written to `state` to end the session. */
    const val STATE_END: Byte = 0x02

    /** First byte of every chunk: more chunks follow. */
    const val CHUNK_MORE: Byte = 0x01

    /** First byte of every chunk: this is the last chunk of the message. */
    const val CHUNK_LAST: Byte = 0x00

    /**
     * Splits a whole message into MTU-sized chunks, each prefixed with a continuation byte.
     *
     * The caller above this plugin deals in whole messages only. Reassembly is our problem, and getting it
     * wrong would hand the protocol layer half a CBOR item — which fails in a way that looks like a crypto
     * bug and is not one.
     */
    fun chunk(message: ByteArray, mtu: Int): List<ByteArray> {
        // One byte of every packet is the continuation flag, so the payload budget is MTU - 1.
        val payloadSize = maxOf(1, mtu - 1)

        val chunks = mutableListOf<ByteArray>()
        var offset = 0

        while (offset < message.size) {
            val end = minOf(offset + payloadSize, message.size)
            val isLast = end == message.size

            val chunk = ByteArray(1 + (end - offset))
            chunk[0] = if (isLast) CHUNK_LAST else CHUNK_MORE
            message.copyInto(chunk, destinationOffset = 1, startIndex = offset, endIndex = end)
            chunks.add(chunk)

            offset = end
        }

        // An empty message still needs one (last) chunk, or the peer waits forever for something we never send.
        if (chunks.isEmpty()) {
            chunks.add(byteArrayOf(CHUNK_LAST))
        }

        return chunks
    }
}

/** Reassembles chunked messages, surfacing each one only when it is complete. */
class MessageAssembler {

    private var buffer = ByteArray(0)

    /**
     * Feeds one received chunk. Returns the whole message once the last chunk arrives, else `null`.
     * A malformed (empty) chunk is dropped — a truncated packet is not a message.
     */
    fun accept(chunk: ByteArray): ByteArray? {
        if (chunk.isEmpty()) return null

        val flag = chunk[0]
        buffer += chunk.copyOfRange(1, chunk.size)

        if (flag != MdocGatt.CHUNK_LAST) return null

        val message = buffer
        buffer = ByteArray(0)
        return message
    }

    fun reset() {
        buffer = ByteArray(0)
    }
}
