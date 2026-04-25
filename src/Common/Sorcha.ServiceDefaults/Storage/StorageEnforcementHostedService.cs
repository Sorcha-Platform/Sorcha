// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sorcha.ServiceDefaults.Storage;

/// <summary>
/// Runs <see cref="StorageRegistrationEnforcement.EnforcePersistentStorageInProduction"/>
/// once at host startup, after the DI container is built but before the host
/// begins accepting traffic. If enforcement throws, the host startup itself
/// fails and the service refuses to start.
/// </summary>
public sealed class StorageEnforcementHostedService : IHostedService
{
    private readonly IStorageRegistrationLog _log;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StorageEnforcementHostedService> _logger;

    /// <summary>Creates a new enforcement hosted service.</summary>
    public StorageEnforcementHostedService(
        IStorageRegistrationLog log,
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger<StorageEnforcementHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _log = log;
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var allowOverride = _configuration.GetValue<bool>(
            StorageRegistrationEnforcement.AllowInMemoryConfigKey);

        StorageRegistrationEnforcement.EnforcePersistentStorageInProduction(
            _log, _environment, allowOverride, _logger);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
