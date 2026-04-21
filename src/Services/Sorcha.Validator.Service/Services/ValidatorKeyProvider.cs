// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Default <see cref="IValidatorKeyProvider"/> that resolves the validator's docket-signing
/// public key by asking Wallet.Service to sign a constant sentinel payload under the
/// <c>sorcha:docket-signing</c> derivation path. The returned <c>SignResult.PublicKey</c>
/// is the key we need; the signature itself is discarded.
/// </summary>
/// <remarks>
/// Probe-by-sign is the simplest path that doesn't require changing the Wallet.Service API
/// surface. The sentinel is a fixed 32-byte hash, pre-hashed, so the operation is fast and
/// adds no side effects. Result is cached for the process lifetime.
/// </remarks>
public sealed class ValidatorKeyProvider : IValidatorKeyProvider
{
    // 32-byte sentinel — "sorcha:validator-key-probe" SHA-256'd offline, inlined here to
    // keep the probe deterministic and self-contained.
    private static readonly byte[] SentinelHash =
    [
        0x73, 0x6f, 0x72, 0x63, 0x68, 0x61, 0x3a, 0x76, 0x61, 0x6c, 0x69, 0x64, 0x61, 0x74, 0x6f, 0x72,
        0x2d, 0x6b, 0x65, 0x79, 0x2d, 0x70, 0x72, 0x6f, 0x62, 0x65, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    ];

    private readonly IWalletServiceClient _walletClient;
    private readonly ISystemWalletProvider _systemWallet;
    private readonly ILogger<ValidatorKeyProvider> _logger;

    private byte[]? _cached;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ValidatorKeyProvider(
        IWalletServiceClient walletClient,
        ISystemWalletProvider systemWallet,
        ILogger<ValidatorKeyProvider> logger)
    {
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _systemWallet = systemWallet ?? throw new ArgumentNullException(nameof(systemWallet));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetValidatorPublicKeyAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is { Length: > 0 }) return _cached;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is { Length: > 0 }) return _cached;

            var walletAddress = _systemWallet.GetSystemWalletId();
            if (string.IsNullOrEmpty(walletAddress))
            {
                _logger.LogDebug("System wallet not yet initialised — validator public key not resolvable");
                return null;
            }

            try
            {
                var result = await _walletClient.SignTransactionAsync(
                    walletAddress,
                    SentinelHash,
                    derivationPath: "sorcha:docket-signing",
                    isPreHashed: true,
                    cancellationToken);

                if (result?.PublicKey is null || result.PublicKey.Length == 0)
                {
                    _logger.LogWarning("Wallet.Service sign-probe returned no PublicKey — validator key unresolved");
                    return null;
                }

                _cached = result.PublicKey;
                _logger.LogInformation(
                    "Resolved validator docket-signing public key ({Bytes} bytes, algorithm={Algorithm})",
                    _cached.Length, result.Algorithm);
                return _cached;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to resolve validator docket-signing public key via Wallet.Service");
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Invalidate()
    {
        _cached = null;
    }
}
