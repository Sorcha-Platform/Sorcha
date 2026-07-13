// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Formats.Cbor;
using System.Security.Cryptography;
using FluentAssertions;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Sorcha.Mdoc.Cbor;
using Sorcha.Mdoc.Cose;
using Sorcha.Mdoc.Proximity;
using Sorcha.Mdoc.Tests.Fixtures;
using Xunit;

namespace Sorcha.Mdoc.Tests.Proximity;

/// <summary>
/// Validates the proximity session layer against ISO/IEC 18013-5:2021 <b>Annex D</b> reference data.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are the tests that actually mean something.</b> Our holder and our reader agreeing with each
/// other proves nothing — two implementations can share a mistake and round-trip perfectly. These tests
/// instead reproduce <em>the standard's own ciphertexts and MAC tag</em> from the standard's own raw keys.
/// You cannot fake that: a wrong HKDF salt, a wrong info string, a wrong ECDH pairing, a wrong nonce, or a
/// wrong session transcript each break it.
/// </para>
/// <para>Provenance and its limits are documented on <see cref="IsoAnnexDVectors"/>. Read that first.</para>
/// </remarks>
public class IsoAnnexDVectorTests
{
    private static ECPrivateKeyParameters PrivateKey(string dHex) =>
        new(new BigInteger(1, Convert.FromHexString(dHex)), CoseKey.DomainParameters);

    private static ECPublicKeyParameters PublicKey(string xHex, string yHex)
    {
        var point = CoseKey.DomainParameters.Curve.CreatePoint(
            new BigInteger(1, Convert.FromHexString(xHex)),
            new BigInteger(1, Convert.FromHexString(yHex)));
        return new ECPublicKeyParameters(point, CoseKey.DomainParameters);
    }

    private static ProximitySessionTranscript AnnexDTranscript() =>
        ProximitySessionTranscript.FromTaggedBytes(
            IsoAnnexDVectors.Hex(IsoAnnexDVectors.SessionTranscriptBytesHex));

    private static MdocSessionKeys DeriveAnnexDKeys() => MdocSessionCrypto.DeriveKeys(
        AnnexDTranscript().TaggedBytes,
        // From the reader's side: its ephemeral private key against the holder's ephemeral public key.
        PrivateKey(IsoAnnexDVectors.EphemeralReaderKeyDHex),
        PublicKey(IsoAnnexDVectors.EphemeralDeviceKeyXHex, IsoAnnexDVectors.EphemeralDeviceKeyYHex),
        // EMacKey: the reader's ephemeral private key against the mdoc's STATIC device key from the MSO.
        StaticDeviceKeyMaterial.ForReader(
            PrivateKey(IsoAnnexDVectors.EphemeralReaderKeyDHex),
            PublicKey(IsoAnnexDVectors.StaticDeviceKeyXHex, IsoAnnexDVectors.StaticDeviceKeyYHex)));

    /// <summary>
    /// The holder and the reader must derive identical session keys from opposite halves of the ECDH. If
    /// this fails, nothing else can work.
    /// </summary>
    [Fact]
    public void DeriveKeys_FromEitherSide_AgreesOnTheSameSessionKeys()
    {
        var transcript = AnnexDTranscript();

        using var readerView = DeriveAnnexDKeys();

        using var holderView = MdocSessionCrypto.DeriveKeys(
            transcript.TaggedBytes,
            PrivateKey(IsoAnnexDVectors.EphemeralDeviceKeyDHex),
            PublicKey(IsoAnnexDVectors.EphemeralReaderKeyXHex, IsoAnnexDVectors.EphemeralReaderKeyYHex),
            StaticDeviceKeyMaterial.ForHolder(
                PrivateKey(IsoAnnexDVectors.StaticDeviceKeyDHex),
                PublicKey(IsoAnnexDVectors.EphemeralReaderKeyXHex, IsoAnnexDVectors.EphemeralReaderKeyYHex)));

        holderView.SkDevice.Should().Equal(readerView.SkDevice);
        holderView.SkReader.Should().Equal(readerView.SkReader);
        holderView.EMacKey.Should().Equal(readerView.EMacKey, because:
            "EMacKey pairs the STATIC MSO device key with the reader's EPHEMERAL key — the asymmetry that " +
            "forces the wallet to hold a second, ECDH-capable device key");
    }

