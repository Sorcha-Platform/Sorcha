// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.JSInterop;
using Org.BouncyCastle.Crypto.Parameters;
using Sorcha.Mdoc.Cose;
using Sorcha.Proximity;

namespace Sorcha.Wallet.Pwa.Services.Proximity;

/// <summary>
/// The citizen's <b>second</b> device key: a non-extractable <b>ECDH</b> P-256 key, used only by the mdoc
/// proximity rail.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a second key exists at all.</b> ISO 18013-5 derives <c>EMacKey</c> by ECDH between the mdoc's
/// <b>static</b> device key (the one published in the MSO) and the reader's <b>ephemeral</b> key — so the
/// MSO device key must be ECDH-capable. In WebCrypto a key's usages are <b>fixed at generation</b>, and a key
/// <b>cannot be both ECDSA and ECDH</b>. The wallet's existing device key
/// (<see cref="IDeviceKeyService"/>) is <c>sign</c>-only, so it structurally cannot produce a
/// <c>deviceMac</c> — no amount of plumbing changes that.
/// </para>
/// <para>
/// So: the <b>SD-JWT rail</b> keeps the existing ECDSA key for KB-JWT signing, untouched; the <b>mdoc rail</b>
/// gets this one. Two keys, each fit for its purpose. A credential bound to this key can produce a
/// <c>deviceMac</c> but not a <c>deviceSignature</c>, which is correct and sufficient: ISO requires exactly
/// one of the two, and a conformant reader must accept either.
/// </para>
/// <para>
/// <b>The key never leaves WebCrypto.</b> That is why <see cref="AgreeAsync"/> exists rather than a
/// "get private key" method: the ECDH happens <em>inside</em> the browser's key store, and .NET only ever
/// sees the resulting shared secret. Anything else would defeat the point of a non-extractable key.
/// </para>
/// </remarks>
public interface IMdocDeviceKeyService
{
    /// <summary>The public key, as an EC2 <c>COSE_Key</c> — what an issuer binds <c>MSO.DeviceKey</c> to.</summary>
    Task<byte[]> GetPublicCoseKeyAsync(CancellationToken ct = default);

    /// <summary>The public key as a JWK.</summary>
    Task<JsonElement> GetPublicJwkAsync(CancellationToken ct = default);

    /// <summary>
    /// Performs the ECDH agreement against the reader's ephemeral public key, inside WebCrypto, and returns
    /// the raw shared secret.
    /// </summary>
    Task<byte[]> AgreeAsync(ECPublicKeyParameters peerPublicKey, CancellationToken ct = default);

    /// <summary>Adapts this key to the seam the proximity holder session expects.</summary>
    HolderDeviceKey AsHolderDeviceKey();
}

/// <inheritdoc />
public sealed class WebCryptoMdocDeviceKeyService : IMdocDeviceKeyService
{
    /// <summary>
    /// Deliberately distinct from the ECDSA key's id (<c>sorcha-wallet-device</c>).
    /// </summary>
    /// <remarks>
    /// Sharing an id would collide in the bridge's key map and hand back a key with the wrong usages — which
    /// would fail at the ECDH with an opaque WebCrypto error rather than anywhere useful.
    /// </remarks>
    private const string KeyId = "sorcha-wallet-mdoc-device";

    private readonly IJSRuntime _js;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _generated;

    public WebCryptoMdocDeviceKeyService(IJSRuntime js)
    {
        _js = js ?? throw new ArgumentNullException(nameof(js));
    }

    /// <inheritdoc />
    public async Task<byte[]> GetPublicCoseKeyAsync(CancellationToken ct = default)
    {
        var jwk = await GetPublicJwkAsync(ct).ConfigureAwait(false);

        var x = Base64Url(jwk.GetProperty("x").GetString()!);
        var y = Base64Url(jwk.GetProperty("y").GetString()!);

        var point = CoseKey.DomainParameters.Curve.CreatePoint(
            new Org.BouncyCastle.Math.BigInteger(1, x),
            new Org.BouncyCastle.Math.BigInteger(1, y));

        return CoseKey.EncodeEc2PublicKey(new ECPublicKeyParameters(point, CoseKey.DomainParameters));
    }

    /// <inheritdoc />
    public async Task<JsonElement> GetPublicJwkAsync(CancellationToken ct = default)
    {
        await EnsureGeneratedAsync(ct).ConfigureAwait(false);

        var json = await _js.InvokeAsync<string>("SorchaWebCrypto.getPublicJwk", ct, KeyId);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    /// <inheritdoc />
    public async Task<byte[]> AgreeAsync(ECPublicKeyParameters peerPublicKey, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(peerPublicKey);

        await EnsureGeneratedAsync(ct).ConfigureAwait(false);

        var q = peerPublicKey.Q.Normalize();
        var peerJwk = JsonSerializer.Serialize(new
        {
            kty = "EC",
            crv = "P-256",
            x = ToBase64Url(q.AffineXCoord.GetEncoded()),
            y = ToBase64Url(q.AffineYCoord.GetEncoded())
        });

        var secretBase64Url = await _js.InvokeAsync<string>(
            "SorchaWebCrypto.deriveSharedSecret", ct, KeyId, peerJwk);

        return Base64Url(secretBase64Url);
    }

    /// <inheritdoc />
    public HolderDeviceKey AsHolderDeviceKey() => new(AgreeAsync);

    private async Task EnsureGeneratedAsync(CancellationToken ct)
    {
        if (_generated)
            return;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_generated)
                return;

            await _js.InvokeAsync<string>("SorchaWebCrypto.generateEcdhP256", ct, KeyId);
            _generated = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static byte[] Base64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
        return Convert.FromBase64String(s);
    }

    private static string ToBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
