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

        var header = new
        {
            alg = "ES256",
            typ = "vc+sd-jwt",
            kid = $"did:sorcha:holder:{holderThumbprint}#0"
        };

        var headerJson = JsonSerializer.SerializeToUtf8Bytes(header);
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(payload);
        var headerB64 = Base64Url.EncodeToString(headerJson);
        var payloadB64 = Base64Url.EncodeToString(payloadJson);
        var signingInput = Encoding.ASCII.GetBytes($"{headerB64}.{payloadB64}");

        var (signature, _) = await _holderKeys.SignAsync(citizenWalletAddress, signingInput, ct);
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
