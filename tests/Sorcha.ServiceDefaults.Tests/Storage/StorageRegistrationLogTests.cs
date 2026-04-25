// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.ServiceDefaults.Storage;

namespace Sorcha.ServiceDefaults.Tests.Storage;

/// <summary>
/// Unit tests for <see cref="StorageRegistrationLog"/>.
/// </summary>
public class StorageRegistrationLogTests
{
    private static StorageRegistrationLog NewLog() =>
        new(NullLogger<StorageRegistrationLog>.Instance);

    [Fact]
    public void RegisterPersistent_WithAuditedInterface_RecordsAuditedNotInMemory()
    {
        var log = NewLog();

        log.RegisterPersistent(
            "Sorcha.Wallet.Core.Repositories.IWalletRepository",
            "Sorcha.Wallet.Core.Repositories.EfCoreWalletRepository",
            "postgres");

        var snapshot = log.Snapshot();
        snapshot.Should().HaveCount(1);
        snapshot[0].InterfaceName.Should().Be("Sorcha.Wallet.Core.Repositories.IWalletRepository");
        snapshot[0].ImplementationName.Should().Be("Sorcha.Wallet.Core.Repositories.EfCoreWalletRepository");
        snapshot[0].Backend.Should().Be("postgres");
        snapshot[0].IsInMemory.Should().BeFalse();
        snapshot[0].IsAudited.Should().BeTrue();
    }

    [Fact]
    public void RegisterInMemory_WithAuditedInterface_RecordsInMemoryAndAudited()
    {
        var log = NewLog();

        log.RegisterInMemory(
            "Sorcha.Wallet.Core.Repositories.IWalletRepository",
            "Sorcha.Wallet.Core.Repositories.InMemoryWalletRepository",
            "no Postgres connection string configured");

        var snapshot = log.Snapshot();
        snapshot.Should().HaveCount(1);
        snapshot[0].Backend.Should().Be("in-memory");
        snapshot[0].IsInMemory.Should().BeTrue();
        snapshot[0].IsAudited.Should().BeTrue();
        snapshot[0].Reason.Should().Be("no Postgres connection string configured");
    }

    [Fact]
    public void RegisterInMemory_WithUnauditedCacheStore_RecordsInMemoryNotAudited()
    {
        var log = NewLog();

        log.RegisterInMemory(
            "Sorcha.Blueprint.Service.Storage.IBlueprintStore",
            "Sorcha.Blueprint.Service.Storage.InMemoryBlueprintStore",
            "rebuilds from register transaction log on cold start");

        var snapshot = log.Snapshot();
        snapshot.Should().HaveCount(1);
        snapshot[0].IsInMemory.Should().BeTrue();
        snapshot[0].IsAudited.Should().BeFalse();
    }

    [Fact]
    public void Register_DuplicateInterfaceAcrossPersistentAndInMemory_Throws()
    {
        var log = NewLog();
        log.RegisterPersistent("ISomeRepo", "EfCoreImpl", "postgres");

        var act = () => log.RegisterInMemory("ISomeRepo", "InMemoryImpl", "fallback");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already registered*");
    }

    [Fact]
    public void Register_DuplicateInterfaceAcrossTwoPersistents_Throws()
    {
        var log = NewLog();
        log.RegisterPersistent("ISomeRepo", "FirstImpl", "postgres");

        var act = () => log.RegisterPersistent("ISomeRepo", "SecondImpl", "postgres");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already registered*");
    }

    [Fact]
    public void Snapshot_ReturnsImmutableCopy()
    {
        var log = NewLog();
        log.RegisterPersistent("IRepoA", "ImplA", "postgres");

        var first = log.Snapshot();

        log.RegisterPersistent("IRepoB", "ImplB", "redis");

        var second = log.Snapshot();

        first.Should().HaveCount(1);
        second.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void RegisterPersistent_BlankInterfaceName_Throws(string? interfaceName)
    {
        var log = NewLog();
        var act = () => log.RegisterPersistent(interfaceName!, "Impl", "postgres");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void RegisterInMemory_BlankReason_Throws(string? reason)
    {
        var log = NewLog();
        var act = () => log.RegisterInMemory("IFoo", "InMemoryFoo", reason!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RegisterPersistent_AllAuditedInterfaces_AreRecognised()
    {
        var log = NewLog();

        var auditedNames = AuditedStorageInterfaces.Names.ToArray();
        for (var i = 0; i < auditedNames.Length; i++)
        {
            log.RegisterPersistent(auditedNames[i], $"Impl{i}", "test-backend");
        }

        log.Snapshot().Should().AllSatisfy(r => r.IsAudited.Should().BeTrue());
    }

    [Fact]
    public async Task RegisterPersistent_ConcurrentCallsDifferentInterfaces_AllRecorded()
    {
        var log = NewLog();

        var tasks = Enumerable.Range(0, 100)
            .Select(i => Task.Run(() => log.RegisterPersistent($"IRepo{i}", $"Impl{i}", "postgres")))
            .ToArray();

        await Task.WhenAll(tasks);

        log.Snapshot().Should().HaveCount(100);
    }

    [Fact]
    public async Task Register_ConcurrentCallsSameInterface_OnlyOneSucceeds()
    {
        var log = NewLog();

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => Task.Run<Exception?>(() =>
            {
                try
                {
                    log.RegisterPersistent("ISharedRepo", "Impl", "postgres");
                    return null;
                }
                catch (InvalidOperationException ex)
                {
                    return ex;
                }
            }))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Exactly one task succeeds (returns null); the rest see the duplicate-registration throw.
        results.Count(r => r is null).Should().Be(1);
        results.Count(r => r is InvalidOperationException).Should().Be(49);

        log.Snapshot().Should().HaveCount(1);
    }
}
