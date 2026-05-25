// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Constants;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Default implementation of <see cref="ICitizenStatusListPublisher"/> per
/// IETF Token Status List 2024 (<c>draft-ietf-oauth-status-list-09+</c>),
/// per research §R-003 of the Citizen Wallet PWA design.
/// </summary>
public sealed class CitizenStatusListPublisher : ICitizenStatusListPublisher
{
    private const int DefaultCapacity = 32_768;
    private const string StatusListMediaType = "statuslist+jwt";
    private static readonly TimeSpan ListLifetime = TimeSpan.FromHours(24);

    private readonly WalletDbContext _db;
    private readonly IWalletRepository _walletRepository;
    private readonly IKeyManagementService _keyManagement;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CitizenStatusListPublisher> _logger;

    /// <summary>Initialises a new instance of the <see cref="CitizenStatusListPublisher"/> class.</summary>
    public CitizenStatusListPublisher(
        WalletDbContext db,
        IWalletRepository walletRepository,
        IKeyManagementService keyManagement,
        IConfiguration configuration,
        ILogger<CitizenStatusListPublisher> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _walletRepository = walletRepository ?? throw new ArgumentNullException(nameof(walletRepository));
        _keyManagement = keyManagement ?? throw new ArgumentNullException(nameof(keyManagement));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<(int ListId, int IndexInList)> AllocateIndexAsync(
        Guid organizationId,
        string signingWalletAddress,
        CancellationToken ct = default)
    {
        // Find the org's open (non-full) list with the highest ListId.
        var openList = await _db.CitizenDeviceStatusLists
            .Where(l => l.OrganizationId == organizationId)
            .OrderByDescending(l => l.ListId)
            .FirstOrDefaultAsync(ct);

        var allocated = false;

        if (openList is null || openList.LastAllocatedIndex + 1 >= openList.Capacity)
        {
            var nextListId = openList is null ? 0 : openList.ListId + 1;
            openList = new CitizenDeviceStatusList
            {
                OrganizationId = organizationId,
                ListId = nextListId,
                Capacity = DefaultCapacity,
                Bitstring = new byte[DefaultCapacity / 8],
                RevokedCount = 0,
                LastAllocatedIndex = -1,
                GeneratedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.Add(ListLifetime)
            };
            _db.CitizenDeviceStatusLists.Add(openList);
            allocated = true;

            _logger.LogInformation(
                "Created CitizenDeviceStatusList org={OrgId} listId={ListId} capacity={Capacity}",
                organizationId, nextListId, DefaultCapacity);
        }

        openList.LastAllocatedIndex += 1;
        var indexInList = openList.LastAllocatedIndex;

        await _db.SaveChangesAsync(ct);

        // Sign-on-allocate ensures every list always has a current signed JWT, even
        // before any device is revoked. Ignore the inevitable optimistic-concurrency
        // edge case on first creation — a parallel allocation would just regenerate.
        if (allocated)
        {
            await RegenerateInternalAsync(openList, signingWalletAddress, ct);
        }

        return (openList.ListId, indexInList);
    }

    /// <inheritdoc />
    public async Task FlipAsync(
        Guid organizationId,
        int listId,
        int indexInList,
        string signingWalletAddress,
        CancellationToken ct = default)
    {
        var list = await _db.CitizenDeviceStatusLists
            .FirstOrDefaultAsync(l => l.OrganizationId == organizationId && l.ListId == listId, ct)
            ?? throw new KeyNotFoundException(
                $"Citizen device status list not found: org={organizationId} listId={listId}");

        if (indexInList < 0 || indexInList >= list.Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(indexInList),
                $"Index {indexInList} outside list capacity {list.Capacity}");
        }

        var byteIndex = indexInList / 8;
        var bitOffset = indexInList % 8;
        var mask = (byte)(1 << bitOffset);

        if ((list.Bitstring[byteIndex] & mask) != 0)
        {
            // Idempotent — bit already set, nothing to do but keep the JWT fresh.
            _logger.LogDebug(
                "FlipAsync no-op: org={OrgId} listId={ListId} idx={Index} already revoked",
                organizationId, listId, indexInList);
        }
        else
        {
            list.Bitstring[byteIndex] = (byte)(list.Bitstring[byteIndex] | mask);
            list.RevokedCount += 1;

            _logger.LogInformation(
                "Flipped citizen device revoked bit: org={OrgId} listId={ListId} idx={Index} revokedCount={Count}",
                organizationId, listId, indexInList, list.RevokedCount);
        }

        await RegenerateInternalAsync(list, signingWalletAddress, ct);
    }

    /// <inheritdoc />
    public async Task<string?> GetSignedListAsync(
        Guid organizationId,
        int listId,
        CancellationToken ct = default)
    {
        var list = await _db.CitizenDeviceStatusLists
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.OrganizationId == organizationId && l.ListId == listId, ct);

