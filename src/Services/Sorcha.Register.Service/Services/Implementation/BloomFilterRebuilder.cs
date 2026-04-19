// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;
using Sorcha.Register.Service.Services.Interfaces;
using Sorcha.ServiceClients.Grpc;
using Sorcha.Wallet.Service.Grpc;

namespace Sorcha.Register.Service.Services.Implementation;

/// <inheritdoc />
public sealed class BloomFilterRebuilder : IBloomFilterRebuilder
{
    private readonly ILocalAddressIndex _addressIndex;
    private readonly IWalletNotificationClient _walletClient;
    private readonly ILogger<BloomFilterRebuilder> _logger;

    public BloomFilterRebuilder(
        ILocalAddressIndex addressIndex,
        IWalletNotificationClient walletClient,
        ILogger<BloomFilterRebuilder> logger)
    {
        _addressIndex = addressIndex ?? throw new ArgumentNullException(nameof(addressIndex));
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<BloomFilterStats> RebuildAsync(string registerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(registerId)) throw new ArgumentException("registerId required", nameof(registerId));

        var addresses = _walletClient.GetAllLocalAddressesAsync(
            registerId, activeOnly: true, cancellationToken);

        var stats = await _addressIndex.RebuildAsync(
            registerId, ExtractAddresses(addresses, cancellationToken), cancellationToken);

        _logger.LogInformation(
            "Bloom rebuild for register {RegisterId}: {AddressCount} addresses indexed.",
            registerId, stats.AddressCount);

        return stats;
    }

    private static async IAsyncEnumerable<string> ExtractAddresses(
        IAsyncEnumerable<LocalAddressEntry> entries,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var entry in entries.WithCancellation(cancellationToken))
        {
            if (!string.IsNullOrEmpty(entry.Address))
            {
                yield return entry.Address;
            }
        }
    }
}
