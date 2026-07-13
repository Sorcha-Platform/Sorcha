// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Sorcha.Mdoc.Cose;

namespace Sorcha.Mdoc.Proximity;

/// <summary>The three session keys of an ISO 18013-5 proximity exchange. Session-scoped secrets.</summary>
/// <remarks>
/// Held in memory for the life of one exchange and <b>zeroised on dispose</b>. Never persisted, never
/// logged, never sent anywhere.
/// </remarks>
public sealed class MdocSessionKeys : IDisposable
{
    private bool _disposed;

    internal MdocSessionKeys(byte[] skDevice, byte[] skReader, byte[] eMacKey)
    {
        SkDevice = skDevice;
        SkReader = skReader;
        EMacKey = eMacKey;
    }

    /// <summary>Encrypts messages sent <b>by the holder</b> (mdoc → reader).</summary>
    public byte[] SkDevice { get; }

    /// <summary>Encrypts messages sent <b>by the reader</b> (reader → mdoc).</summary>
    public byte[] SkReader { get; }

    /// <summary>MACs the holder's <c>DeviceAuthentication</c> (the <c>deviceMac</c> device-auth form).</summary>
    /// <remarks>
    /// <see langword="null"/>-length when the exchange has no static device key to agree with — i.e. when
    /// the holder will use <c>deviceSignature</c> instead.
    /// </remarks>
    public byte[] EMacKey { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(SkDevice);
        CryptographicOperations.ZeroMemory(SkReader);
        CryptographicOperations.ZeroMemory(EMacKey);
        _disposed = true;
    }
}

/// <summary>
/// ISO 18013-5 proximity session key agreement and message encryption.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pure BouncyCastle, deliberately.</b> This assembly runs inside a Blazor WASM host, where the BCL's EC
/// and AES-GCM implementations are not dependable. BouncyCastle is pure-managed, so it works identically on
/// the desktop test host and in the browser. Do not "simplify" this to
/// <c>System.Security.Cryptography.ECDiffieHellman</c> / <c>AesGcm</c> — it will compile and then fail on a
/// phone.
/// </para>
/// <para>
/// The derivation below is pinned by ISO 18013-5:2021 Annex D reference data — see
/// <c>IsoAnnexDVectorTests</c>, which reproduces the standard's own <c>SessionEstablishment</c> and
/// <c>SessionData</c> <em>ciphertexts</em> from the raw Annex D keys. That is a real check: a wrong salt,
/// info string, ECDH pairing or nonce cannot reproduce someone else's ciphertext.
/// </para>
/// <para>
/// ⚠ <b>Do not "fix" this against the freely-downloadable DIS draft of 18013-5.</b> The draft specifies
/// <em>different</em> crypto — empty HKDF info, salts of <c>0x00</c>/<c>0x01</c>, and a 2-element
/// SessionTranscript. It is superseded, and following it would break every real wallet.
/// </para>
/// </remarks>
public static class MdocSessionCrypto
{
    /// <summary>Length of every derived session key.</summary>
    public const int KeyLength = 32;

    /// <summary>Length of the AES-GCM nonce.</summary>
    public const int NonceLength = 12;

    /// <summary>Length of the AES-GCM authentication tag.</summary>
    public const int TagLength = 16;

    // HKDF info strings (ISO 18013-5:2021 §9.1.1.4). Verified against Annex D reference data.
    private static readonly byte[] InfoSkDevice = "SKDevice"u8.ToArray();
    private static readonly byte[] InfoSkReader = "SKReader"u8.ToArray();
    private static readonly byte[] InfoEMacKey = "EMacKey"u8.ToArray();

    /// <summary>Generates a fresh ephemeral P-256 key pair for one session.</summary>
    /// <remarks>Never reused across sessions — reuse would let two exchanges be linked.</remarks>
    public static AsymmetricCipherKeyPair GenerateEphemeralKeyPair()
    {
        var generator = new ECKeyPairGenerator();
        generator.Init(new ECKeyGenerationParameters(CoseKey.DomainParameters, new SecureRandom()));
        return generator.GenerateKeyPair();
    }

