// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Service.Grpc;

namespace Sorcha.ServiceClients.Grpc;

/// <summary>
/// Client interface for Register Address gRPC service.
/// Direction: Wallet Service → Register Service.
/// Manages local wallet address registration in the bloom filter.
/// </summary>
public interface IRegisterAddressClient
{
    /// <summary>Register a wallet address as local in the bloom filter.</summary>
    Task<RegisterLocalAddressResponse> RegisterLocalAddressAsync(
        string address, string registerId, CancellationToken ct = default);

    /// <summary>Remove a wallet address from the local index (triggers rebuild).</summary>
    Task<RemoveLocalAddressResponse> RemoveLocalAddressAsync(
        string address, string registerId, CancellationToken ct = default);

    /// <summary>Trigger a full rebuild of the bloom filter for a register.</summary>
    Task<RebuildAddressIndexResponse> RebuildAddressIndexAsync(
        string registerId, CancellationToken ct = default);
}
