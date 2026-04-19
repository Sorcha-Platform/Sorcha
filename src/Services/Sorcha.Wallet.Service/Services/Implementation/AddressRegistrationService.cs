// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Grpc;
using Sorcha.ServiceClients.Register;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Orchestrates wallet address registration with the Register Service bloom filter
/// via gRPC. Called on wallet create/delete to keep the local address index in sync.
/// </summary>
public class AddressRegistrationService : IAddressRegistrationService
{
    private readonly IRegisterAddressClient _registerAddressClient;
    private readonly IRegisterServiceClient _registerServiceClient;
    private readonly ILogger<AddressRegistrationService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="AddressRegistrationService"/>.
    /// </summary>
    /// <param name="registerAddressClient">gRPC client for Register Service address operations.</param>
    /// <param name="registerServiceClient">HTTP client used to enumerate registers for Option B fan-out.</param>
    /// <param name="logger">Logger instance.</param>
    public AddressRegistrationService(
        IRegisterAddressClient registerAddressClient,
        IRegisterServiceClient registerServiceClient,
        ILogger<AddressRegistrationService> logger)
    {
        _registerAddressClient = registerAddressClient ?? throw new ArgumentNullException(nameof(registerAddressClient));
        _registerServiceClient = registerServiceClient ?? throw new ArgumentNullException(nameof(registerServiceClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> RegisterAddressAsync(
        string address, string registerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);
        ArgumentException.ThrowIfNullOrEmpty(registerId);

        try
        {
            var response = await _registerAddressClient.RegisterLocalAddressAsync(
                address, registerId, cancellationToken);

            if (!response.Success)
            {
                _logger.LogWarning(
                    "Register Service declined RegisterLocalAddress for address {Address} in register {RegisterId}",
                    address, registerId);
            }

            return response.Success;
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(
                ex,
                "gRPC call RegisterLocalAddress failed for address {Address} in register {RegisterId}: {StatusCode} — {Detail}",
                address, registerId, ex.StatusCode, ex.Status.Detail);

            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAddressAsync(
        string address, string registerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);
        ArgumentException.ThrowIfNullOrEmpty(registerId);

        try
        {
            var response = await _registerAddressClient.RemoveLocalAddressAsync(
                address, registerId, cancellationToken);

            if (!response.Success)
            {
                _logger.LogWarning(
                    "Register Service declined RemoveLocalAddress for address {Address} in register {RegisterId}",
                    address, registerId);
            }

            return response.Success;
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(
                ex,
                "gRPC call RemoveLocalAddress failed for address {Address} in register {RegisterId}: {StatusCode} — {Detail}",
                address, registerId, ex.StatusCode, ex.Status.Detail);

            return false;
        }
    }

    /// <inheritdoc />
    public async Task NotifyLocalAddressCreatedAsync(string address, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(address))
        {
            return;
        }

        List<InternalRegisterInfo> registers;
        try
        {
            registers = await _registerServiceClient.GetInternalRegistersAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Bloom fan-out skipped for {Address}: failed to enumerate registers. Startup rebuild will reconcile.",
                address);
            return;
        }

        if (registers.Count == 0)
        {
            _logger.LogDebug(
                "Bloom fan-out for {Address}: no registers known yet. Startup rebuild will reconcile once registers exist.",
                address);
            return;
        }

        var added = 0;
        foreach (var register in registers)
        {
            if (string.IsNullOrEmpty(register.Id))
            {
                continue;
            }

            try
            {
                var ok = await RegisterAddressAsync(address, register.Id, cancellationToken);
                if (ok)
                {
                    added++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Bloom registration failed for address {Address} on register {RegisterId}; startup rebuild will reconcile.",
                    address, register.Id);
            }
        }

        _logger.LogInformation(
            "Bloom fan-out complete for {Address}: added to {Added}/{Total} registers.",
            address, added, registers.Count);
    }
}