    /// <summary>
    /// Derives the session keys for one exchange.
    /// </summary>
    /// <param name="sessionTranscriptBytes">
    /// <b>The tag-24-wrapped <c>SessionTranscriptBytes</c></b>, i.e. <c>#6.24(bstr .cbor SessionTranscript)</c>
    /// — <b>not</b> the bare <c>SessionTranscript</c> array. Its SHA-256 is the HKDF salt, which is what binds
    /// every derived key to <em>this</em> engagement, and therefore what makes a captured response useless in
    /// any other session.
    /// <para>
    /// ⚠ The two forms are easy to confuse and the standard uses <b>both</b>: the salt hashes the
    /// <b>tag-24-wrapped</b> form, while <c>DeviceAuthentication</c> splices in the <b>bare array</b>. Getting
    /// this the wrong way round derives plausible-looking keys that decrypt nothing, with no diagnostic.
    /// <see cref="ProximitySessionTranscript"/> hands you both, correctly labelled, so you never have to choose.
    /// </para>
    /// </param>
    /// <param name="ownEphemeralPrivate">This party's ephemeral private key.</param>
    /// <param name="peerEphemeralPublic">The peer's ephemeral public key.</param>
    /// <param name="staticDeviceKey">
    /// For <c>EMacKey</c>: the ECDH pair involving the mdoc's <b>static</b> device key (the one published in
    /// the MSO) and the reader's <b>ephemeral</b> key.
    /// <list type="bullet">
    ///   <item><description>On the <b>holder</b>, pass the device's static <b>private</b> key — it is agreed with the reader's ephemeral public key.</description></item>
    ///   <item><description>On the <b>reader</b>, pass the device's static <b>public</b> key (read from the MSO) — it is agreed with the reader's own ephemeral private key.</description></item>
    ///   <item><description>Pass <see langword="null"/> when the holder will use <c>deviceSignature</c>; <c>EMacKey</c> then comes back empty.</description></item>
    /// </list>
    /// This asymmetry is the whole reason the mdoc device key must be <b>ECDH-capable</b> — and why the
    /// wallet needs a second device key, since a WebCrypto ECDSA key cannot do ECDH (feature 185, design §3).
    /// </param>
    public static MdocSessionKeys DeriveKeys(
        byte[] sessionTranscriptBytes,
        ECPrivateKeyParameters ownEphemeralPrivate,
        ECPublicKeyParameters peerEphemeralPublic,
        StaticDeviceKeyMaterial? staticDeviceKey)
    {
        ArgumentNullException.ThrowIfNull(sessionTranscriptBytes);
        ArgumentNullException.ThrowIfNull(ownEphemeralPrivate);
        ArgumentNullException.ThrowIfNull(peerEphemeralPublic);

        var salt = SHA256.HashData(sessionTranscriptBytes);

        var sharedSecret = Agree(ownEphemeralPrivate, peerEphemeralPublic);
        try
        {
            var skDevice = Hkdf(sharedSecret, salt, InfoSkDevice);
            var skReader = Hkdf(sharedSecret, salt, InfoSkReader);

            byte[] eMacKey = [];
            if (staticDeviceKey is not null)
            {
                var macSecret = staticDeviceKey.Agree();
                try
                {
                    eMacKey = Hkdf(macSecret, salt, InfoEMacKey);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(macSecret);
                }
            }

            return new MdocSessionKeys(skDevice, skReader, eMacKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
        }
    }

    /// <summary>Encrypts one message. <paramref name="messageCounter"/> starts at 1 and increments per message per direction.</summary>
    /// <remarks>
    /// The counter is what stops nonce reuse under a fixed key — reusing an AES-GCM nonce is catastrophic,
    /// not merely weak, so the session engines own the counter and never let a caller supply one.
    /// </remarks>
    public static byte[] Encrypt(byte[] key, byte[] plaintext, uint messageCounter, MdocSessionRole senderRole)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(plaintext);

        var nonce = BuildNonce(messageCounter, senderRole);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];

        using var aes = new AesGcm(key, TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var output = new byte[ciphertext.Length + TagLength];
        Buffer.BlockCopy(ciphertext, 0, output, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, output, ciphertext.Length, TagLength);
        return output;
    }

    /// <summary>
    /// Decrypts one message. Returns <see langword="null"/> — never throws — when authentication fails, so
    /// callers on the receive path fail closed without exception handling.
    /// </summary>
    public static byte[]? Decrypt(byte[] key, byte[] ciphertextWithTag, uint messageCounter, MdocSessionRole senderRole)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(ciphertextWithTag);

        if (ciphertextWithTag.Length < TagLength)
            return null;