    /// <summary>
    /// Decrypts the standard's own <c>SessionEstablishment</c> ciphertext and expects the standard's own
    /// <c>DeviceRequest</c> plaintext back.
    /// </summary>
    /// <remarks>
    /// This single assertion pins the HKDF salt (SHA-256 of the <b>tag-24-wrapped</b> transcript), the
    /// <c>"SKReader"</c> info string, the ephemeral-to-ephemeral ECDH pairing, the AES-256-GCM nonce layout,
    /// the reader's direction identifier, and a message counter starting at 1. Any one of them wrong and the
    /// GCM tag does not authenticate.
    /// </remarks>
    [Fact]
    public void SessionEstablishment_FromTheStandard_DecryptsToTheStandardsDeviceRequest()
    {
        using var keys = DeriveAnnexDKeys();

        var establishment = SessionEstablishment.Decode(
            IsoAnnexDVectors.Hex(IsoAnnexDVectors.SessionEstablishmentHex));

        var plaintext = MdocSessionCrypto.Decrypt(
            keys.SkReader, establishment.Data, messageCounter: 1, MdocSessionRole.Reader);

        plaintext.Should().NotBeNull(because:
            "the reader's first message decrypts under SKReader with counter 1 — a null here means the salt, " +
            "info string, ECDH pairing or nonce is wrong");
        plaintext.Should().Equal(IsoAnnexDVectors.Hex(IsoAnnexDVectors.DeviceRequestHex));
    }

    /// <summary>
    /// Decrypts the standard's own <c>SessionData</c> ciphertext and expects the standard's own
    /// <c>DeviceResponse</c> plaintext back — the holder's direction, under <c>SKDevice</c>.
    /// </summary>
    [Fact]
    public void SessionData_FromTheStandard_DecryptsToTheStandardsDeviceResponse()
    {
        using var keys = DeriveAnnexDKeys();

        var sessionData = SessionData.Decode(IsoAnnexDVectors.Hex(IsoAnnexDVectors.SessionDataHex));
        sessionData.Data.Should().NotBeNull();

        var plaintext = MdocSessionCrypto.Decrypt(
            keys.SkDevice, sessionData.Data!, messageCounter: 1, MdocSessionRole.Holder);

        plaintext.Should().NotBeNull(because:
            "the holder's direction identifier differs from the reader's — if this decrypts but the previous " +
            "test does not (or vice versa), the direction bytes are swapped");
        plaintext.Should().Equal(IsoAnnexDVectors.Hex(IsoAnnexDVectors.DeviceResponseHex));
    }

    /// <summary>
    /// Re-encrypts the standard's plaintext and expects the standard's ciphertext back, byte for byte.
    /// </summary>
    /// <remarks>
    /// Decryption alone could pass with a nonce that happened to be self-consistent. Reproducing someone
    /// else's ciphertext exactly cannot.
    /// </remarks>
    [Fact]
    public void Encrypt_ReproducesTheStandardsCiphertext_ByteForByte()
    {
        using var keys = DeriveAnnexDKeys();

        var reEncrypted = MdocSessionCrypto.Encrypt(
            keys.SkReader,
            IsoAnnexDVectors.Hex(IsoAnnexDVectors.DeviceRequestHex),
            messageCounter: 1,
            MdocSessionRole.Reader);

        var expected = SessionEstablishment
            .Decode(IsoAnnexDVectors.Hex(IsoAnnexDVectors.SessionEstablishmentHex)).Data;

        reEncrypted.Should().Equal(expected);
    }

    /// <summary>
    /// Verifies the <c>deviceMac</c> carried in the standard's own <c>DeviceResponse</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the test that settles the question the BCL's missing <c>COSE_Mac0</c> left open, and it is the
    /// one implementations most often get wrong: whether <c>DeviceAuthenticationBytes</c> occupies the
    /// <c>payload</c> slot of the <c>MAC_structure</c> (standard RFC 9052 detached-payload handling) or the
    /// <c>external_aad</c> slot. Prose disagrees; the standard's own MAC tag does not. We check against the tag.
    /// </para>
    /// <para>
    /// It simultaneously proves the <c>EMacKey</c> derivation, the <c>DeviceAuthentication</c> construction,
    /// and that the bare (not tag-24-wrapped) transcript is what gets spliced into it.
    /// </para>
    /// </remarks>
    [Fact]
    public void DeviceMac_FromTheStandardsDeviceResponse_Verifies()
    {
        using var keys = DeriveAnnexDKeys();
        var transcript = AnnexDTranscript();

        var response = MdocCodec.DecodeDeviceResponse(
            IsoAnnexDVectors.Hex(IsoAnnexDVectors.DeviceResponseHex));

        response.Documents.Should().NotBeEmpty();
        var doc = response.Documents[0];

        doc.DeviceSigned.DeviceAuth.DeviceMacRaw.Should().NotBeNull(because:
            "Annex D's worked example authenticates the device with a MAC, not a signature — which is exactly " +
            "the path feature 135 refused and this feature implements");

        var deviceAuthentication = MdocCodec.BuildDeviceAuthentication(
            transcript.Encoded, doc.DocType, doc.DeviceSigned.NameSpacesBytes);

        CoseMac0.VerifyDetached(doc.DeviceSigned.DeviceAuth.DeviceMacRaw!, deviceAuthentication, keys.EMacKey)
            .Should().BeTrue(because:
                "the standard's own deviceMac must verify under our COSE_Mac0 and our EMacKey");
    }

