// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Formats.Cbor;
using Org.BouncyCastle.Crypto.Parameters;
using Sorcha.Mdoc.Cbor;
using Sorcha.Mdoc.Cose;

namespace Sorcha.Mdoc.Proximity;

/// <summary>How the reader should connect back to the holder.</summary>
public sealed class BleRetrievalOptions
{
    /// <summary>The holder acts as the BLE peripheral (GATT server) and the reader connects to it.</summary>
    /// <remarks>The mode this feature ships: it matches the physical ritual of holding a phone up to a reader.</remarks>
    public bool SupportsPeripheralServerMode { get; init; }

    /// <summary>The reader acts as the BLE peripheral and the holder connects to it.</summary>
    public bool SupportsCentralClientMode { get; init; }

    /// <summary>The service UUID to advertise / scan for in peripheral-server mode.</summary>
    /// <remarks>
    /// Freshly random per session — it is not a fixed, well-known UUID. A stable UUID would let a passive
    /// observer correlate a citizen's presentations across time and place, which is precisely the tracking
    /// property proximity presentation is supposed to avoid.
    /// </remarks>
    public Guid? PeripheralServerUuid { get; init; }

    /// <summary>The service UUID for central-client mode.</summary>
    public Guid? CentralClientUuid { get; init; }
}

/// <summary>
/// The holder's opening offer, encoded into the QR code the reader scans: an ephemeral public key plus how
/// to reach the holder.
/// </summary>
/// <remarks>
/// <para>
/// <c>DeviceEngagement = { 0: version, 1: Security, 2: DeviceRetrievalMethods }</c> where
/// <c>Security = [ cipherSuite, EDeviceKeyBytes ]</c>.
/// </para>
/// <para>
/// <b>It says nothing about the citizen.</b> It is pre-identity by construction — an ephemeral key and a
/// random service UUID. Nothing here identifies the holder or what they hold, which is what makes it safe to
/// display as a QR code to anyone who asks.
/// </para>
/// </remarks>
public sealed class DeviceEngagement
{
    /// <summary>The only version this implementation speaks.</summary>
    public const string Version1_0 = "1.0";

    /// <summary>Cipher suite 1 — the sole suite defined by ISO 18013-5 (P-256 / AES-256-GCM / HKDF-SHA256).</summary>
    public const int CipherSuiteP256 = 1;

    private const int RetrievalMethodTypeBle = 2;
    private const int BleRetrievalVersion = 1;

    // BleOptions map keys (ISO 18013-5 §8.2.1.1).
    private const int BleOptionSupportsPeripheralServer = 0;
    private const int BleOptionSupportsCentralClient = 1;
    private const int BleOptionPeripheralServerUuid = 10;
    private const int BleOptionCentralClientUuid = 11;

    /// <summary>Engagement version.</summary>
    public string Version { get; init; } = Version1_0;

    /// <summary>The tag-24-wrapped <c>EDeviceKeyBytes</c> — the holder's <b>ephemeral</b> session COSE_Key.</summary>
    /// <remarks>
    /// Ephemeral, not the mdoc's static device key. It is regenerated for every exchange; reuse would let two
    /// presentations be linked to the same phone.
    /// </remarks>
    public required byte[] EDeviceKeyBytes { get; init; }

    /// <summary>How to connect over BLE.</summary>
    public required BleRetrievalOptions Ble { get; init; }

    /// <summary>Builds an engagement for the peripheral-server mode this feature ships.</summary>
    /// <param name="ephemeralPublicKey">The holder's freshly generated ephemeral public key.</param>
    /// <param name="serviceUuid">A freshly random per-session service UUID.</param>
    public static DeviceEngagement ForBlePeripheralServer(ECPublicKeyParameters ephemeralPublicKey, Guid serviceUuid)
    {
        ArgumentNullException.ThrowIfNull(ephemeralPublicKey);

        return new DeviceEngagement
        {
            EDeviceKeyBytes = MdocCbor.WrapTag24(CoseKey.EncodeEc2PublicKey(ephemeralPublicKey)),
            Ble = new BleRetrievalOptions
            {
                SupportsPeripheralServerMode = true,
                SupportsCentralClientMode = false,
                PeripheralServerUuid = serviceUuid
            }
        };
    }

    /// <summary>Reads the holder's ephemeral public key out of <see cref="EDeviceKeyBytes"/>.</summary>
    /// <returns><see langword="null"/> when the key is absent or not a valid P-256 EC2 COSE_Key.</returns>
    public ECPublicKeyParameters? TryGetEphemeralPublicKey()
    {
        try
        {
            return CoseKey.TryParseEc2PublicKey(MdocCbor.UnwrapTag24(EDeviceKeyBytes));
        }
        catch (CborContentException)
        {
            return null;
        }
    }

    /// <summary>Encodes to CBOR — the bytes that go into the QR code.</summary>
    public byte[] Encode() => MdocCbor.Encode(w =>
    {
        w.WriteStartMap(3);

        w.WriteInt32(0);
        w.WriteTextString(Version);

        w.WriteInt32(1);
        w.WriteStartArray(2);
        w.WriteInt32(CipherSuiteP256);
        w.WriteEncodedValue(EDeviceKeyBytes);       // already tag-24-wrapped; splice verbatim
        w.WriteEndArray();

        w.WriteInt32(2);
        w.WriteStartArray(1);
        w.WriteStartArray(3);
        w.WriteInt32(RetrievalMethodTypeBle);
        w.WriteInt32(BleRetrievalVersion);
        WriteBleOptions(w, Ble);
        w.WriteEndArray();
        w.WriteEndArray();

        w.WriteEndMap();
    });