        var nonce = BuildNonce(messageCounter, senderRole);
        var cipherLength = ciphertextWithTag.Length - TagLength;
        var ciphertext = ciphertextWithTag.AsSpan(0, cipherLength);
        var tag = ciphertextWithTag.AsSpan(cipherLength, TagLength);
        var plaintext = new byte[cipherLength];

        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        catch (CryptographicException)
        {
            // Tag mismatch: the message was tampered with, replayed under the wrong counter, or is not ours.
            return null;
        }
    }

    /// <summary>
    /// Builds the 12-byte AES-GCM nonce: an 8-byte direction identifier followed by a big-endian 4-byte
    /// message counter.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>UNVERIFIED</b> against the standard text — the direction-identifier values in particular. Both
    /// of our own endpoints agree on them, so the loopback tests pass regardless; that is exactly why this
    /// cannot be treated as evidence of interoperability (T013).
    /// </remarks>
    internal static byte[] BuildNonce(uint messageCounter, MdocSessionRole senderRole)
    {
        var nonce = new byte[NonceLength];
        // Bytes 0..7: direction identifier. Reader-sent = all zero; holder-sent = ...01.
        if (senderRole == MdocSessionRole.Holder)
            nonce[7] = 0x01;
        // Bytes 8..11: big-endian message counter.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8, 4), messageCounter);
        return nonce;
    }

    private static byte[] Agree(ECPrivateKeyParameters ownPrivate, ECPublicKeyParameters peerPublic)
    {
        var agreement = new ECDHBasicAgreement();
        agreement.Init(ownPrivate);
        var z = agreement.CalculateAgreement(peerPublic);

        // Fixed-width field element — BouncyCastle strips leading zeros, and a short Z would silently
        // change the derived keys.
        var bytes = z.ToByteArrayUnsigned();
        if (bytes.Length == CoseKey.P256CoordinateLength)
            return bytes;

        var padded = new byte[CoseKey.P256CoordinateLength];
        Buffer.BlockCopy(bytes, 0, padded, CoseKey.P256CoordinateLength - bytes.Length, bytes.Length);
        return padded;
    }

    private static byte[] Hkdf(byte[] inputKeyMaterial, byte[] salt, byte[] info)
    {
        var output = new byte[KeyLength];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, inputKeyMaterial, output, salt, info);
        return output;
    }
}

/// <summary>Which party sent a message — this selects the encryption key and the nonce's direction bytes.</summary>
public enum MdocSessionRole
{
    /// <summary>The mdoc holder (the citizen's phone).</summary>
    Holder,

    /// <summary>The mdoc reader (the verifier's device).</summary>
    Reader
}

/// <summary>
/// The half of the <c>EMacKey</c> ECDH each party holds. The holder has the static device <b>private</b> key;
/// the reader has the static device <b>public</b> key (from the MSO) and its own ephemeral private key.
/// </summary>
public abstract class StaticDeviceKeyMaterial
{
    /// <summary>Performs this party's half of the EMacKey agreement.</summary>
    public abstract byte[] Agree();

    /// <summary>The holder's half: the mdoc's static device private key, agreed with the reader's ephemeral public key.</summary>
    public static StaticDeviceKeyMaterial ForHolder(
        ECPrivateKeyParameters staticDevicePrivate, ECPublicKeyParameters readerEphemeralPublic)
        => new HolderMaterial(staticDevicePrivate, readerEphemeralPublic);

    /// <summary>The reader's half: its own ephemeral private key, agreed with the mdoc's static device public key.</summary>
    public static StaticDeviceKeyMaterial ForReader(
        ECPrivateKeyParameters readerEphemeralPrivate, ECPublicKeyParameters staticDevicePublic)
        => new ReaderMaterial(readerEphemeralPrivate, staticDevicePublic);

    private sealed class HolderMaterial(ECPrivateKeyParameters priv, ECPublicKeyParameters pub) : StaticDeviceKeyMaterial
    {
        public override byte[] Agree() => AgreeCore(priv, pub);
    }

    private sealed class ReaderMaterial(ECPrivateKeyParameters priv, ECPublicKeyParameters pub) : StaticDeviceKeyMaterial
    {
        public override byte[] Agree() => AgreeCore(priv, pub);
    }

    private static byte[] AgreeCore(ECPrivateKeyParameters priv, ECPublicKeyParameters pub)
    {
        var agreement = new ECDHBasicAgreement();
        agreement.Init(priv);
        var z = agreement.CalculateAgreement(pub);

        var bytes = z.ToByteArrayUnsigned();
        if (bytes.Length == CoseKey.P256CoordinateLength)
            return bytes;

        var padded = new byte[CoseKey.P256CoordinateLength];
        Buffer.BlockCopy(bytes, 0, padded, CoseKey.P256CoordinateLength - bytes.Length, bytes.Length);
        return padded;
    }
}
