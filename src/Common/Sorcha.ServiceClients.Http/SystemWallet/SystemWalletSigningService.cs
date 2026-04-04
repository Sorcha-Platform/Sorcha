// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.ServiceClients.SystemWallet;

/// <summary>
/// Secure system wallet signing service with whitelist, rate limiting, audit logging,
/// and automatic wallet lifecycle management.
/// </summary>
public class SystemWalletSigningService : ISystemWalletSigningService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SystemWalletSigningOptions _options;
    private readonly ILogger<SystemWalletSigningService> _logger;
    private readonly HashSet<string> _allowedPaths;
    private readonly ConcurrentDictionary<string, SlidingWindowCounter> _rateLimitCounters = new();
    private readonly SemaphoreSlim _walletLock = new(1, 1);
    private string? _walletAddress;

    public SystemWalletSigningService(
        IServiceScopeFactory scopeFactory,
        SystemWalletSigningOptions options,
        ILogger<SystemWalletSigningService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _allowedPaths = new HashSet<string>(options.AllowedDerivationPaths, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<SystemSignResult> SignAsync(
        string registerId,
        string txId,
        string payloadHash,
        string derivationPath,
        string transactionType,
        CancellationToken cancellationToken = default)
    {
        // 1. Whitelist check
        if (!_allowedPaths.Contains(derivationPath))
        {
            LogAudit("WhitelistRejected", registerId, txId, transactionType, derivationPath, null);
            throw new InvalidOperationException(
                $"Derivation path '{derivationPath}' is not in the operation whitelist");
        }

        // 2. Rate limit check
        var counter = _rateLimitCounters.GetOrAdd(registerId, _ => new SlidingWindowCounter());
        if (!counter.TryIncrement(_options.MaxSignsPerRegisterPerMinute))
        {
            LogAudit("RateLimited", registerId, txId, transactionType, derivationPath, null);
            throw new InvalidOperationException(
                $"Rate limit exceeded for register '{registerId}': max {_options.MaxSignsPerRegisterPerMinute} signs per minute");
        }

        // 3. Acquire wallet
        var walletAddress = await EnsureWalletAsync(cancellationToken);

        // 4. Sign
        try
        {
            var result = await SignWithWalletAsync(walletAddress, txId, payloadHash, derivationPath, cancellationToken);
            LogAudit("Success", registerId, txId, transactionType, derivationPath, walletAddress);
            return result;
        }
        catch (Exception ex) when (IsWalletUnavailable(ex))
        {
            // 5. Wallet recovery: clear cache, recreate, retry once
            _logger.LogWarning(ex, "System wallet unavailable, attempting recreation");
            _walletAddress = null;
            walletAddress = await EnsureWalletAsync(cancellationToken);

            try
            {
                var result = await SignWithWalletAsync(walletAddress, txId, payloadHash, derivationPath, cancellationToken);
                LogAudit("Success", registerId, txId, transactionType, derivationPath, walletAddress);
                return result;
            }
            catch (Exception retryEx)
            {
                LogAudit("Error", registerId, txId, transactionType, derivationPath, walletAddress);
                throw new InvalidOperationException(
                    "System wallet unavailable after retry", retryEx);
            }
        }
    }

    private async Task<string> EnsureWalletAsync(CancellationToken ct)
    {
        if (_walletAddress is not null)
            return _walletAddress;

        await _walletLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_walletAddress is not null)
                return _walletAddress;

            using var scope = _scopeFactory.CreateScope();
            var walletClient = scope.ServiceProvider.GetRequiredService<IWalletServiceClient>();
            _walletAddress = await walletClient.CreateOrRetrieveSystemWalletAsync(
                _options.ValidatorId, ct);

            _logger.LogInformation("System wallet acquired: {WalletAddress}", _walletAddress);
            return _walletAddress;
        }
        finally
        {
            _walletLock.Release();
        }
    }

    private async Task<SystemSignResult> SignWithWalletAsync(
        string walletAddress,
        string txId,
        string payloadHash,
        string derivationPath,
        CancellationToken ct)
    {
        // Signing data format: SHA-256(UTF-8("{TxId}:{PayloadHash}")) → sign with isPreHashed
        var dataToSign = $"{txId}:{payloadHash}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(dataToSign));

        using var scope = _scopeFactory.CreateScope();
        var walletClient = scope.ServiceProvider.GetRequiredService<IWalletServiceClient>();
        var signResult = await walletClient.SignTransactionAsync(
            walletAddress, hashBytes, derivationPath, isPreHashed: true, ct);

        return new SystemSignResult
        {
            Signature = signResult.Signature,
            PublicKey = signResult.PublicKey,
            Algorithm = signResult.Algorithm,
            WalletAddress = signResult.SignedBy
        };
    }

    private static bool IsWalletUnavailable(Exception ex)
    {
        var message = ex.Message;
        return message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("401", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unavailable", StringComparison.OrdinalIgnoreCase);
    }

    private void LogAudit(
        string outcome,
        string registerId,
        string txId,
        string transactionType,
        string derivationPath,
        string? walletAddress)
    {
        _logger.LogInformation(
            "SystemWalletSigning: {Outcome} | Register={RegisterId} TxId={TxId} Type={TransactionType} Path={DerivationPath} Wallet={WalletAddress} Caller={CallerService}",
            outcome, registerId, txId, transactionType, derivationPath,
            walletAddress ?? "N/A", _options.ValidatorId);
    }

    /// <summary>
    /// Sliding window rate limiter that tracks timestamps within a 1-minute window
    /// </summary>
    private sealed class SlidingWindowCounter
    {
        private readonly Queue<DateTimeOffset> _timestamps = new();
        private readonly object _lock = new();

        public bool TryIncrement(int maxPerMinute)
        {
            lock (_lock)
            {
                var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);

                // Remove expired entries
                while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
                    _timestamps.Dequeue();

                if (_timestamps.Count >= maxPerMinute)
                    return false;

                _timestamps.Enqueue(DateTimeOffset.UtcNow);
                return true;
            }
        }
    }
}
