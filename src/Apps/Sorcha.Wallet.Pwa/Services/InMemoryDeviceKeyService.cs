// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Demo-grade <see cref="IDeviceKeyService"/>. Generates and caches an ECDSA P-256
/// keypair in-memory for the lifetime of the WASM runtime. <strong>Not production</strong>:
/// the production impl wraps a non-extractable WebCrypto <c>CryptoKey</c> (T055).
/// </summary>
public sealed class InMemoryDeviceKeyService : IDeviceKeyService, IDisposable
{
    private readonly object _lock = new();
    private ECDsa? _ecdsa;
    private string? _publicJwkJson;
    private string? _thumbprint;

    /// <inheritdoc />
    public Task<JsonElement> GetPublicJwkAsync(CancellationToken ct = default)
    {
        EnsureKey();
        return Task.FromResult(JsonSerializer.Deserialize<JsonElement>(_publicJwkJson!));
    }

    /// <inheritdoc />
    public Task<string> GetThumbprintAsync(CancellationToken ct = default)
    {
        EnsureKey();
        return Task.FromResult(_thumbprint!);
    }

    /// <inheritdoc />
    public Task<byte[]> SignAsync(byte[] data, CancellationToken ct = default)
    {
        EnsureKey();
        return Task.FromResult(_ecdsa!.SignData(data, HashAlgorithmName.SHA256));
    }

    private void EnsureKey()
    {
        if (_ecdsa is not null) return;
        lock (_lock)
        {
            if (_ecdsa is not null) return;
            // CA1416: ECDsa.Create(ECCurve) is flagged unsupported on 'browser', but recent
            // .NET 10 mono-wasm builds DO ship ECDsa via crypto.subtle interop. Either way,
            // T055 replaces this with the explicit webcrypto-bridge.js path so this call site
            // disappears in production. Suppressed here because the MVP demo runs against
            // a runtime where the call works.
#pragma warning disable CA1416
            _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
#pragma warning restore CA1416
            var p = _ecdsa.ExportParameters(false);
            var x = Base64Url.EncodeToString(p.Q.X!);
            var y = Base64Url.EncodeToString(p.Q.Y!);
            _publicJwkJson = JsonSerializer.Serialize(new { kty = "EC", crv = "P-256", x, y });
            var canonical = $"{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{x}\",\"y\":\"{y}\"}}";
            _thumbprint = Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        }
    }

    /// <inheritdoc />
    public void Dispose() => _ecdsa?.Dispose();
}