        return list?.SignedJwt;
    }

    /// <inheritdoc />
    public async Task RegenerateAsync(
        Guid organizationId,
        int listId,
        string signingWalletAddress,
        CancellationToken ct = default)
    {
        var list = await _db.CitizenDeviceStatusLists
            .FirstOrDefaultAsync(l => l.OrganizationId == organizationId && l.ListId == listId, ct)
            ?? throw new KeyNotFoundException(
                $"Citizen device status list not found: org={organizationId} listId={listId}");

        await RegenerateInternalAsync(list, signingWalletAddress, ct);
    }

    /// <inheritdoc />
    public string BuildStatusListUri(Guid organizationId, int listId)
    {
        var baseUrl = _configuration["CitizenWallet:PublicBaseUrl"]?.TrimEnd('/')
                      ?? "https://localhost";
        return $"{baseUrl}/api/v1/wallet/status/{organizationId}/citizen-devices/{listId}.statuslist+jwt";
    }

    private async Task RegenerateInternalAsync(
        CitizenDeviceStatusList list,
        string signingWalletAddress,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        list.GeneratedAt = now;
        list.ExpiresAt = now.Add(ListLifetime);

        var compressed = ZlibDeflate(list.Bitstring);
        var issuer = $"did:sorcha:org:{list.OrganizationId:N}";
        var payload = new
        {
            iss = issuer,
            iat = now.ToUnixTimeSeconds(),
            exp = list.ExpiresAt.ToUnixTimeSeconds(),
            sub = BuildStatusListUri(list.OrganizationId, list.ListId),
            status_list = new
            {
                bits = 1,
                lst = Base64Url.EncodeToString(compressed)
            }
        };

        // Feature 138 US1 — identify the signing verification method so the verifier's
        // IIssuerKeyResolver can match the correct key (it falls back to the first VM matching
        // the alg when the kid does not resolve, preserving back-compat with already-issued lists).
        var kid = $"{issuer}#citizen-status-signing";
        list.SignedJwt = await SignJwtAsync(payload, signingWalletAddress, kid, ct);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Regenerated signed status list: org={OrgId} listId={ListId} revoked={Revoked}/{Capacity} bytes={JwtBytes}",
            list.OrganizationId, list.ListId, list.RevokedCount, list.Capacity, list.SignedJwt.Length);
    }

    private async Task<string> SignJwtAsync(object payload, string signingWalletAddress, string kid, CancellationToken ct)
    {
        var wallet = await _walletRepository.GetByAddressAsync(signingWalletAddress, false, false, false, ct)
            ?? throw new KeyNotFoundException($"Signing wallet {signingWalletAddress} not found");

        var masterKey = await _keyManagement.DecryptPrivateKeyAsync(
            wallet.EncryptedPrivateKey, wallet.EncryptionKeyId);

        var resolvedPath = SorchaDerivationPaths.ResolvePath(SorchaDerivationPaths.CitizenStatusSigning);
        var parsedPath = new DerivationPath(resolvedPath);

        var derivationAlg = WalletAlgorithmClassification.ClassicalAlgorithms.Contains(wallet.Algorithm)
            ? wallet.Algorithm
            : WalletAlgorithmClassification.DefaultClassicalAlgorithm;

        var (privateKey, _) = await _keyManagement.DeriveKeyAtPathAsync(masterKey, parsedPath, derivationAlg);

        try
        {
            var alg = derivationAlg.ToUpperInvariant();
            var joseAlg = alg switch
            {
                "ED25519" or "EDDSA" => "EdDSA",
                "ES256" or "P-256" or "P256" or "NIST-P256" or "NISTP256" or "ECDSA-P256" => "ES256",
                _ => throw new NotSupportedException($"Unsupported status-list signing algorithm: {derivationAlg}")
            };

            var header = new { alg = joseAlg, kid, typ = StatusListMediaType };
            var headerJson = JsonSerializer.SerializeToUtf8Bytes(header);
            var payloadJson = JsonSerializer.SerializeToUtf8Bytes(payload);

            var headerB64 = Base64Url.EncodeToString(headerJson);
            var payloadB64 = Base64Url.EncodeToString(payloadJson);
            var signingInput = Encoding.ASCII.GetBytes($"{headerB64}.{payloadB64}");

            byte[] signature;
            if (joseAlg == "EdDSA")
            {
                signature = Sodium.PublicKeyAuth.SignDetached(signingInput, privateKey);
            }
            else
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportECPrivateKey(privateKey, out _);
                signature = ecdsa.SignData(signingInput, HashAlgorithmName.SHA256);
            }

            var signatureB64 = Base64Url.EncodeToString(signature);
            return $"{headerB64}.{payloadB64}.{signatureB64}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    private static byte[] ZlibDeflate(byte[] input)
    {
        // Token Status List 2024 mandates zlib-wrapped deflate (RFC 1950),
        // not raw deflate. ZLibStream handles this correctly when used with
        // System.IO.Compression.ZLibStream (.NET 6+).
        using var output = new MemoryStream();
        using (var compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(input, 0, input.Length);
        }
        return output.ToArray();
    }
}
