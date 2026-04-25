// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.ServiceDefaults.Storage;

namespace Sorcha.ServiceDefaults.Tests.Storage;

/// <summary>
/// Unit tests for <see cref="StorageProvidersHealthCheck"/>.
/// </summary>
public class StorageProvidersHealthCheckTests
{
    private const string AuditedInterface = "Sorcha.Wallet.Core.Repositories.IWalletRepository";

    private static StorageRegistrationLog NewLog() =>
        new(NullLogger<StorageRegistrationLog>.Instance);

    [Fact]
    public async Task Healthy_WhenAllAuditedPersistent()
    {
        var log = NewLog();
        log.RegisterPersistent(AuditedInterface, "EfCoreWalletRepository", "postgres");

        var check = new StorageProvidersHealthCheck(log);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("1 registered storage interfaces are persistent");
    }

    [Fact]
    public async Task Healthy_WhenLogIsEmpty()
    {
        var log = NewLog();
        var check = new StorageProvidersHealthCheck(log);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        // Distinct description for the empty case so operators can tell
        // "no services have registered yet" from "all services are persistent".
        result.Description.Should().Be("No storage interfaces registered yet.");
    }

    [Fact]
    public async Task Degraded_WhenAuditedInterfaceInMemory()
    {
        var log = NewLog();
        log.RegisterInMemory(AuditedInterface, "InMemoryWalletRepository", "no postgres");

        var check = new StorageProvidersHealthCheck(log);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain(AuditedInterface);
        result.Description.Should().Contain("InMemoryWalletRepository");
    }

    [Fact]
    public async Task Healthy_WhenOnlyCacheStoreInMemory()
    {
        // Cache stores are not on the audited list — degraded only fires for audited interfaces.
        var log = NewLog();
        log.RegisterInMemory(
            "Sorcha.Blueprint.Service.Storage.IBlueprintStore",
            "InMemoryBlueprintStore",
            "rebuilds on cold start");

        var check = new StorageProvidersHealthCheck(log);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Degraded_DescriptionEnumeratesAllOffenders()
    {
        var log = NewLog();
        log.RegisterInMemory("Sorcha.Wallet.Core.Repositories.IWalletRepository", "InMemoryWalletRepository", "no postgres");
        log.RegisterInMemory("Sorcha.Blueprint.Service.Storage.IInstanceStore", "InMemoryInstanceStore", "no postgres");

        var check = new StorageProvidersHealthCheck(log);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("IWalletRepository");
        result.Description.Should().Contain("IInstanceStore");
    }

    [Fact]
    public async Task Degraded_DataDictionaryContainsOffenders()
    {
        var log = NewLog();
        log.RegisterInMemory(AuditedInterface, "InMemoryWalletRepository", "no postgres");

        var check = new StorageProvidersHealthCheck(log);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Data.Should().ContainKey(AuditedInterface);
        result.Data[AuditedInterface].Should().Be("InMemoryWalletRepository");
    }
}
