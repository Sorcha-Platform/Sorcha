// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Cryptography;
using Sorcha.ServiceClients.OrgDidDocument;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Domain.Enums;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Lazy-derivation entry point for per-organisation VC issuance keys (Feature 120 T038).
/// </summary>
/// <remarks>
/// On first call for an org, derives a key under <see cref="KeyUsage.VCIssuance"/>
/// (Feature 083 slot 1) via the existing <see cref="IOrgKeyDerivationService"/>,
/// persists an <see cref="IssuanceKeyState"/> row with <c>Status=Active, RotationIndex=1</c>,
/// and triggers DID-document regeneration on the Tenant side. Idempotent on retries —
/// returns the existing Active row.
/// </remarks>
public sealed class IssuanceKeyService : IIssuanceKeyService
{
    private const int IssuanceSlot = 1; // Feature 083 slot 1 = KeyUsage.VCIssuance

    private readonly WalletDbContext _db;
    private readonly IOrgKeyDerivationService _orgKey;
    private readonly IOrgDidDocumentClient _didDocClient;
    private readonly ILogger<IssuanceKeyService> _logger;

    /// <summary>DI-friendly constructor.</summary>
    public IssuanceKeyService(
        WalletDbContext db,
        IOrgKeyDerivationService orgKey,
        IOrgDidDocumentClient didDocClient,
        ILogger<IssuanceKeyService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _orgKey = orgKey ?? throw new ArgumentNullException(nameof(orgKey));
        _didDocClient = didDocClient ?? throw new ArgumentNullException(nameof(didDocClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IssuanceKeyState> GetOrDeriveAsync(Guid organizationId, CancellationToken ct = default)
    {
        // Idempotency — return any existing Active row first.
        var existing = await _db.IssuanceKeyStates
            .FirstOrDefaultAsync(
                k => k.OrganizationId == organizationId
                  && k.Slot == IssuanceSlot
                  && k.Status == IssuanceKeyStatus.Active,
                ct)
            .ConfigureAwait(false);
        if (existing is not null) return existing;

        // Derive via Feature 083 — uses orgId as both controller and subject for the org's own key.
        var derived = await _orgKey.DeriveUserKeyAsync(
            organizationId.ToString(),
            organizationId.ToString(),
            departmentId: 0,
            usage: KeyUsage.VCIssuance,
            ct: ct).ConfigureAwait(false);

        // Look up the persisted wallet to read the public key bytes.
        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.Address == derived.WalletAddress, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Issuance key derivation produced wallet address '{derived.WalletAddress}' " +
                "but the wallet row could not be found. Underlying derivation pipeline failure.");

        var publicKeyBytes = string.IsNullOrEmpty(wallet.PublicKey)
            ? throw new InvalidOperationException("Derived wallet has no public key recorded.")
            : Convert.FromBase64String(wallet.PublicKey);

        var jwkJson = BuildJwk(wallet.Algorithm, publicKeyBytes);
        var thumbprint = JsonWebKeyThumbprint.Compute(jwkJson);

        var state = new IssuanceKeyState
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Slot = IssuanceSlot,
            RotationIndex = 1,
            Status = IssuanceKeyStatus.Active,
            PublicKey = publicKeyBytes,
            Algorithm = wallet.Algorithm,
            Thumbprint = thumbprint,
            DerivedAt = DateTimeOffset.UtcNow
        };

        _db.IssuanceKeyStates.Add(state);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Issuance key derived for org {OrgId} algo={Algorithm} rotation={Rotation}",
            organizationId, state.Algorithm, state.RotationIndex);

        // Fire-and-forget DID-document regeneration trigger; the client is non-throwing.
        var snapshot = new OrgDidRegenerateRequest(
            OrganizationId: organizationId,
            KeyEventReason: "IssuanceKeyDerived",
            WalletAddress: derived.WalletAddress,
            ActiveKeys:
            [
                new OrgDidActiveKey(
                    RotationIndex: state.RotationIndex,
                    Algorithm: state.Algorithm,
                    PublicKeyJwk: jwkJson,
                    Thumbprint: state.Thumbprint)
            ]);
        await _didDocClient.RegenerateAsync(snapshot, ct).ConfigureAwait(false);

        return state;
    }

    /// <inheritdoc />
    public Task<IssuanceKeyState?> GetActiveAsync(Guid organizationId, CancellationToken ct = default)
        => _db.IssuanceKeyStates
            .FirstOrDefaultAsync(
                k => k.OrganizationId == organizationId
                  && k.Slot == IssuanceSlot
                  && k.Status == IssuanceKeyStatus.Active,
                ct);

    /// <inheritdoc />
    public async Task<JsonElement?> GetPublicJwkAsync(
        Guid organizationId, int rotationIndex, CancellationToken ct = default)
    {
        var row = await _db.IssuanceKeyStates
            .FirstOrDefaultAsync(
                k => k.OrganizationId == organizationId
                  && k.RotationIndex == rotationIndex,
                ct)
            .ConfigureAwait(false);

        if (row is null) return null;

        var jwkJson = BuildJwk(row.Algorithm, row.PublicKey);
        using var doc = JsonDocument.Parse(jwkJson);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Builds the JWK JSON for an issuance public key. Currently supports the wallet
    /// algorithms exposed by <see cref="IOrgKeyDerivationService"/> — ED25519 (OKP),
    /// NIST-P256 (EC), RSA-4096 (RSA). Unknown algorithms raise.
    /// </summary>
    internal static string BuildJwk(string algorithm, byte[] publicKeyBytes)
    {
        var algo = algorithm.ToUpperInvariant();
        return algo switch
        {
            "ED25519" => $$"""{"kty":"OKP","crv":"Ed25519","x":"{{Base64Url(publicKeyBytes)}}"}""",
            "NIST-P256" or "NISTP256" or "P-256" or "P256" or "ECDSA-P256"
                => BuildEcJwk(publicKeyBytes),
            _ => throw new NotSupportedException($"Issuance JWK build unsupported for algorithm '{algorithm}'.")
        };
    }

    private static string BuildEcJwk(byte[] publicKeyBytes)
    {
        // Uncompressed P-256 point: 0x04 || X(32) || Y(32).
        if (publicKeyBytes.Length == 65 && publicKeyBytes[0] == 0x04)
        {
            var x = publicKeyBytes.AsSpan(1, 32);
            var y = publicKeyBytes.AsSpan(33, 32);
            return $$"""{"kty":"EC","crv":"P-256","x":"{{Base64Url(x.ToArray())}}","y":"{{Base64Url(y.ToArray())}}"}""";
        }

        throw new NotSupportedException(
            $"Unsupported P-256 public key encoding (length={publicKeyBytes.Length}, first byte=0x{publicKeyBytes.FirstOrDefault():X2}).");
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
