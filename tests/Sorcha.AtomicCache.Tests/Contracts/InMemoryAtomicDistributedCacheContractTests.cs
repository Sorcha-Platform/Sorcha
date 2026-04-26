// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.AtomicCache;

namespace Sorcha.AtomicCache.Tests.Contracts;

/// <summary>
/// Runs the <see cref="IAtomicDistributedCacheContractTests"/> suite
/// against <see cref="InMemoryAtomicDistributedCache"/>.
/// </summary>
public class InMemoryAtomicDistributedCacheContractTests : IAtomicDistributedCacheContractTests
{
    protected override IAtomicDistributedCache CreateCache() => new InMemoryAtomicDistributedCache();
}
