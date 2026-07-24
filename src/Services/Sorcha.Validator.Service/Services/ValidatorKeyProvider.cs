// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Wallet.Contracts.Constants;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Default <see cref="IValidatorKeyProvider"/> that resolves the validator's docket-signing
/// public key by asking Wallet.Service to sign a constant sentinel payload under the
/// <c>sorcha:docket-signing</c> derivation path. The returned <c>SignResult.PublicKey</c>
/// is the key we need; the signature itself is discarded.
/// </summary>
/// <remarks>
/// <para>
/// Probe-by-sign is the simplest path that doesn't require changing the Wallet.Service API
/// surface. The sentinel is a fixed 32-byte hash, pre-hashed, so the operation is fast and
/// adds no side effects. Result is cached for the process lifetime.
/// </para>
/// <para>
/// Security note — the sentinel bytes (<c>"sorcha:validator-key-probe"</c> zero-padded to
/// 32 bytes) are PUBLIC and not a valid docket-hash. A signature over the sentinel cannot
/// be replayed against any Sorcha verifier: <see cref="Sorcha.Cryptography.Utilities.DocketHasher"/>
/// produces 32-byte SHA-256 outputs of canonical docket envelopes, and the sentinel is
/// neither (it's lower-case ASCII followed by null bytes). Audit logs that capture all
/// validator-key-signing operations will see one signature per validator startup over
/// this constant — that's expected and benign, not a credential leak.
/// </para>
/// </remarks>
public sealed class ValidatorKeyProvider : IValidatorKeyProvider
{
    // 32-byte sentinel payload — the UTF-8 bytes of "sorcha:validator-key-probe" (26 bytes)
    // zero-padded to 32 bytes so Wallet.Service treats it as a pre-hashed payload. The exact
    // content is not cryptographically significant — we only need deterministic bytes to sign,
    // then discard the signature and keep the returned PublicKey.
    private static readonly byte[] SentinelHash =
    [
        0x73, 0x6f, 0x72, 0x63, 0x68, 0x61, 0x3a, 0x76, 0x61, 0x6c, 0x69, 0x64, 0x61, 0x74, 0x6f, 0x72,
        0x2d, 0x6b, 0x65, 0x79, 0x2d, 0x70, 0x72, 0x6f, 0x62, 0x65, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISystemWalletProvider _systemWallet;
    private readonly ILogger<ValidatorKeyProvider> _logger;

    private byte[]? _cached;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ValidatorKeyProvider(
        IServiceScopeFactory scopeFactory,
        ISystemWalletProvider systemWallet,
        ILogger<ValidatorKeyProvider> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
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
                await using var scope = _scopeFactory.CreateAsyncScope();
                var walletClient = scope.ServiceProvider.GetRequiredService<IWalletServiceClient>();
                var result = await walletClient.SignTransactionAsync(
                    walletAddress,
                    SentinelHash,
                    derivationPath: SorchaDerivationPaths.DocketSigning,
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
