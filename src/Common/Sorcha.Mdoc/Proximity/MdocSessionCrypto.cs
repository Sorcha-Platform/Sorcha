// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
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
    public static async Task<MdocSessionKeys> DeriveKeysAsync(
        byte[] sessionTranscriptBytes,
        ECPrivateKeyParameters ownEphemeralPrivate,
        ECPublicKeyParameters peerEphemeralPublic,
        StaticDeviceKeyMaterial? staticDeviceKey,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sessionTranscriptBytes);
        ArgumentNullException.ThrowIfNull(ownEphemeralPrivate);
        ArgumentNullException.ThrowIfNull(peerEphemeralPublic);

        var salt = SHA256.HashData(sessionTranscriptBytes);

        // The EPHEMERAL keys are ours, generated per session, and always in memory — there is nothing to
        // protect in a key that exists for one exchange and is then discarded. Only the STATIC device key is
        // hardware-held, and that is why only it goes through the agreement seam.
        var sharedSecret = Agree(ownEphemeralPrivate, peerEphemeralPublic);
        try
        {
            var skDevice = Hkdf(sharedSecret, salt, InfoSkDevice);
            var skReader = Hkdf(sharedSecret, salt, InfoSkReader);

            byte[] eMacKey = [];
            if (staticDeviceKey is not null)
            {
                var macSecret = await staticDeviceKey.AgreeAsync(ct).ConfigureAwait(false);
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

        // BouncyCastle, not System.Security.Cryptography.AesGcm — see the class remarks. GcmBlockCipher
        // appends the tag to the ciphertext, which is exactly the `ct ‖ tag` layout ISO 18013-5 wants.
        var cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(forEncryption: true, new AeadParameters(new KeyParameter(key), TagLength * 8, nonce));

        var output = new byte[cipher.GetOutputSize(plaintext.Length)];
        var written = cipher.ProcessBytes(plaintext, 0, plaintext.Length, output, 0);
        cipher.DoFinal(output, written);

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

        try
        {
            var cipher = new GcmBlockCipher(new AesEngine());
            cipher.Init(forEncryption: false, new AeadParameters(new KeyParameter(key), TagLength * 8, nonce));

            var output = new byte[cipher.GetOutputSize(ciphertextWithTag.Length)];
            var written = cipher.ProcessBytes(ciphertextWithTag, 0, ciphertextWithTag.Length, output, 0);
            cipher.DoFinal(output, written);

            return output;
        }
        catch (InvalidCipherTextException)
        {
            // Tag mismatch: the message was tampered with, replayed under the wrong counter, or is not ours.
            // Returning null (rather than throwing) is what lets every receive path fail closed without
            // exception handling.
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
/// The half of the <c>EMacKey</c> ECDH that this party can perform.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is an agreement <em>seam</em>, not a key — and that distinction is load-bearing.</b>
/// </para>
/// <para>
/// On the citizen's phone the mdoc's static device key is a <b>non-extractable WebCrypto key</b>. C# can
/// never hold its private half — that is the entire point of it. So the ECDH itself has to happen inside
/// WebCrypto (<c>deriveBits</c> returns exactly the raw shared secret the standard calls <c>Z</c>), and we
/// take the result. Modelling this as an <c>ECPrivateKeyParameters</c> would have compiled fine and then
/// been impossible to wire to the one key that actually matters.
/// </para>
/// <para>
/// It is the same shape as the device-<em>signer</em> delegate in <see cref="Cose.CoseSign1Builder"/>, and
/// for the same reason: a hardware-protected key is reachable only as an operation, never as bytes.
/// </para>
/// <para>
/// The in-process implementations below are for the reader, for tests, and for the server — all places where
/// the key really is in memory.
/// </para>
/// </remarks>
public abstract class StaticDeviceKeyMaterial
{
    /// <summary>
    /// Performs this party's half of the <c>EMacKey</c> agreement and returns the raw shared secret
    /// (the P-256 x-coordinate, 32 bytes).
    /// </summary>
    /// <remarks>Async because on a phone this crosses the JS-interop boundary into WebCrypto.</remarks>
    public abstract Task<byte[]> AgreeAsync(CancellationToken ct = default);

    /// <summary>
    /// The <b>holder's</b> half, when the static device private key is genuinely in memory (tests, server).
    /// </summary>
    /// <remarks>
    /// On a real device use the WebCrypto-backed implementation instead — a phone's device key cannot be
    /// loaded into an <see cref="ECPrivateKeyParameters"/>, and if it could, it would not be worth having.
    /// </remarks>
    public static StaticDeviceKeyMaterial ForHolder(
        ECPrivateKeyParameters staticDevicePrivate, ECPublicKeyParameters readerEphemeralPublic)
        => new InProcessMaterial(staticDevicePrivate, readerEphemeralPublic);

    /// <summary>
    /// The <b>reader's</b> half: its own ephemeral private key, agreed with the mdoc's static device public
    /// key (read from the MSO).
    /// </summary>
    /// <remarks>
    /// The reader's key is ephemeral and per-session, so it is always in memory — the reader never needs the
    /// WebCrypto seam.
    /// </remarks>
    public static StaticDeviceKeyMaterial ForReader(
        ECPrivateKeyParameters readerEphemeralPrivate, ECPublicKeyParameters staticDevicePublic)
        => new InProcessMaterial(readerEphemeralPrivate, staticDevicePublic);

    private sealed class InProcessMaterial(ECPrivateKeyParameters priv, ECPublicKeyParameters pub)
        : StaticDeviceKeyMaterial
    {
        public override Task<byte[]> AgreeAsync(CancellationToken ct = default)
            => Task.FromResult(AgreeCore(priv, pub));
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
