// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.ServiceDefaults.Storage;

namespace Sorcha.ServiceDefaults.Tests.Storage;

/// <summary>
/// Tests for <see cref="StorageEnforcementHostedService"/> — the
/// <c>IHostedService</c> that runs the enforcement helper at host startup.
/// </summary>
public class StorageEnforcementHostedServiceTests
{
    private const string AuditedInterface = "Sorcha.Wallet.Core.Repositories.Interfaces.IWalletRepository";

    private static StorageRegistrationLog NewLog() =>
        new();

    private static IHostEnvironment Env(string envName, string app = "Sorcha.Test.Service") =>
        new FakeHostEnvironment { EnvironmentName = envName, ApplicationName = app };

    private static IConfiguration Config(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();

    private static StorageEnforcementHostedService NewService(
        IStorageRegistrationLog log,
        IHostEnvironment env,
        IConfiguration? config = null) =>
        new(
            log,
            env,
            config ?? Config(),
            NullLogger<StorageEnforcementHostedService>.Instance);

    [Fact]
    public async Task StartAsync_Production_AuditedInMemory_NoOverride_Throws()
    {
        var log = NewLog();
        log.RegisterInMemory(AuditedInterface, "InMemoryWalletRepository", "no postgres");

        var svc = NewService(log, Env(Environments.Production));

        await svc.Invoking(s => s.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StartAsync_Production_AuditedInMemory_OverrideTrue_DoesNotThrow()
    {
        var log = NewLog();
        log.RegisterInMemory(AuditedInterface, "InMemoryWalletRepository", "no postgres");

        var config = Config(new Dictionary<string, string?>
        {
            [StorageRegistrationEnforcement.AllowInMemoryConfigKey] = "true"
        });
        var svc = NewService(log, Env(Environments.Production), config);

        await svc.Invoking(s => s.StartAsync(CancellationToken.None)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_Production_AllPersistent_DoesNotThrow()
    {
        var log = NewLog();
        log.RegisterPersistent(AuditedInterface, "EfCoreWalletRepository", "postgres");

        var svc = NewService(log, Env(Environments.Production));

        await svc.Invoking(s => s.StartAsync(CancellationToken.None)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_Development_AuditedInMemory_DoesNotThrow()
    {
        var log = NewLog();
        log.RegisterInMemory(AuditedInterface, "InMemoryWalletRepository", "no postgres");

        var svc = NewService(log, Env(Environments.Development));

        await svc.Invoking(s => s.StartAsync(CancellationToken.None)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_Staging_AuditedInMemory_NoOverride_Throws()
    {
        var log = NewLog();
        log.RegisterInMemory(AuditedInterface, "InMemoryWalletRepository", "no postgres");

        var svc = NewService(log, Env(Environments.Staging));

        await svc.Invoking(s => s.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StopAsync_DoesNotThrow()
    {
        var log = NewLog();
        var svc = NewService(log, Env(Environments.Production));

        await svc.Invoking(s => s.StopAsync(CancellationToken.None)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_OverrideKeyNonBoolean_TreatedAsFalse()
    {
        // GetValue<bool> returns default(bool)=false for non-parseable values like "yes" or "1xyz".
        // This test pins that behaviour: only the literal string "true" (case-insensitive) bypasses.
        var log = NewLog();
        log.RegisterInMemory(AuditedInterface, "InMemoryWalletRepository", "no postgres");

        var config = Config(new Dictionary<string, string?>
        {
            [StorageRegistrationEnforcement.AllowInMemoryConfigKey] = "yes"
        });
        var svc = NewService(log, Env(Environments.Production), config);

        await svc.Invoking(s => s.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Sorcha.Test.Service";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
