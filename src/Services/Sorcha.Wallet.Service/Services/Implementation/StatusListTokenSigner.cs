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
    /// <summary>The longest lifetime a status list token may declare (24 hours).</summary>
    public const int MaxTtlSeconds = 86_400;

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
            var jwt = BuildAndSign(
                new StatusListSigningKey(material.IssuerDid, material.Kid, material.PrivateKey, material.Algorithm),
                request, _clock.GetUtcNow());
            return new StatusListToken(jwt, material.IssuerDid, material.Kid);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material.PrivateKey);
        }
    }

    /// <summary>
    /// Builds and signs a status list token with an explicit key. The one place a
    /// <c>statuslist+jwt</c> is assembled in the Wallet Service — the org-key path above and the
    /// citizen-device publisher both come through here. Does NOT wipe <paramref name="key"/>.
    /// </summary>
    public static string BuildAndSign(StatusListSigningKey key, StatusListTokenSignRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var joseAlg = ToJoseAlgorithm(key.Algorithm);
        var iat = now.ToUnixTimeSeconds();

        var header = new Dictionary<string, object>
        {
            ["alg"] = joseAlg,
            ["kid"] = key.Kid,
            ["typ"] = MediaType,
        };
        var payload = new Dictionary<string, object>
        {
            ["iss"] = key.IssuerDid,
            ["sub"] = request.Subject,
            ["iat"] = iat,
            ["exp"] = iat + request.TtlSeconds,
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
        var signature = Sign(signingInput, key.PrivateKey, joseAlg);
        return $"{headerB64}.{payloadB64}.{Base64Url.EncodeToString(signature)}";
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

/// <summary>A key to sign a status list token with, and the identity it signs under.</summary>
/// <param name="IssuerDid">The token's <c>iss</c>.</param>
/// <param name="Kid">The token's <c>kid</c> — must resolve through <paramref name="IssuerDid"/> to this key.</param>
/// <param name="PrivateKey">Raw private key bytes in the wallet's format.</param>
/// <param name="Algorithm">The wallet algorithm name (e.g. <c>ED25519</c>, <c>NISTP256</c>).</param>
public sealed record StatusListSigningKey(string IssuerDid, string Kid, byte[] PrivateKey, string Algorithm);
