// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <inheritdoc />
public sealed class StatusListTokenSigner : IStatusListTokenSigner
{
    /// <summary>The JWT <c>typ</c> the IETF Token Status List requires (§5.1).</summary>
    public const string MediaType = "statuslist+jwt";

    private const int MinTtlSeconds = 60;
    private const int MaxTtlSeconds = 86_400;

    private readonly IIssuanceKeyService _issuanceKeys;
    private readonly TimeProvider _clock;
    private readonly ILogger<StatusListTokenSigner> _logger;

    /// <summary>Initialises a new instance of the <see cref="StatusListTokenSigner"/> class.</summary>
    public StatusListTokenSigner(
        IIssuanceKeyService issuanceKeys, TimeProvider clock, ILogger<StatusListTokenSigner> logger)
    {
        _issuanceKeys = issuanceKeys ?? throw new ArgumentNullException(nameof(issuanceKeys));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<StatusListToken?> SignAsync(StatusListTokenSignRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var material = await _issuanceKeys.GetActiveSigningMaterialAsync(request.OrganizationId, ct);
        if (material is null)
        {
            _logger.LogWarning(
                "Refusing to sign status list {Subject}: organisation {OrgId} has no active VC-issuance key",
                request.Subject, request.OrganizationId);
            return null;
        }

        try
        {
            var joseAlg = ToJoseAlgorithm(material.Algorithm);
            var now = _clock.GetUtcNow().ToUnixTimeSeconds();

            var header = new Dictionary<string, object>
            {
                ["alg"] = joseAlg,
                ["kid"] = material.Kid,
                ["typ"] = MediaType,
            };
            var payload = new Dictionary<string, object>
            {
                ["iss"] = material.IssuerDid,
                ["sub"] = request.Subject,
                ["iat"] = now,
                ["exp"] = now + request.TtlSeconds,
                ["ttl"] = request.TtlSeconds,
                ["status_list"] = new Dictionary<string, object>
                {
                    ["bits"] = request.Bits,
                    ["lst"] = Base64Url.EncodeToString(Deflate(request.Entries)),
                },
            };

            var headerB64 = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header));
            var payloadB64 = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
            var signingInput = Encoding.ASCII.GetBytes($"{headerB64}.{payloadB64}");
            var signature = Sign(signingInput, material.PrivateKey, joseAlg);

            return new StatusListToken(
                $"{headerB64}.{payloadB64}.{Base64Url.EncodeToString(signature)}",
                material.IssuerDid,
                material.Kid);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material.PrivateKey);
        }
    }

    private static void Validate(StatusListTokenSignRequest request)
    {
        if (request.Bits is not (1 or 2 or 4 or 8))
            throw new ArgumentOutOfRangeException(nameof(request), request.Bits, "bits must be 1, 2, 4 or 8.");
        if (request.TtlSeconds is < MinTtlSeconds or > MaxTtlSeconds)
            throw new ArgumentOutOfRangeException(nameof(request), request.TtlSeconds,
                $"ttlSeconds must be between {MinTtlSeconds} and {MaxTtlSeconds}.");
        if (!Uri.TryCreate(request.Subject, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException("subject must be an absolute http(s) URI.", nameof(request));
        if (request.Entries is not { Length: > 0 })
            throw new ArgumentException("entries must not be empty.", nameof(request));
    }

    /// <summary>
    /// Maps a wallet algorithm name to its JOSE name. Wallets record P-256 as <c>NISTP256</c>, so the
    /// JOSE names alone are not enough — a missing alias here refuses every P-256 organisation.
    /// </summary>
    internal static string ToJoseAlgorithm(string walletAlgorithm) => walletAlgorithm.ToUpperInvariant() switch
    {
        "ED25519" or "EDDSA" => "EdDSA",
        "NISTP256" or "NIST-P256" or "P-256" or "P256" or "ES256" or "ECDSA-P256" or "SECP256R1" => "ES256",
        _ => throw new NotSupportedException($"Unsupported status list signing algorithm: {walletAlgorithm}"),
    };

    private static byte[] Sign(byte[] signingInput, byte[] privateKey, string joseAlg)
    {
        if (joseAlg == "EdDSA")
        {
            return Sodium.PublicKeyAuth.SignDetached(signingInput, privateKey);
        }

        // Wallet P-256 keys are the raw 32-byte scalar (CryptoModule); JWS wants r‖s (RFC 7518 §3.4).
        using var ecdsa = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = privateKey });
        return ecdsa.SignData(signingInput, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private static byte[] Deflate(byte[] input)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(input, 0, input.Length);
        }
        return output.ToArray();
    }
}
