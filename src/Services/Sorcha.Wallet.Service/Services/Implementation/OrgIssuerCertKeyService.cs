// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Contracts.Constants;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Feature 181 US4 — resolves + signs with an org's P-256 certificate-issuing key (research R9/R10).
/// See <see cref="IOrgIssuerCertKeyService"/> for the contract and the distinction from
/// <see cref="HaipIssuerCoKeyService"/>.
/// </summary>
public sealed class OrgIssuerCertKeyService : IOrgIssuerCertKeyService
{
    // The X.509 rail is P-256-only. A wallet whose PRIMARY algorithm is one of these binds its primary
    // key directly; anything else (Ed25519, PQC, RSA) derives the ES256 co-key. ED25519 is deliberately
    // absent — it is "classical" for HAIP but is not a P-256 key and cannot back an EUDI X.509 cert.
    private static readonly HashSet<string> P256PrimaryAlgorithms =
        new(StringComparer.OrdinalIgnoreCase) { "ES256", "P-256", "P256", "NIST-P256", "NISTP256", "ECDSA-P256" };

    private readonly IWalletRepository _repository;
    private readonly IKeyManagementService _keyManagement;
    private readonly ILogger<OrgIssuerCertKeyService> _logger;

    public OrgIssuerCertKeyService(
        IWalletRepository repository,
        IKeyManagementService keyManagement,
        ILogger<OrgIssuerCertKeyService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _keyManagement = keyManagement ?? throw new ArgumentNullException(nameof(keyManagement));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<OrgIssuerCertKeyResolution> ResolveAsync(string walletAddress, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);

        var resolved = await TryResolveAsync(walletAddress, needPrivate: false, ct);
        if (resolved is null)
        {
            return new OrgIssuerCertKeyResolution(false, "CERT_KEY_NOT_ELIGIBLE", null, null);
        }

        try
        {
            var spki = ExportSpki(resolved.Value.PublicPoint);
            return new OrgIssuerCertKeyResolution(true, null, resolved.Value.Source, spki);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(resolved.Value.PrivateKey);
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> SignPreHashedAsync(string walletAddress, byte[] digest, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);
        ArgumentNullException.ThrowIfNull(digest);

        var resolved = await TryResolveAsync(walletAddress, needPrivate: true, ct)
            ?? throw new OrgIssuerCertKeyNotEligibleException(walletAddress);

        var (privateKey, _, _) = resolved;
        try
        {
            var parameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = privateKey,
            };
            using var ecdsa = ECDsa.Create(parameters);
            // Default output is IEEE P1363 fixed-length r‖s — exactly what the Tenant's DER converter expects.
            return ecdsa.SignHash(digest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    private async Task<(byte[] PrivateKey, byte[] PublicPoint, string Source)?> TryResolveAsync(
        string walletAddress, bool needPrivate, CancellationToken ct)
    {
        var wallet = await _repository.GetByAddressAsync(walletAddress, false, false, false, ct);
        if (wallet is null || string.IsNullOrEmpty(wallet.EncryptedPrivateKey))
        {
            _logger.LogWarning("Org cert key resolve for {Address}: wallet missing or no master key", walletAddress);
            return null;
        }

        byte[] masterKey;
        try
        {
            masterKey = await _keyManagement.DecryptPrivateKeyAsync(wallet.EncryptedPrivateKey, wallet.EncryptionKeyId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Org cert key resolve for {Address}: master key decrypt failed", walletAddress);
            return null;
        }

        try
        {
            // ES256-primary org → bind the primary key directly.
            if (P256PrimaryAlgorithms.Contains(wallet.Algorithm) && !string.IsNullOrEmpty(wallet.PublicKey))
            {
                var publicPoint = Convert.FromBase64String(wallet.PublicKey!);
                if (publicPoint.Length != 64)
                {
                    _logger.LogWarning(
                        "Org cert key resolve for {Address}: primary {Alg} public key is {Len} bytes, expected 64",
                        walletAddress, wallet.Algorithm, publicPoint.Length);
                    return null;
                }
                // For a classical-primary wallet the decrypted master key is the 32-byte signing scalar.
                return (masterKey, publicPoint, "Primary");
            }

            // Otherwise derive the ES256 co-key under sorcha:haip-issuer-signing.
            var resolvedPath = SorchaDerivationPaths.ResolvePath(SorchaDerivationPaths.HaipIssuerSigning);
            var (derivedPrivate, derivedPublic) = await _keyManagement.DeriveKeyAtPathAsync(
                masterKey, new DerivationPath(resolvedPath), "ES256");
            CryptographicOperations.ZeroMemory(masterKey);

            if (derivedPublic.Length != 64)
            {
                _logger.LogWarning(
                    "Org cert key resolve for {Address}: derived ES256 co-key public is {Len} bytes, expected 64",
                    walletAddress, derivedPublic.Length);
                CryptographicOperations.ZeroMemory(derivedPrivate);
                return null;
            }
            return (derivedPrivate, derivedPublic, "HaipCoKey");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Org cert key resolve for {Address}: P-256 key derivation failed", walletAddress);
            CryptographicOperations.ZeroMemory(masterKey);
            return null;
        }
    }

    private static byte[] ExportSpki(byte[] publicPoint64)
    {
        var qx = new byte[32];
        var qy = new byte[32];
        Array.Copy(publicPoint64, 0, qx, 0, 32);
        Array.Copy(publicPoint64, 32, qy, 0, 32);
        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = qx, Y = qy },
        };
        using var ecdsa = ECDsa.Create(parameters);
        return ecdsa.ExportSubjectPublicKeyInfo();
    }
}
