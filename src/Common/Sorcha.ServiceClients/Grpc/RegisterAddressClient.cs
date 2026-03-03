// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Service.Grpc;

namespace Sorcha.ServiceClients.Grpc;

/// <summary>
/// gRPC client for Register Address Service.
/// Manages local wallet address registration in the bloom filter.
/// Uses GrpcClientFactory for Aspire service discovery and HTTP handler pooling.
/// </summary>
public class RegisterAddressClient : IRegisterAddressClient
{
    internal const string ClientName = "RegisterAddress";

    private readonly GrpcClientFactory _clientFactory;
    private readonly ILogger<RegisterAddressClient> _logger;

    public RegisterAddressClient(GrpcClientFactory clientFactory, ILogger<RegisterAddressClient> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RegisterLocalAddressResponse> RegisterLocalAddressAsync(
        string address, string registerId, CancellationToken ct = default)
    {
        _logger.LogDebug("Registering local address {Address} for register {RegisterId}", address, registerId);
        var client = _clientFactory.CreateClient<RegisterAddressService.RegisterAddressServiceClient>(ClientName);
        return await client.RegisterLocalAddressAsync(
            new RegisterLocalAddressRequest { Address = address, RegisterId = registerId },
            cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task<RemoveLocalAddressResponse> RemoveLocalAddressAsync(
        string address, string registerId, CancellationToken ct = default)
    {
        _logger.LogDebug("Removing local address {Address} from register {RegisterId}", address, registerId);
        var client = _clientFactory.CreateClient<RegisterAddressService.RegisterAddressServiceClient>(ClientName);
        return await client.RemoveLocalAddressAsync(
            new RemoveLocalAddressRequest { Address = address, RegisterId = registerId },
            cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task<RebuildAddressIndexResponse> RebuildAddressIndexAsync(
        string registerId, CancellationToken ct = default)
    {
        _logger.LogInformation("Requesting bloom filter rebuild for register {RegisterId}", registerId);
        var client = _clientFactory.CreateClient<RegisterAddressService.RegisterAddressServiceClient>(ClientName);
        return await client.RebuildAddressIndexAsync(
            new RebuildAddressIndexRequest { RegisterId = registerId },
            cancellationToken: ct);
    }
}
