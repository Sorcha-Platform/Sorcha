// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.CitizenWallet.Abstractions.Constants;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Default <see cref="IDeviceDelegationIssuer"/>. Pure composition — defers all
/// crypto material to <see cref="IHolderKeyService"/> and all status-list state
/// to <see cref="ICitizenStatusListPublisher"/>, so the only thing this class
/// owns is the SD-JWT VC payload shape.
/// </summary>
public sealed class DeviceDelegationIssuer : IDeviceDelegationIssuer
{
    private static readonly TimeSpan DelegationLifetime = TimeSpan.FromDays(365);

    private readonly IHolderKeyService _holderKeys;
    private readonly ICitizenStatusListPublisher _statusList;
    private readonly ILogger<DeviceDelegationIssuer> _logger;

    /// <summary>Initialises a new instance of the <see cref="DeviceDelegationIssuer"/> class.</summary>
    public DeviceDelegationIssuer(
        IHolderKeyService holderKeys,
        ICitizenStatusListPublisher statusList,
        ILogger<DeviceDelegationIssuer> logger)
    {
        _holderKeys = holderKeys ?? throw new ArgumentNullException(nameof(holderKeys));
        _statusList = statusList ?? throw new ArgumentNullException(nameof(statusList));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<DeviceDelegationResult> IssueAsync(
        Guid platformUserId,
        string citizenWalletAddress,
        Guid organizationId,
        string orgStatusSigningWalletAddress,
        EcP256PublicJwk devicePublicJwk,
        string deviceLabel,
        string platform,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(devicePublicJwk);
        ArgumentException.ThrowIfNullOrWhiteSpace(citizenWalletAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(orgStatusSigningWalletAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceLabel);

        // Allocate the status-list slot first so the credential can reference it.
        // Allocation is durable — if signing fails afterwards, the bit is wasted but
        // never wrongly interpreted as revoked (default 0 = active), which is the
        // correct failure mode for a status list.
        var (listId, statusListIndex) = await _statusList.AllocateIndexAsync(
            organizationId, orgStatusSigningWalletAddress, ct);

        var statusListUri = _statusList.BuildStatusListUri(organizationId, listId);

        var holderJwk = await _holderKeys.GetHolderPublicJwkAsync(citizenWalletAddress, ct);
        var holderThumbprint = await _holderKeys.GetHolderJwkThumbprintAsync(citizenWalletAddress, ct);
        var deviceThumbprint = ComputeEcThumbprint(devicePublicJwk);

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(DelegationLifetime);
        var jti = Guid.NewGuid().ToString("N");

        var payload = new Dictionary<string, object?>
        {
            ["iss"] = $"did:sorcha:holder:{holderThumbprint}",
            ["sub"] = $"did:sorcha:device:{deviceThumbprint}",
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = expiresAt.ToUnixTimeSeconds(),
            ["jti"] = jti,
            ["vct"] = VctUris.CitizenDeviceDelegationV1,
            ["delegated_capabilities"] = new[] { DelegatedCapabilities.PresentationHolderKeyBinding },
            ["device"] = new
            {
                label = deviceLabel,
                platform,
                enrolled_at = now.ToUnixTimeSeconds()
            },
            ["cnf"] = new { jwk = devicePublicJwk },
            ["status"] = new
            {
                status_list = new
                {
                    uri = statusListUri,
                    idx = statusListIndex
                }
            }
        };

        // Sign first so the header advertises the holder key's *real* JOSE algorithm. The holder
        // key derives from the underlying wallet algorithm — Ed25519 for the default Sorcha wallet,
        // EC P-256 for a P-256 wallet — so a hardcoded "ES256" header over an Ed25519 signature is
        // unverifiable by any conformant verifier. Two-pass: build header with a placeholder alg to
        // get the signing input, but the alg is known from SignAsync's return, so we sign over the
        // honest header in one pass below.
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(payload);
        var payloadB64 = Base64Url.EncodeToString(payloadJson);

        // We need the JOSE alg before computing the signing input, but the holder algorithm is a
        // property of the key, not the signature — resolve it via the public JWK's key type so the
        // signed header is correct on the first and only pass.
        var joseAlg = ResolveJoseAlg(holderJwk);
        var header = new
        {
            alg = joseAlg,
            typ = "dc+sd-jwt",
            kid = $"did:sorcha:holder:{holderThumbprint}#0"
        };
        var headerJson = JsonSerializer.SerializeToUtf8Bytes(header);
        var headerB64 = Base64Url.EncodeToString(headerJson);
        var signingInput = Encoding.ASCII.GetBytes($"{headerB64}.{payloadB64}");

        var (signature, signingAlg) = await _holderKeys.SignAsync(citizenWalletAddress, signingInput, ct);

        // Defence in depth: the header alg derived from the JWK must agree with the algorithm the
        // signer actually used. A mismatch means the holder key material and JWK have diverged —
        // fail closed rather than emit a credential whose header lies about its own signature.
        var signerJoseAlg = ToJoseAlg(signingAlg);
        if (!string.Equals(joseAlg, signerJoseAlg, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Holder JWK advertises '{joseAlg}' but the signer used '{signerJoseAlg}' " +
                $"(wallet {citizenWalletAddress}); refusing to issue a delegation with a mismatched alg header.");
        }

        var compactJwt = $"{headerB64}.{payloadB64}.{Base64Url.EncodeToString(signature)}";

        _logger.LogInformation(
            "Issued device delegation credential platformUser={PlatformUserId} device={DeviceThumbprint} " +
            "holder={HolderThumbprint} statusList={Org}/{ListId}#{Index} jti={Jti} exp={ExpiresAt:O}",
            platformUserId, deviceThumbprint, holderThumbprint,
            organizationId, listId, statusListIndex, jti, expiresAt);

        return new DeviceDelegationResult(
            compactJwt,
            jti,
            expiresAt,
            statusListUri,
            statusListIndex,
            listId,
            holderJwk);
    }

    /// <summary>
    /// Resolves the JOSE <c>alg</c> for a delegation header from the holder public JWK's key type:
    /// <c>OKP</c>/<c>Ed25519</c> → <c>EdDSA</c>, <c>EC</c>/<c>P-256</c> → <c>ES256</c>. The header alg
    /// must match the curve the holder key signs with so the verifier picks the right primitive.
    /// </summary>
    private static string ResolveJoseAlg(JsonElement holderJwk)
    {
        var kty = holderJwk.TryGetProperty("kty", out var k) ? k.GetString() : null;
        var crv = holderJwk.TryGetProperty("crv", out var c) ? c.GetString() : null;
        return (kty, crv) switch
        {
            ("OKP", "Ed25519") => "EdDSA",
            ("EC", "P-256") => "ES256",
            _ => throw new NotSupportedException(
                $"Unsupported holder key type for delegation issuance: kty='{kty}', crv='{crv}'. " +
                "Only Ed25519 (OKP) and P-256 (EC) holder keys are supported.")
        };
    }

    /// <summary>Maps an <see cref="IHolderKeyService"/> algorithm string to its JOSE <c>alg</c> name.</summary>
    private static string ToJoseAlg(string algorithm) => algorithm.ToUpperInvariant() switch
    {
        "ED25519" or "EDDSA" => "EdDSA",
        "ES256" or "P-256" or "P256" or "NIST-P256" or "NISTP256" or "ECDSA-P256" => "ES256",
        _ => throw new NotSupportedException(
            $"Unsupported holder signing algorithm for delegation issuance: '{algorithm}'.")
    };

    /// <summary>
    /// RFC 7638 thumbprint over an EC P-256 JWK. Required members in lex order:
    /// <c>crv</c>, <c>kty</c>, <c>x</c>, <c>y</c>; no whitespace; SHA-256; base64url.
    /// Mirrors the EC branch of <see cref="HolderKeyService"/>.
    /// </summary>
    private static string ComputeEcThumbprint(EcP256PublicJwk jwk)
    {
        var canonical = $"{{\"crv\":\"{jwk.Crv}\",\"kty\":\"{jwk.Kty}\",\"x\":\"{jwk.X}\",\"y\":\"{jwk.Y}\"}}";
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Base64Url.EncodeToString(hash);
    }
}
