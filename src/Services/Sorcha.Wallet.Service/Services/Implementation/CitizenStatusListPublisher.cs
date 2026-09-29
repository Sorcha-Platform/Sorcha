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
using Sorcha.Wallet.Contracts.Constants;
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
    private static readonly TimeSpan ListLifetime = TimeSpan.FromHours(24);

    private readonly WalletDbContext _db;
    private readonly IWalletRepository _walletRepository;
    private readonly IKeyManagementService _keyManagement;
    private readonly IIssuanceKeyService _issuanceKeys;
    private readonly TimeProvider _clock;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CitizenStatusListPublisher> _logger;

    /// <summary>Initialises a new instance of the <see cref="CitizenStatusListPublisher"/> class.</summary>
    public CitizenStatusListPublisher(
        WalletDbContext db,
        IWalletRepository walletRepository,
        IKeyManagementService keyManagement,
        IIssuanceKeyService issuanceKeys,
        TimeProvider clock,
        IConfiguration configuration,
        ILogger<CitizenStatusListPublisher> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _walletRepository = walletRepository ?? throw new ArgumentNullException(nameof(walletRepository));
        _keyManagement = keyManagement ?? throw new ArgumentNullException(nameof(keyManagement));
        _issuanceKeys = issuanceKeys ?? throw new ArgumentNullException(nameof(issuanceKeys));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<(int ListId, int IndexInList)> AllocateIndexAsync(
        Guid organizationId,
        string signingWalletAddress,
        CancellationToken ct = default)
    {
        // #1759 — a list is signed under ONE DID for its whole life, because every credential pointing
        // at it pins its status to that signer. So the list an index comes from must be signed by the
        // signer this org would use today: the org's issuer DID when it has a VC-issuance key, else a
        // did:key of its status key. When that has changed (the org gained a key), a new list is opened
        // and the old one keeps its signer.
        var signerDid = await ResolveCurrentSignerDidAsync(organizationId, signingWalletAddress, ct);

        var openList = await _db.CitizenDeviceStatusLists
            .Where(l => l.OrganizationId == organizationId)
            .OrderByDescending(l => l.ListId)
            .FirstOrDefaultAsync(ct);

        var allocated = false;

        // A list from before #1759 has no recorded signer; it adopts the current one.
        if (openList is not null && openList.SignerDid is null)
        {
            openList.SignerDid = signerDid;
        }

        if (openList is null
            || openList.LastAllocatedIndex + 1 >= openList.Capacity
            || !string.Equals(openList.SignerDid, signerDid, StringComparison.Ordinal))
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
                GeneratedAt = _clock.GetUtcNow(),
                ExpiresAt = _clock.GetUtcNow().Add(ListLifetime),
                SignerDid = signerDid
            };
            _db.CitizenDeviceStatusLists.Add(openList);
            allocated = true;

            _logger.LogInformation(
                "Created CitizenDeviceStatusList org={OrgId} listId={ListId} capacity={Capacity} signer={Signer}",
                organizationId, nextListId, DefaultCapacity, signerDid);
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

        // #1499: LSB-first per draft-ietf-oauth-status-list §4.1 ("packed into bytes from the least
        // significant bit (0) to the most significant bit (7)") — entry N's bit is byte N/8, bit N%8,
        // counting from bit 0 (least significant). Verified against the spec's own worked example,
        // which zlib-decompresses to the raw bytes 0xb9 0xa3 for its 16-entry bits=1 vector; that only
        // reconstructs under LSB-first, not MSB-first. This is the OPPOSITE convention to W3C
        // Bitstring Status List (MSB-first — see BitstringStatusList.GetBit/SetBit) — do not "fix"
        // this to match that spec, they genuinely disagree. See StatusListCache's remarks for the
        // read-side mirror of this note, and StatusListCacheIetfConformanceTests for the pinned vector.
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
    public async Task<string?> GetSignerDidAsync(Guid organizationId, int listId, CancellationToken ct = default)
    {
        var list = await _db.CitizenDeviceStatusLists
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.OrganizationId == organizationId && l.ListId == listId, ct);

        return list?.SignerDid;
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
        list.SignerDid ??= await ResolveCurrentSignerDidAsync(list.OrganizationId, signingWalletAddress, ct);

        var key = await ResolveSigningKeyAsync(list, signingWalletAddress, ct);
        try
        {
            var now = _clock.GetUtcNow();
            // Accurately 1: this rail only ever models a single revoked-or-not bit per device (no
            // suspension state — see #1498). A future width MUST be written using the same LSB-first,
            // bits-wide packing FlipAsync documents above.
            var jwt = StatusListTokenSigner.BuildAndSign(
                key,
                new StatusListTokenSignRequest(
                    list.OrganizationId,
                    BuildStatusListUri(list.OrganizationId, list.ListId),
                    Bits: 1,
                    list.Bitstring,
                    (int)ListLifetime.TotalSeconds),
                now);

            list.GeneratedAt = now;
            list.ExpiresAt = now.Add(ListLifetime);
            list.SignedJwt = jwt;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key.PrivateKey);
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Regenerated signed status list: org={OrgId} listId={ListId} signer={Signer} revoked={Revoked}/{Capacity} bytes={JwtBytes}",
            list.OrganizationId, list.ListId, list.SignerDid, list.RevokedCount, list.Capacity, list.SignedJwt.Length);
    }

    /// <summary>
    /// The signer this org would use for a NEW list today: its issuer DID when it has an active
    /// VC-issuance key, otherwise a did:key of its <c>sorcha:citizen-status-signing</c> key.
    /// </summary>
    private async Task<string> ResolveCurrentSignerDidAsync(
        Guid organizationId, string signingWalletAddress, CancellationToken ct)
    {
        var material = await _issuanceKeys.GetActiveSigningMaterialAsync(organizationId, ct);
        if (material is not null)
        {
            CryptographicOperations.ZeroMemory(material.PrivateKey);
            return material.IssuerDid;
        }

        var (privateKey, publicKey, algorithm) = await DeriveStatusKeyAsync(signingWalletAddress, ct);
        CryptographicOperations.ZeroMemory(privateKey);
        return StatusSignerDidKey.FromPublicKey(algorithm, publicKey).Did;
    }

    /// <summary>
    /// The key for the signer RECORDED on the list. Refuses rather than sign under anything else: every
    /// credential pointing at this list pins its status to that signer, so a list signed by a different
    /// key would be rejected by every verifier — or, worse, trusted by a careless one.
    /// </summary>
    private async Task<StatusListSigningKey> ResolveSigningKeyAsync(
        CitizenDeviceStatusList list, string signingWalletAddress, CancellationToken ct)
    {
        var signerDid = list.SignerDid!;

        if (signerDid.StartsWith("did:key:", StringComparison.Ordinal))
        {
            var (privateKey, publicKey, algorithm) = await DeriveStatusKeyAsync(signingWalletAddress, ct);
            var (did, kid) = StatusSignerDidKey.FromPublicKey(algorithm, publicKey);
            if (!string.Equals(did, signerDid, StringComparison.Ordinal))
            {
                CryptographicOperations.ZeroMemory(privateKey);
                throw new InvalidOperationException(
                    $"Citizen status list org={list.OrganizationId} listId={list.ListId} is signed by {signerDid}, "
                    + $"but the status key now derives {did}; refusing to re-sign it under a different key.");
            }
            return new StatusListSigningKey(did, kid, privateKey, algorithm);
        }

        var material = await _issuanceKeys.GetActiveSigningMaterialAsync(list.OrganizationId, ct);
        if (material is null || !string.Equals(material.IssuerDid, signerDid, StringComparison.Ordinal))
        {
            if (material is not null) CryptographicOperations.ZeroMemory(material.PrivateKey);
            throw new InvalidOperationException(
                $"Citizen status list org={list.OrganizationId} listId={list.ListId} is signed by {signerDid}, "
                + "and that organisation has no active VC-issuance key under it; refusing to re-sign.");
        }
        return new StatusListSigningKey(material.IssuerDid, material.Kid, material.PrivateKey, material.Algorithm);
    }

    private async Task<(byte[] PrivateKey, byte[] PublicKey, string Algorithm)> DeriveStatusKeyAsync(
        string signingWalletAddress, CancellationToken ct)
    {
        var wallet = await _walletRepository.GetByAddressAsync(signingWalletAddress, false, false, false, ct)
            ?? throw new KeyNotFoundException($"Signing wallet {signingWalletAddress} not found");

        var masterKey = await _keyManagement.DecryptPrivateKeyAsync(
            wallet.EncryptedPrivateKey, wallet.EncryptionKeyId);

        var parsedPath = new DerivationPath(
            SorchaDerivationPaths.ResolvePath(SorchaDerivationPaths.CitizenStatusSigning));
        var derivationAlg = WalletAlgorithmClassification.ClassicalAlgorithms.Contains(wallet.Algorithm)
            ? wallet.Algorithm
            : WalletAlgorithmClassification.DefaultClassicalAlgorithm;

        var (privateKey, publicKey) = await _keyManagement.DeriveKeyAtPathAsync(masterKey, parsedPath, derivationAlg);
        return (privateKey, publicKey, derivationAlg);
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
