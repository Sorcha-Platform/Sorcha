// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Service.Grpc;
using Sorcha.Register.Service.Services.Interfaces;
using Sorcha.ServiceClients.Grpc;
using Sorcha.Wallet.Service.Grpc;

namespace Sorcha.Register.Service.GrpcServices;

/// <summary>
/// gRPC service for managing local address registration in the bloom filter.
/// Handles RegisterLocalAddress, RemoveLocalAddress, and RebuildAddressIndex RPCs.
/// </summary>
public class RegisterAddressGrpcService : RegisterAddressService.RegisterAddressServiceBase
{
    private readonly ILocalAddressIndex _addressIndex;
    private readonly IWalletNotificationClient _walletNotificationClient;
    private readonly ILogger<RegisterAddressGrpcService> _logger;

    public RegisterAddressGrpcService(
        ILocalAddressIndex addressIndex,
        IWalletNotificationClient walletNotificationClient,
        ILogger<RegisterAddressGrpcService> logger)
    {
        _addressIndex = addressIndex ?? throw new ArgumentNullException(nameof(addressIndex));
        _walletNotificationClient = walletNotificationClient ?? throw new ArgumentNullException(nameof(walletNotificationClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public override async Task<RegisterLocalAddressResponse> RegisterLocalAddress(
        RegisterLocalAddressRequest request, ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.Address) || string.IsNullOrEmpty(request.RegisterId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Address and RegisterId are required"));
        }

        try
        {
            var added = await _addressIndex.AddAsync(request.RegisterId, request.Address, context.CancellationToken);

            return new RegisterLocalAddressResponse
            {
                Success = true,
                Message = added ? "Address added to bloom filter" : "Address was already present"
            };
        }
        catch (Exception ex) when (ex is not RpcException)
        {
            _logger.LogError(ex, "Failed to register local address {Address} for register {RegisterId}",
                request.Address, request.RegisterId);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to register address"));
        }
    }

    /// <inheritdoc />
    public override async Task<RemoveLocalAddressResponse> RemoveLocalAddress(
        RemoveLocalAddressRequest request, ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.Address) || string.IsNullOrEmpty(request.RegisterId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Address and RegisterId are required"));
        }

        try
        {
            // Bloom filters don't support deletion — trigger a full rebuild
            _logger.LogInformation(
                "Remove requested for address {Address} in register {RegisterId} — triggering bloom filter rebuild",
                request.Address, request.RegisterId);

            var addresses = _walletNotificationClient.GetAllLocalAddressesAsync(
                request.RegisterId, activeOnly: true, ct: context.CancellationToken);

            var addressStream = FilterOutRemovedAddress(addresses, request.Address);

            var stats = await _addressIndex.RebuildAsync(request.RegisterId, addressStream, context.CancellationToken);

            return new RemoveLocalAddressResponse
            {
                Success = true,
                RebuildTriggered = true,
                Message = $"Bloom filter rebuilt with {stats.AddressCount} addresses (removed address excluded)"
            };
        }
        catch (Exception ex) when (ex is not RpcException)
        {
            _logger.LogError(ex, "Failed to remove local address {Address} for register {RegisterId}",
                request.Address, request.RegisterId);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to remove address"));
        }
    }

    /// <inheritdoc />
    public override async Task<RebuildAddressIndexResponse> RebuildAddressIndex(
        RebuildAddressIndexRequest request, ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.RegisterId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "RegisterId is required"));
        }

        try
        {
            var sw = Stopwatch.StartNew();

            var addresses = _walletNotificationClient.GetAllLocalAddressesAsync(
                request.RegisterId, activeOnly: true, ct: context.CancellationToken);

            var addressStream = ExtractAddresses(addresses);

            var stats = await _addressIndex.RebuildAsync(request.RegisterId, addressStream, context.CancellationToken);

            sw.Stop();

            _logger.LogInformation(
                "Bloom filter rebuild completed for register {RegisterId}: {AddressCount} addresses in {Duration}ms",
                request.RegisterId, stats.AddressCount, sw.ElapsedMilliseconds);

            return new RebuildAddressIndexResponse
            {
                Success = true,
                AddressCount = stats.AddressCount,
                RebuildDurationMs = sw.ElapsedMilliseconds,
                Message = $"Bloom filter rebuilt successfully with {stats.AddressCount} addresses"
            };
        }
        catch (Exception ex) when (ex is not RpcException)
        {
            _logger.LogError(ex, "Failed to rebuild address index for register {RegisterId}", request.RegisterId);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to rebuild address index"));
        }
    }

    private static async IAsyncEnumerable<string> FilterOutRemovedAddress(
        IAsyncEnumerable<LocalAddressEntry> entries, string addressToRemove,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var entry in entries.WithCancellation(cancellationToken))
        {
            if (!string.Equals(entry.Address, addressToRemove, StringComparison.OrdinalIgnoreCase))
                yield return entry.Address;
        }
    }

    private static async IAsyncEnumerable<string> ExtractAddresses(
        IAsyncEnumerable<LocalAddressEntry> entries,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var entry in entries.WithCancellation(cancellationToken))
        {
            yield return entry.Address;
        }
    }
}
