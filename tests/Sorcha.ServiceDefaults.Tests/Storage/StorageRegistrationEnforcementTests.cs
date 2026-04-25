// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.ServiceDefaults.Storage;

namespace Sorcha.ServiceDefaults.Tests.Storage;

/// <summary>
/// Unit tests for <see cref="StorageRegistrationEnforcement"/> — the
/// fail-fast helper that gates Production / Staging startup when audited
/// storage interfaces fall through to in-memory implementations.
/// </summary>
public class StorageRegistrationEnforcementTests
{
    private const string AuditedInterface = "Sorcha.Wallet.Core.Repositories.IWalletRepository";

    private static StorageRegistrationLog NewLog() =>
        new(NullLogger<StorageRegistrationLog>.Instance);

    private static IHostEnvironment Env(string envName, string app = "Sorcha.Test.Service") =>
        new FakeHostEnvironment { EnvironmentName = envName, ApplicationName = app };

    [Fact]
    public void Production_WithAuditedInMemory_Throws()
    {
        var log = NewLog();
        log.RegisterInMemory(AuditedInterface, "InMemoryWalletRepository", "no connection string");

        var act = () => StorageRegistrationEnforcement.EnforcePersistentStorageInProduction(
            log, Env(Environments.Production), allowInMemoryOverride: false, NullLogger.Instance);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*refuses to start*Production*")
            .Which.Message.Should().Contain(AuditedInterface);
    }

    [Fact]
    public void Staging_WithAuditedInMemory_Throws()
    {
        var log = NewLog();
        log.RegisterInMemory(AuditedInterface, "InMemoryWalletRepository", "no connection string");

        var act = () => StorageRegistrationEnforcement.EnforcePersistentStorageInProduction(
            log, Env(Environments.Staging), allowInMemoryOverride: false, NullLogger.Instance);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*refuses to start*Staging*");
    }

    [Fact]
    public void Development_WithAuditedInMemory_DoesNotThrow()
    {
        var log = NewLog();
        log.RegisterInMemory(AuditedInterface, "InMemoryWalletRepository", "no connection string");

        var act = () => StorageRegistrationEnforcement.EnforcePersistentStorageInProduction(
            log, Env(Environments.Development), allowInMemoryOverride: false, NullLogger.Instance);

        act.Should().NotThrow();
    }

    [Fact]
    public void Production_WithBypassFlag_DoesNotThrow()
    {
        var log = NewLog();
        log.RegisterInMemory(AuditedInterface, "InMemoryWalletRepository", "no connection string");

        var act = () => StorageRegistrationEnforcement.EnforcePersistentStorageInProduction(
            log, Env(Environments.Production), allowInMemoryOverride: true, NullLogger.Instance);

        act.Should().NotThrow();
    }

    [Fact]
    public void Production_WithCacheStoreInMemoryOnly_DoesNotThrow()
    {
        // Cache stores like IBlueprintStore are not on the audited list — they should not gate startup.
        var log = NewLog();
        log.RegisterInMemory(
            "Sorcha.Blueprint.Service.Storage.IBlueprintStore",
            "InMemoryBlueprintStore",
            "rebuilds on cold start");

        var act = () => StorageRegistrationEnforcement.EnforcePersistentStorageInProduction(
            log, Env(Environments.Production), allowInMemoryOverride: false, NullLogger.Instance);

        act.Should().NotThrow();
    }

    [Fact]
    public void Production_WithAllAuditedPersistent_DoesNotThrow()
    {
        var log = NewLog();
        log.RegisterPersistent(AuditedInterface, "EfCoreWalletRepository", "postgres");

        var act = () => StorageRegistrationEnforcement.EnforcePersistentStorageInProduction(
            log, Env(Environments.Production), allowInMemoryOverride: false, NullLogger.Instance);

        act.Should().NotThrow();
    }

    [Fact]
    public void Production_WithEmptyLog_DoesNotThrow()
    {
        var log = NewLog();

        var act = () => StorageRegistrationEnforcement.EnforcePersistentStorageInProduction(
            log, Env(Environments.Production), allowInMemoryOverride: false, NullLogger.Instance);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrownMessage_ListsAllOffendingInterfaces()
    {
        var log = NewLog();
        log.RegisterInMemory("Sorcha.Wallet.Core.Repositories.IWalletRepository", "InMemoryWalletRepository", "no postgres");
        log.RegisterInMemory("Sorcha.Blueprint.Service.Storage.IInstanceStore", "InMemoryInstanceStore", "no postgres");

        var act = () => StorageRegistrationEnforcement.EnforcePersistentStorageInProduction(
            log, Env(Environments.Production), allowInMemoryOverride: false, NullLogger.Instance);

        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("Sorcha.Wallet.Core.Repositories.IWalletRepository");
        ex.Message.Should().Contain("Sorcha.Blueprint.Service.Storage.IInstanceStore");
        ex.Message.Should().Contain("InMemoryWalletRepository");
        ex.Message.Should().Contain("InMemoryInstanceStore");
        ex.Message.Should().Contain(StorageRegistrationEnforcement.AllowInMemoryConfigKey);
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