    /// <summary>Decodes from CBOR. Throws <see cref="CborContentException"/> on a malformed engagement.</summary>
    public static DeviceEngagement Decode(ReadOnlyMemory<byte> cbor)
    {
        string? version = null;
        byte[]? eDeviceKeyBytes = null;
        BleRetrievalOptions? ble = null;

        var r = new CborReader(cbor, CborConformanceMode.Lax);
        r.ReadStartMap();
        while (r.PeekState() != CborReaderState.EndMap)
        {
            switch (r.ReadInt32())
            {
                case 0:
                    version = r.ReadTextString();
                    break;

                case 1:
                    r.ReadStartArray();
                    var suite = r.ReadInt32();
                    if (suite != CipherSuiteP256)
                        throw new CborContentException($"Unsupported cipher suite {suite}; ISO 18013-5 defines only 1.");
                    eDeviceKeyBytes = MdocCbor.ReadRawItem(r);   // keep the tag-24 bytes verbatim
                    r.ReadEndArray();
                    break;

                case 2:
                    ble = ReadRetrievalMethods(r);
                    break;

                default:
                    r.SkipValue();
                    break;
            }
        }
        r.ReadEndMap();

        if (eDeviceKeyBytes is null)
            throw new CborContentException("DeviceEngagement carries no EDeviceKey.");
        if (ble is null)
            throw new CborContentException("DeviceEngagement offers no BLE device-retrieval method.");

        return new DeviceEngagement
        {
            Version = version ?? Version1_0,
            EDeviceKeyBytes = eDeviceKeyBytes,
            Ble = ble
        };
    }

    private static void WriteBleOptions(CborWriter w, BleRetrievalOptions options)
    {
        var count = 2
            + (options.PeripheralServerUuid is not null ? 1 : 0)
            + (options.CentralClientUuid is not null ? 1 : 0);

        w.WriteStartMap(count);

        w.WriteInt32(BleOptionSupportsPeripheralServer);
        w.WriteBoolean(options.SupportsPeripheralServerMode);

        w.WriteInt32(BleOptionSupportsCentralClient);
        w.WriteBoolean(options.SupportsCentralClientMode);

        if (options.PeripheralServerUuid is not null)
        {
            w.WriteInt32(BleOptionPeripheralServerUuid);
            w.WriteByteString(UuidToBigEndian(options.PeripheralServerUuid.Value));
        }

        if (options.CentralClientUuid is not null)
        {
            w.WriteInt32(BleOptionCentralClientUuid);
            w.WriteByteString(UuidToBigEndian(options.CentralClientUuid.Value));
        }

        w.WriteEndMap();
    }

    private static BleRetrievalOptions ReadRetrievalMethods(CborReader r)
    {
        BleRetrievalOptions? ble = null;

        r.ReadStartArray();
        while (r.PeekState() != CborReaderState.EndArray)
        {
            r.ReadStartArray();
            var type = r.ReadInt32();
            r.ReadInt32();  // retrieval-method version

            if (type == RetrievalMethodTypeBle)
                ble = ReadBleOptions(r);
            else
                r.SkipValue();  // a transport we do not speak (NFC, Wi-Fi Aware)

            r.ReadEndArray();
        }
        r.ReadEndArray();

        return ble ?? throw new CborContentException("No BLE retrieval method present.");
    }

    private static BleRetrievalOptions ReadBleOptions(CborReader r)
    {
        bool peripheral = false, central = false;
        Guid? peripheralUuid = null, centralUuid = null;

        r.ReadStartMap();
        while (r.PeekState() != CborReaderState.EndMap)
        {
            switch (r.ReadInt32())
            {
                case BleOptionSupportsPeripheralServer: peripheral = r.ReadBoolean(); break;
                case BleOptionSupportsCentralClient: central = r.ReadBoolean(); break;
                case BleOptionPeripheralServerUuid: peripheralUuid = UuidFromBigEndian(r.ReadByteString()); break;
                case BleOptionCentralClientUuid: centralUuid = UuidFromBigEndian(r.ReadByteString()); break;
                default: r.SkipValue(); break;
            }
        }
        r.ReadEndMap();

        return new BleRetrievalOptions
        {
            SupportsPeripheralServerMode = peripheral,
            SupportsCentralClientMode = central,
            PeripheralServerUuid = peripheralUuid,
            CentralClientUuid = centralUuid
        };
    }

    /// <summary>
    /// Converts a <see cref="Guid"/> to the 16 big-endian bytes a UUID is on the wire.
    /// </summary>
    /// <remarks>
    /// .NET's <see cref="Guid.ToByteArray()"/> emits the first three fields <b>little-endian</b> — a
    /// Microsoft quirk. Handing those bytes straight to BLE produces a UUID that looks right in a debugger
    /// and matches nothing on the air.
    /// </remarks>
    internal static byte[] UuidToBigEndian(Guid uuid)
    {
        var bytes = uuid.ToByteArray();
        Array.Reverse(bytes, 0, 4);
        Array.Reverse(bytes, 4, 2);
        Array.Reverse(bytes, 6, 2);
        return bytes;
    }

    /// <summary>Inverse of <see cref="UuidToBigEndian"/>.</summary>
    internal static Guid UuidFromBigEndian(byte[] bytes)
    {
        if (bytes.Length != 16)
            throw new CborContentException($"A UUID must be 16 bytes but this is {bytes.Length}.");

        var copy = (byte[])bytes.Clone();
        Array.Reverse(copy, 0, 4);
        Array.Reverse(copy, 4, 2);
        Array.Reverse(copy, 6, 2);
        return new Guid(copy);
    }
}
