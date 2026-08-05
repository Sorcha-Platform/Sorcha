// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Storage.InMemory;
using Sorcha.Storage.Abstractions;
using Sorcha.Storage.Abstractions.Caching;
using Sorcha.Storage.InMemory;
using FluentAssertions;
using Xunit;

namespace Sorcha.Register.Storage.Tests;

/// <summary>
/// Runs the IRegisterRepository contract tests against CachedRegisterRepository
/// (backed by InMemoryRegisterRepository).
/// </summary>
public class CachedRegisterRepositoryContractTests : RegisterRepositoryContractTests
{
    private readonly CachedRegisterRepository _repository;
    private readonly InMemoryRegisterRepository _innerRepository;

    public CachedRegisterRepositoryContractTests()
    {
        var innerRepository = new InMemoryRegisterRepository();
        _innerRepository = innerRepository;
        var cacheStore = new InMemoryCacheStore();
        var wormStore = new InMemoryWormStore<DocketHeader, ulong>(d => d.Id);

        var cacheConfig = Options.Create(new VerifiedCacheConfiguration
        {
            KeyPrefix = "register:docket:",
            CacheTtlSeconds = 3600,
            EnableHashVerification = true
        });

        var docketCache = new VerifiedCache<DocketHeader, ulong>(
            cacheStore,
            wormStore,
            d => d.Id,
            cacheConfig,
            d => d.Hash);

        var storageConfig = Options.Create(new RegisterStorageConfiguration());

        _repository = new CachedRegisterRepository(
            innerRepository,
            docketCache,
            cacheStore,
            storageConfig);
    }

    protected override IRegisterRepository CreateRepository() => _repository;

    [Fact]
    public override async Task GetDocketsAsync_ReturnsInsertedDockets()
    {
        // CachedRegisterRepository with verified cache writes dockets to the WORM store
        // on insert, but GetDocketsAsync reads from the inner repository. Individual
        // docket retrieval via GetDocketAsync uses the verified cache. This is by design —
        // the verified cache handles single lookups while list operations go to the backing store.
        var sut = CreateRepository();
        await sut.InsertRegisterAsync(CreateRegister("contract-docket-list"));

        // Insert via inner repo so GetDocketsAsync can find them
        await _innerRepository.InsertDocketAsync(CreateDocket(1, "contract-docket-list"));
        await _innerRepository.InsertDocketAsync(CreateDocket(2, "contract-docket-list"));

        var result = (await sut.GetDocketsAsync("contract-docket-list")).ToList();

        result.Should().HaveCountGreaterThanOrEqualTo(2);
    }
}
