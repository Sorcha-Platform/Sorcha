// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Service.Services.Implementation;
using Sorcha.Register.Service.Services.Interfaces;
using StackExchange.Redis;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// Unit tests for RedisBloomFilterAddressIndex.
/// </summary>
public class RedisBloomFilterAddressIndexTests
{
    private const string RegisterId = "test-register-001";
    private const string BloomKeyPrefix = "register:bloom:";
    private const string ParamsKeyPrefix = "register:bloom:params:";
    private const string RebuildSuffix = ":rebuild";

    private readonly Mock<IConnectionMultiplexer> _mockRedis;
    private readonly Mock<IDatabase> _mockDb;
    private readonly Mock<IBatch> _mockBatch;
    private readonly Mock<ILogger<RedisBloomFilterAddressIndex>> _mockLogger;
    private readonly IConfiguration _configuration;
    private readonly RedisBloomFilterAddressIndex _sut;

    public RedisBloomFilterAddressIndexTests()
    {
        _mockDb = new Mock<IDatabase>();
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockBatch = new Mock<IBatch>();
        _mockLogger = new Mock<ILogger<RedisBloomFilterAddressIndex>>();

        _mockRedis
            .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(_mockDb.Object);

        _mockDb
            .Setup(d => d.CreateBatch(It.IsAny<object?>()))
            .Returns(_mockBatch.Object);

        // Default: HashSetAsync on batch does nothing
        _mockBatch
            .Setup(b => b.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<HashEntry[]>(), It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BloomFilter:ExpectedAddressCount"] = "100000",
                ["BloomFilter:FalsePositiveRate"] = "0.001"
            })
            .Build();