    /// <summary>A tampered MAC tag must be rejected — the verifier fails closed.</summary>
    [Fact]
    public void DeviceMac_Tampered_IsRejected()
    {
        using var keys = DeriveAnnexDKeys();
        var transcript = AnnexDTranscript();

        var response = MdocCodec.DecodeDeviceResponse(
            IsoAnnexDVectors.Hex(IsoAnnexDVectors.DeviceResponseHex));
        var doc = response.Documents[0];

        var deviceAuthentication = MdocCodec.BuildDeviceAuthentication(
            transcript.Encoded, doc.DocType, doc.DeviceSigned.NameSpacesBytes);

        var tampered = (byte[])doc.DeviceSigned.DeviceAuth.DeviceMacRaw!.Clone();
        tampered[^1] ^= 0x01;   // flip one bit of the tag

        CoseMac0.VerifyDetached(tampered, deviceAuthentication, keys.EMacKey).Should().BeFalse();
    }

    /// <summary>
    /// The session transcript's two forms must not be confused: the tag-24 form is what the salt hashes.
    /// </summary>
    [Fact]
    public void SessionTranscript_TaggedAndBareForms_AreDistinctAndRoundTrip()
    {
        var transcript = AnnexDTranscript();

        transcript.TaggedBytes.Should().NotEqual(transcript.Encoded, because:
            "SessionTranscriptBytes is tag-24-wrapped; SessionTranscript is the bare array. The salt hashes " +
            "the former, DeviceAuthentication splices the latter — conflating them derives keys that decrypt " +
            "nothing, silently");

        MdocCbor.UnwrapTag24(transcript.TaggedBytes).Should().Equal(transcript.Encoded);

        // Structural: a 3-element array (the Handover element is a :2021 addition — the DIS draft has two).
        var reader = new CborReader(transcript.Encoded, CborConformanceMode.Lax);
        reader.ReadStartArray().Should().Be(3);
    }

    /// <summary>
    /// The QR-engagement transcript we actually ship has no published vector (Annex D's example is NFC), so
    /// it is validated structurally. This test exists to make that gap explicit rather than silent.
    /// </summary>
    [Fact]
    public void QrEngagementTranscript_HasNoPublishedVector_SoIsCheckedStructurally()
    {
        var deviceEngagementTagged = MdocCbor.WrapTag24(
            IsoAnnexDVectors.Hex(IsoAnnexDVectors.DeviceEngagementHex));
        var eReaderKeyTagged = MdocCbor.WrapTag24(
            CoseKey.EncodeEc2PublicKey(PublicKey(
                IsoAnnexDVectors.EphemeralReaderKeyXHex, IsoAnnexDVectors.EphemeralReaderKeyYHex)));

        var transcript = ProximitySessionTranscript.ForQrEngagement(deviceEngagementTagged, eReaderKeyTagged);

        var reader = new CborReader(transcript.Encoded, CborConformanceMode.Lax);
        reader.ReadStartArray().Should().Be(3);
        reader.ReadEncodedValue().ToArray().Should().Equal(deviceEngagementTagged);
        reader.ReadEncodedValue().ToArray().Should().Equal(eReaderKeyTagged);
        reader.PeekState().Should().Be(CborReaderState.Null, because:
            "QR engagement carries no handover — the third element is null");
    }

    /// <summary>The AES-GCM nonce layout: 8-byte direction identifier, then a big-endian 4-byte counter.</summary>
    [Fact]
    public void Nonce_DirectionIdentifierAndCounter_AreLaidOutCorrectly()
    {
        MdocSessionCrypto.BuildNonce(1, MdocSessionRole.Reader).Should()
            .Equal(Convert.FromHexString("000000000000000000000001"));

        MdocSessionCrypto.BuildNonce(1, MdocSessionRole.Holder).Should()
            .Equal(Convert.FromHexString("000000000000000100000001"), because:
                "the holder's direction identifier is ...01 — the one byte that stops the two directions " +
                "reusing a nonce under the same key, which in GCM is catastrophic rather than merely weak");

        MdocSessionCrypto.BuildNonce(2, MdocSessionRole.Holder).Should()
            .Equal(Convert.FromHexString("000000000000000100000002"));
    }
}
