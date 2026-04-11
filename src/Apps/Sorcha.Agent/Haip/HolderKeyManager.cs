// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sorcha.Agent.Haip;

/// <summary>
/// Manages the holder's P-256 key pair for HAIP credential binding.
/// Generates on first use, persists as PEM (private) and JWK (public),
/// and reuses on subsequent invocations.
/// </summary>
public class HolderKeyManager
{
    private readonly string _keyDir;
    private ECDsa? _ecdsa;

    public HolderKeyManager(string walletDir)
    {
        _keyDir = walletDir;
        Directory.CreateDirectory(_keyDir);
    }

    /// <summary>
    /// Loads the existing key pair or generates a new one.
    /// </summary>
    public ECDsa GetOrCreateKey()
    {
        if (_ecdsa != null) return _ecdsa;

        var pemPath = Path.Combine(_keyDir, "holder-key.pem");

        if (File.Exists(pemPath))
        {
            _ecdsa = ECDsa.Create();
            _ecdsa.ImportFromPem(File.ReadAllText(pemPath));
        }
        else
        {
            _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var pem = _ecdsa.ExportECPrivateKeyPem();
            File.WriteAllText(pemPath, pem);

            // Also write the public JWK for reference
            var jwk = ExportPublicJwk(_ecdsa);
            File.WriteAllText(Path.Combine(_keyDir, "holder-key.jwk.json"),
                JsonSerializer.Serialize(jwk, new JsonSerializerOptions { WriteIndented = true }));
        }

        return _ecdsa;
    }

    /// <summary>
    /// Exports the holder's public key as a JWK JsonElement.
    /// </summary>
    public JsonElement GetPublicJwk()
    {
        var key = GetOrCreateKey();
        var jwk = ExportPublicJwk(key);
        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(jwk));
    }

    private static Dictionary<string, string> ExportPublicJwk(ECDsa key)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        return new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Base64Url.EncodeToString(parameters.Q.X!),
            ["y"] = Base64Url.EncodeToString(parameters.Q.Y!)
        };
    }
}