        _sut = new RedisBloomFilterAddressIndex(_mockRedis.Object, _configuration, _mockLogger.Object);
    }

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_NewAddress_SetsBloomFilterBits()
    {
        // Arrange
        var address = "1A1zP1eP5QGefi2DMPTfTL5SLmv7Divf";

        // All GETBIT calls return false — address not yet present
        _mockDb
            .Setup(d => d.StringGetBitAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // SETBIT returns false (previous bit value)
        _mockDb
            .Setup(d => d.StringSetBitAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        _mockDb
            .Setup(d => d.HashIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        _mockDb
            .Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        _mockDb
            .Setup(d => d.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<HashEntry[]>(), It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.AddAsync(RegisterId, address);

        // Assert
        result.Should().BeTrue();
        _mockDb.Verify(
            d => d.StringSetBitAsync(
                It.Is<RedisKey>(k => k == BloomKeyPrefix + RegisterId),
                It.IsAny<long>(),
                true,
                It.IsAny<CommandFlags>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task AddAsync_ExistingAddress_ReturnsFalse()
    {
        // Arrange
        var address = "1A1zP1eP5QGefi2DMPTfTL5SLmv7Divf";

        // All GETBIT calls return true — all bits are already set (address present)
        _mockDb
            .Setup(d => d.StringGetBitAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.AddAsync(RegisterId, address);

        // Assert
        result.Should().BeFalse();

        // SETBIT should NOT be called when all bits are already set
        _mockDb.Verify(
            d => d.StringSetBitAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CommandFlags>()),
            Times.Never);
    }

    [Theory]
    [InlineData(null, "address")]
    [InlineData("", "address")]
    [InlineData("reg-1", null)]
    [InlineData("reg-1", "")]
    public async Task AddAsync_NullOrEmptyArguments_ThrowsArgumentException(string? registerId, string? address)
    {
        // Act
        var act = async () => await _sut.AddAsync(registerId!, address!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region MayContainAsync Tests

    [Fact]
    public async Task MayContainAsync_AddressPresent_ReturnsTrue()
    {
        // Arrange
        var address = "1A1zP1eP5QGefi2DMPTfTL5SLmv7Divf";

        // All GETBIT calls return true — all hash positions are set
        _mockDb
            .Setup(d => d.StringGetBitAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.MayContainAsync(RegisterId, address);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task MayContainAsync_AddressAbsent_ReturnsFalse()
    {
        // Arrange
        var address = "1A1zP1eP5QGefi2DMPTfTL5SLmv7Divf";

        // Return false for every bit check — address is definitely absent
        _mockDb
            .Setup(d => d.StringGetBitAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.MayContainAsync(RegisterId, address);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task MayContainAsync_OnePositionUnset_ReturnsFalse()
    {
        // Arrange
        var address = "1A1zP1eP5QGefi2DMPTfTL5SLmv7Divf";
        var callCount = 0;

        // First call returns false (first hash position is not set) — short-circuit
        _mockDb
            .Setup(d => d.StringGetBitAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount > 1; // first call returns false
            });

        // Act
        var result = await _sut.MayContainAsync(RegisterId, address);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "address")]
    [InlineData("", "address")]
    [InlineData("reg-1", null)]
    [InlineData("reg-1", "")]
    public async Task MayContainAsync_NullOrEmptyArguments_ThrowsArgumentException(string? registerId, string? address)
    {
        // Act
        var act = async () => await _sut.MayContainAsync(registerId!, address!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region RebuildAsync Tests

    [Fact]
    public async Task RebuildAsync_StreamsAddresses_BuildsNewFilter()
    {
        // Arrange
        var addresses = new[] { "addr-1", "addr-2", "addr-3" };
        var rebuildKey = BloomKeyPrefix + RegisterId + RebuildSuffix;

        _mockDb
            .Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _mockDb
            .Setup(d => d.StringSetBitAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        _mockDb
            .Setup(d => d.KeyRenameAsync(It.IsAny<RedisKey>(), It.IsAny<RedisKey>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var stats = await _sut.RebuildAsync(RegisterId, addresses.ToAsyncEnumerable());

        // Assert
        stats.Should().NotBeNull();
        stats.AddressCount.Should().Be(3);
        stats.BitArraySize.Should().BeGreaterThan(0);
        stats.HashFunctionCount.Should().BeGreaterThan(0);

        // Verify SETBIT called on the rebuild key (not the main key)
        _mockDb.Verify(
            d => d.StringSetBitAsync(
                It.Is<RedisKey>(k => k == rebuildKey),
                It.IsAny<long>(),
                true,
                It.IsAny<CommandFlags>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task RebuildAsync_AtomicSwap_CallsKeyRename()
    {
        // Arrange
        var rebuildKey = BloomKeyPrefix + RegisterId + RebuildSuffix;
        var addresses = new[] { "addr-1" };

        _mockDb
            .Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _mockDb
            .Setup(d => d.StringSetBitAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        _mockDb
            .Setup(d => d.KeyRenameAsync(It.IsAny<RedisKey>(), It.IsAny<RedisKey>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _sut.RebuildAsync(RegisterId, addresses.ToAsyncEnumerable());

        // Assert — atomic swap: RENAME rebuildKey -> bloomKey
        _mockDb.Verify(
            d => d.KeyRenameAsync(
                It.Is<RedisKey>(k => k == rebuildKey),
                It.Is<RedisKey>(k => k == BloomKeyPrefix + RegisterId),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task RebuildAsync_EmptyStream_RebuildsEmptyFilter()
    {
        // Arrange
        _mockDb
            .Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _mockDb
            .Setup(d => d.KeyRenameAsync(It.IsAny<RedisKey>(), It.IsAny<RedisKey>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var stats = await _sut.RebuildAsync(RegisterId, AsyncEnumerable.Empty<string>());

        // Assert
        stats.Should().NotBeNull();
        stats.AddressCount.Should().Be(0);

        // SETBIT should not be called for an empty stream
        _mockDb.Verify(
            d => d.StringSetBitAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CommandFlags>()),
            Times.Never);

        // But RENAME should still be called (atomic swap of empty filter)
        _mockDb.Verify(
            d => d.KeyRenameAsync(It.IsAny<RedisKey>(), It.IsAny<RedisKey>(), It.IsAny<When>(), It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task RebuildAsync_NullAddresses_ThrowsArgumentNullException()
    {
        // Act
        var act = async () => await _sut.RebuildAsync(RegisterId, null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RebuildAsync_NullOrEmptyRegisterId_ThrowsArgumentException()
    {
        // Act
        var act = async () => await _sut.RebuildAsync("", AsyncEnumerable.Empty<string>());

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RebuildAsync_SkipsNullOrEmptyAddresses_OnlyCountsValidOnes()
    {
        // Arrange
        var addresses = new[] { "valid-addr-1", "", "valid-addr-2", null!, "valid-addr-3" };

        _mockDb
            .Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _mockDb
            .Setup(d => d.StringSetBitAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        _mockDb
            .Setup(d => d.KeyRenameAsync(It.IsAny<RedisKey>(), It.IsAny<RedisKey>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var stats = await _sut.RebuildAsync(RegisterId, addresses.ToAsyncEnumerable());

        // Assert — only 3 valid addresses counted
        stats.AddressCount.Should().Be(3);
    }

    #endregion

    #region GetStatsAsync Tests

    [Fact]
    public async Task GetStatsAsync_ExistingFilter_ReturnsStats()
    {
        // Arrange
        var paramsKey = ParamsKeyPrefix + RegisterId;
        var lastRebuiltMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var entries = new HashEntry[]
        {
            new("address_count", "5000"),
            new("bit_array_size", "1437759"),
            new("hash_function_count", "10"),
            new("last_rebuilt_at", lastRebuiltMs.ToString())
        };

        _mockDb
            .Setup(d => d.HashGetAllAsync(
                It.Is<RedisKey>(k => k == paramsKey),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(entries);

        // Act
        var stats = await _sut.GetStatsAsync(RegisterId);

        // Assert
        stats.Should().NotBeNull();
        stats!.AddressCount.Should().Be(5000);
        stats.BitArraySize.Should().Be(1_437_759);
        stats.HashFunctionCount.Should().Be(10);
        stats.LastRebuiltAt.Should().NotBeNull();
        stats.LastRebuiltAt!.Value.ToUnixTimeMilliseconds().Should().Be(lastRebuiltMs);
    }

    [Fact]
    public async Task GetStatsAsync_NoFilter_ReturnsNull()
    {
        // Arrange — empty hash means no filter exists
        _mockDb
            .Setup(d => d.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<HashEntry>());

        // Act
        var stats = await _sut.GetStatsAsync(RegisterId);

        // Assert
        stats.Should().BeNull();
    }

    [Fact]
    public async Task GetStatsAsync_MissingOptionalFields_ReturnsDefaultValues()
    {
        // Arrange — only address_count present, others missing
        var entries = new HashEntry[]
        {
            new("address_count", "100")
        };

        _mockDb
            .Setup(d => d.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(entries);

        // Act
        var stats = await _sut.GetStatsAsync(RegisterId);

        // Assert
        stats.Should().NotBeNull();
        stats!.AddressCount.Should().Be(100);
        stats.BitArraySize.Should().Be(0);
        stats.HashFunctionCount.Should().Be(0);
        stats.LastRebuiltAt.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task GetStatsAsync_NullOrEmptyRegisterId_ThrowsArgumentException(string? registerId)
    {
        // Act
        var act = async () => await _sut.GetStatsAsync(registerId!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region CalculateOptimalParameters Tests

    [Fact]
    public void CalculateOptimalParameters_100kAddresses_ReturnsCorrectSizing()
    {
        // Arrange
        const int expectedCount = 100_000;
        const double falsePositiveRate = 0.001;

        // Act
        var (bitArraySize, hashCount) = RedisBloomFilterAddressIndex.CalculateOptimalParameters(
            expectedCount, falsePositiveRate);

        // Assert
        // m = -(100000 * ln(0.001)) / (ln(2)^2) ≈ 1,437,759
        bitArraySize.Should().BeCloseTo(1_437_759, 100);

        // k = (m/n) * ln(2) ≈ 10
        hashCount.Should().Be(10);
    }

    [Fact]
    public void CalculateOptimalParameters_LargeCount_ScalesLinearly()
    {
        // Arrange
        var (size100k, hash100k) = RedisBloomFilterAddressIndex.CalculateOptimalParameters(100_000, 0.001);
        var (size200k, hash200k) = RedisBloomFilterAddressIndex.CalculateOptimalParameters(200_000, 0.001);

        // Assert — doubling n roughly doubles m, k stays the same
        size200k.Should().BeGreaterThan(size100k);
        ((double)size200k / size100k).Should().BeApproximately(2.0, 0.1);
        hash200k.Should().Be(hash100k);
    }

    [Fact]
    public void CalculateOptimalParameters_ZeroOrNegativeCount_ClampedToOne()
    {
        // Act — should not throw; clamps expectedCount to 1
        var act = () => RedisBloomFilterAddressIndex.CalculateOptimalParameters(0, 0.001);

        // Assert
        act.Should().NotThrow();
        var (bitArraySize, hashCount) = act();
        bitArraySize.Should().BeGreaterThan(0);
        hashCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void CalculateOptimalParameters_InvalidFalsePositiveRate_UsesDefaultRate()
    {
        // Act — out-of-range FP rate should fall back to 0.001
        var (sizeWithDefault, hashWithDefault) = RedisBloomFilterAddressIndex.CalculateOptimalParameters(100_000, 0.001);
        var (sizeWithZero, hashWithZero) = RedisBloomFilterAddressIndex.CalculateOptimalParameters(100_000, 0.0);
        var (sizeWithOne, hashWithOne) = RedisBloomFilterAddressIndex.CalculateOptimalParameters(100_000, 1.0);

        // Assert — invalid values should produce same result as default
        sizeWithZero.Should().Be(sizeWithDefault);
        hashWithZero.Should().Be(hashWithDefault);
        sizeWithOne.Should().Be(sizeWithDefault);
        hashWithOne.Should().Be(hashWithDefault);
    }

    [Fact]
    public void CalculateOptimalParameters_AlwaysReturnsAtLeastOneHashFunction()
    {
        // Act
        var (_, hashCount) = RedisBloomFilterAddressIndex.CalculateOptimalParameters(1, 0.001);

        // Assert
        hashCount.Should().BeGreaterThanOrEqualTo(1);
    }

    #endregion

    #region ComputeHashPositions Tests

    [Fact]
    public void ComputeHashPositions_DeterministicOutput_SameInputSamePositions()
    {
        // Arrange
        const string address = "1A1zP1eP5QGefi2DMPTfTL5SLmv7Divf";
        const int hashCount = 10;
        const long bitArraySize = 1_437_759;

        // Act
        var positions1 = RedisBloomFilterAddressIndex.ComputeHashPositions(address, hashCount, bitArraySize);
        var positions2 = RedisBloomFilterAddressIndex.ComputeHashPositions(address, hashCount, bitArraySize);

        // Assert
        positions1.Should().BeEquivalentTo(positions2, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void ComputeHashPositions_DifferentAddresses_DifferentPositions()
    {
        // Arrange
        const int hashCount = 10;
        const long bitArraySize = 1_437_759;

        // Act
        var positions1 = RedisBloomFilterAddressIndex.ComputeHashPositions(
            "1A1zP1eP5QGefi2DMPTfTL5SLmv7Divf", hashCount, bitArraySize);
        var positions2 = RedisBloomFilterAddressIndex.ComputeHashPositions(
            "3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy", hashCount, bitArraySize);

        // Assert — different addresses produce different position sets
        positions1.Should().NotBeEquivalentTo(positions2);
    }

    [Fact]
    public void ComputeHashPositions_ReturnsCorrectCount()
    {
        // Arrange
        const int hashCount = 7;
        const long bitArraySize = 1_000_000;

        // Act
        var positions = RedisBloomFilterAddressIndex.ComputeHashPositions("any-address", hashCount, bitArraySize);

        // Assert
        positions.Should().HaveCount(hashCount);
    }

    [Fact]
    public void ComputeHashPositions_AllPositionsWithinBounds()
    {
        // Arrange
        const int hashCount = 10;
        const long bitArraySize = 1_437_759;

        // Act
        var positions = RedisBloomFilterAddressIndex.ComputeHashPositions(
            "1A1zP1eP5QGefi2DMPTfTL5SLmv7Divf", hashCount, bitArraySize);

        // Assert — every position must be within [0, bitArraySize)
        positions.Should().AllSatisfy(p =>
        {
            p.Should().BeGreaterThanOrEqualTo(0);
            p.Should().BeLessThan(bitArraySize);
        });
    }

    [Fact]
    public void ComputeHashPositions_DistributionAcrossSpace()
    {
        // Arrange — hash positions for many addresses should spread across the bit space
        const int hashCount = 10;
        const long bitArraySize = 1_437_759;
        var allPositions = new HashSet<long>();

        // Act — compute positions for 100 distinct addresses
        for (var i = 0; i < 100; i++)
        {
            var positions = RedisBloomFilterAddressIndex.ComputeHashPositions(
                $"address-{i:D6}", hashCount, bitArraySize);
            foreach (var p in positions)
                allPositions.Add(p);
        }

        // Assert — 100 addresses * 10 hash functions = 1000 calls; expect significant spread
        allPositions.Count.Should().BeGreaterThan(500);
    }

    #endregion

    #region False Positive Rate Tests

    [Fact]
    public async Task MayContainAsync_UnseenAddress_ReturnsNoFalseNegatives()
    {
        // Arrange — a correctly implemented bloom filter never returns false for a known-added address.
        // Simulate: add "known-addr", then check it exists.
        var knownAddress = "known-wallet-address-123";
        var setPositions = new HashSet<long>();

        // Track which positions are set during AddAsync
        _mockDb
            .Setup(d => d.StringGetBitAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey _, long offset, CommandFlags _) => setPositions.Contains(offset));

        _mockDb
            .Setup(d => d.StringSetBitAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey _, long offset, bool _, CommandFlags _) =>
            {
                setPositions.Add(offset);
                return false;
            });

        _mockDb
            .Setup(d => d.HashIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        _mockDb
            .Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        _mockDb
            .Setup(d => d.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<HashEntry[]>(), It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.AddAsync(RegisterId, knownAddress);
        var mayContain = await _sut.MayContainAsync(RegisterId, knownAddress);

        // Assert — bloom filter must return true for an address that was added (no false negatives)
        mayContain.Should().BeTrue();
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_NullRedis_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new RedisBloomFilterAddressIndex(null!, _configuration, _mockLogger.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullConfiguration_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new RedisBloomFilterAddressIndex(_mockRedis.Object, null!, _mockLogger.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new RedisBloomFilterAddressIndex(_mockRedis.Object, _configuration, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_DefaultConfiguration_UsesDefaultParameters()
    {
        // Arrange — config with no bloom filter keys; should use defaults (100000, 0.001)
        var emptyConfig = new ConfigurationBuilder().Build();

        // Act — should not throw; uses GetValue defaults
        var act = () => new RedisBloomFilterAddressIndex(_mockRedis.Object, emptyConfig, _mockLogger.Object);

        // Assert
        act.Should().NotThrow();
    }

    #endregion
}

/// <summary>
/// Helper extensions used by tests.
/// </summary>
internal static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (var item in source)
            yield return item;

        await Task.CompletedTask;
    }
}
