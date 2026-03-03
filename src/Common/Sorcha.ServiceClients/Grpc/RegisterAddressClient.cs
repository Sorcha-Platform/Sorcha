// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Service.Grpc;

namespace Sorcha.ServiceClients.Grpc;

/// <summary>
/// gRPC client for Register Address Service.
/// Manages local wallet address registration in the bloom filter.
/// </summary>
public class RegisterAddressClient : IRegisterAddressClient, IDisposable
{
    private readonly RegisterAddressService.RegisterAddressServiceClient _client;
    private readonly GrpcChannel _channel;
    private readonly ILogger<RegisterAddressClient> _logger;

    public RegisterAddressClient(IConfiguration configuration, ILogger<RegisterAddressClient> logger)
    {
        _logger = logger;
        var address = configuration["ServiceClients:RegisterService:GrpcAddress"]
            ?? configuration["ServiceClients:RegisterService:Address"]
            ?? "https://localhost:7290";
        _channel = GrpcChannel.ForAddress(address);
        _client = new RegisterAddressService.RegisterAddressServiceClient(_channel);
    }

    /// <inheritdoc />
    public async Task<RegisterLocalAddressResponse> RegisterLocalAddressAsync(
        string address, string registerId, CancellationToken ct = default)
    {
        _logger.LogDebug("Registering local address {Address} for register {RegisterId}", address, registerId);
        return await _client.RegisterLocalAddressAsync(
            new RegisterLocalAddressRequest { Address = address, RegisterId = registerId },
            cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task<RemoveLocalAddressResponse> RemoveLocalAddressAsync(
        string address, string registerId, CancellationToken ct = default)
    {
        _logger.LogDebug("Removing local address {Address} from register {RegisterId}", address, registerId);
        return await _client.RemoveLocalAddressAsync(
            new RemoveLocalAddressRequest { Address = address, RegisterId = registerId },
            cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task<RebuildAddressIndexResponse> RebuildAddressIndexAsync(
        string registerId, CancellationToken ct = default)
    {
        _logger.LogInformation("Requesting bloom filter rebuild for register {RegisterId}", registerId);
        return await _client.RebuildAddressIndexAsync(
            new RebuildAddressIndexRequest { RegisterId = registerId },
            cancellationToken: ct);
    }

    public void Dispose()
    {
        _channel.Dispose();
    }
}
