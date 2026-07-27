// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Service.Storage.Presentations;
using StackExchange.Redis;

namespace Sorcha.Blueprint.Service.Tests.Storage.Presentations;

/// <summary>
/// T023 — unit tests for RedisPendingPresentationStore. Validates key naming
/// per data-model §2.1, TTL is applied from the validity window, and sentinel
/// operations use SET NX for first-writer-wins semantics.
/// </summary>
public class RedisPendingPresentationStoreTests
{
    private readonly Mock<IConnectionMultiplexer> _mockRedis;
    private readonly Mock<IDatabase> _mockDb;
    private readonly RedisPendingPresentationStore _store;

    public RedisPendingPresentationStoreTests()
    {
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockDb = new Mock<IDatabase>();
        _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDb.Object);

        _store = new RedisPendingPresentationStore(
            _mockRedis.Object,
            new Mock<ILogger<RedisPendingPresentationStore>>().Object);
    }

    [Fact]
    public void Constructor_NullRedis_Throws()
    {
        var act = () => new RedisPendingPresentationStore(
            null!, new Mock<ILogger<RedisPendingPresentationStore>>().Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task StoreAsync_SetsHashAndAppliesTtl_AtomicallyViaBatch()
    {
        var id = Guid.NewGuid();
        var pending = MakePending(id, validitySeconds: 300);

        // StoreAsync now pipelines HSET + EXPIRE through IBatch so a crash
        // between them cannot leave a hash without a TTL. The test mocks the
        // batch and captures both calls.
        var mockBatch = new Mock<IBatch>();
        _mockDb.Setup(d => d.CreateBatch(It.IsAny<object>())).Returns(mockBatch.Object);

        RedisKey? capturedHashKey = null;
        HashEntry[]? capturedFields = null;
        mockBatch.Setup(b => b.HashSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<HashEntry[]>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, HashEntry[], CommandFlags>((k, f, _) =>
            {
                capturedHashKey = k;
                capturedFields = f;
            })
            .Returns(Task.CompletedTask);

        TimeSpan? capturedTtl = null;
        mockBatch.Setup(b => b.KeyExpireAsync(
                It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(),
                It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, TimeSpan?, ExpireWhen, CommandFlags>((_, ttl, __, ___) =>
                capturedTtl = ttl)
            .ReturnsAsync(true);

        await _store.StoreAsync(pending);

        mockBatch.Verify(b => b.Execute(), Times.Once);
        capturedHashKey!.Value.ToString().Should().Be($"sorcha:presentation:pending:{id}");
        capturedFields.Should().NotBeNull();
        capturedTtl.Should().Be(TimeSpan.FromSeconds(300));

        var fieldMap = capturedFields!.ToDictionary(h => h.Name.ToString(), h => h.Value.ToString());
        fieldMap["consumerName"].Should().Be("haip");
        fieldMap["submitterWallet"].Should().Be("ws11qcitizen");
        fieldMap["recordAbandonment"].Should().Be("false");
        fieldMap["outcomeDetailLevel"].Should().Be("minimal");
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenHashEmpty()
    {
        _mockDb.Setup(d => d.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<HashEntry>());

        var result = await _store.GetAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_RoundTripsAllFields()
    {
        var id = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        _mockDb.Setup(d => d.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new HashEntry[]
            {
                new("instanceId", instanceId.ToString()),
                new("actionId", "3"),
                new("registerId", "reg-1"),
                new("blueprintId", "bp-1"),
                new("submitterWallet", "ws11qcitizen"),
                new("consumerName", "haip"),
                new("draftPayload", "{\"x\":1}"),
                new("credentialRequirementDigest", "deadbeef"),
                new("delegationToken", "jwt-token"),
                new("recordAbandonment", "true"),
                new("outcomeDetailLevel", "verbose"),
                new("validityWindowSeconds", "600"),
                new("createdAt", createdAt.ToString("o"))
            });

        var result = await _store.GetAsync(id);

        result.Should().NotBeNull();
        result!.InstanceId.Should().Be(instanceId);
        result.ActionId.Should().Be(3);
        result.RegisterId.Should().Be("reg-1");
        result.BlueprintId.Should().Be("bp-1");
        result.SubmitterWallet.Should().Be("ws11qcitizen");
        result.ConsumerName.Should().Be("haip");
        result.DraftPayloadJson.Should().Be("{\"x\":1}");
        result.CredentialRequirementDigestHex.Should().Be("deadbeef");
        result.DelegationToken.Should().Be("jwt-token");
        result.RecordAbandonment.Should().BeTrue();
        result.OutcomeDetailLevel.Should().Be("verbose");
        result.ValidityWindowSeconds.Should().Be(600);
    }

    [Fact]
    public async Task StoreAsync_ThenGetAsync_RoundTripsT032VerifierSessionFields()
    {
        // Regression test for the #1195 Phase 2 / T032 field-drop bug: PendingPresentation
        // grew Nonce/VerifierClientId/CredentialType/RequiredClaimNames, but the store's
        // hand-maintained field list on the way into Redis and the hand-maintained
        // reconstruction on the way out never picked them up. Every field was silently
        // dropped, so every sorcha-wallet presentation declined in production ("the
        // pending state carries no nonce/credentialType"). This drives the real
        // StoreAsync path (capturing exactly what would be HSET to Redis) and feeds
        // those captured fields back through HashGetAllAsync, so it fails the moment
        // StoreAsync and GetAsync drift on any field again.
        var id = Guid.NewGuid();
        var pending = MakePending(id, validitySeconds: 300) with
        {
            Nonce = "nonce-abc123",
            VerifierClientId = "did:sorcha:org:ws11qcouncil",
            CredentialType = "urn:eu.europa.ec.eudi:pid:1",
            RequiredClaimNames = new List<string> { "given_name", "birth_date" }
        };

        var capturedFields = await CaptureStoredFieldsAsync(pending);
        SetupHashGetAllFromCapturedFields(capturedFields);

        var result = await _store.GetAsync(id);

        result.Should().NotBeNull();
        result!.Nonce.Should().Be("nonce-abc123");
        result.VerifierClientId.Should().Be("did:sorcha:org:ws11qcouncil");
        result.CredentialType.Should().Be("urn:eu.europa.ec.eudi:pid:1");
        result.RequiredClaimNames.Should().BeEquivalentTo(new[] { "given_name", "birth_date" });

        // Pre-existing fields must still round-trip alongside the new ones.
        result.InstanceId.Should().Be(pending.InstanceId);
        result.SubmitterWallet.Should().Be(pending.SubmitterWallet);
        result.CredentialRequirementDigestHex.Should().Be(pending.CredentialRequirementDigestHex);
    }

    [Fact]
    public async Task StoreAsync_ThenGetAsync_RoundTripsRequiredClaimNamesNull()
    {
        var id = Guid.NewGuid();
        var pending = MakePending(id, validitySeconds: 300) with
        {
            Nonce = "nonce-1",
            VerifierClientId = "did:sorcha:org:x",
            CredentialType = "urn:vct:x",
            RequiredClaimNames = null
        };

        var capturedFields = await CaptureStoredFieldsAsync(pending);
        SetupHashGetAllFromCapturedFields(capturedFields);

        var result = await _store.GetAsync(id);

        result.Should().NotBeNull();
        result!.RequiredClaimNames.Should().BeNull();
    }

    [Fact]
    public async Task StoreAsync_ThenGetAsync_RoundTripsRequiredClaimNamesEmptyList_DistinctFromNull()
    {
        var id = Guid.NewGuid();
        var pending = MakePending(id, validitySeconds: 300) with
        {
            Nonce = "nonce-2",
            VerifierClientId = "did:sorcha:org:x",
            CredentialType = "urn:vct:x",
            RequiredClaimNames = new List<string>()
        };

        var capturedFields = await CaptureStoredFieldsAsync(pending);
        SetupHashGetAllFromCapturedFields(capturedFields);

        var result = await _store.GetAsync(id);

        result.Should().NotBeNull();
        result!.RequiredClaimNames.Should().NotBeNull();
        result.RequiredClaimNames.Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_ThenGetAsync_RoundTripsRequiredClaimNamesPopulated()
    {
        var id = Guid.NewGuid();
        var pending = MakePending(id, validitySeconds: 300) with
        {
            Nonce = "nonce-3",
            VerifierClientId = "did:sorcha:org:x",
            CredentialType = "urn:vct:x",
            RequiredClaimNames = new List<string> { "family_name", "age_over_18", "document_number" }
        };

        var capturedFields = await CaptureStoredFieldsAsync(pending);
        SetupHashGetAllFromCapturedFields(capturedFields);

        var result = await _store.GetAsync(id);

        result.Should().NotBeNull();
        result!.RequiredClaimNames.Should().BeEquivalentTo(
            new[] { "family_name", "age_over_18", "document_number" });
    }

    /// <summary>
    /// Drives the real <c>StoreAsync</c> path and captures the <see cref="HashEntry"/>[]
    /// it would HSET to Redis, so round-trip tests exercise the actual write logic
    /// instead of hand-crafting the expected wire shape.
    /// </summary>
    private async Task<HashEntry[]> CaptureStoredFieldsAsync(PendingPresentation pending)
    {
        var mockBatch = new Mock<IBatch>();
        _mockDb.Setup(d => d.CreateBatch(It.IsAny<object>())).Returns(mockBatch.Object);

        HashEntry[]? captured = null;
        mockBatch.Setup(b => b.HashSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<HashEntry[]>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, HashEntry[], CommandFlags>((_, f, _) => captured = f)
            .Returns(Task.CompletedTask);
        mockBatch.Setup(b => b.KeyExpireAsync(
                It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(),
                It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        await _store.StoreAsync(pending);

        return captured!;
    }

    /// <summary>Wires HashGetAllAsync to return exactly the fields StoreAsync wrote.</summary>
    private void SetupHashGetAllFromCapturedFields(HashEntry[] fields)
    {
        _mockDb.Setup(d => d.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(fields);
    }

    [Fact]
    public async Task TryClaimOutcomeSentinelAsync_UsesSetNx()
    {
        var id = Guid.NewGuid();

        When? capturedWhen = null;
        RedisKey? capturedKey = null;
        _mockDb.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, When>((k, _, _, w) =>
            {
                capturedKey = k;
                capturedWhen = w;
            })
            .ReturnsAsync(true);

        var ok = await _store.TryClaimOutcomeSentinelAsync(id, "claimant-1", validityWindowSeconds: 300);

        ok.Should().BeTrue();
        capturedWhen.Should().Be(When.NotExists);
        capturedKey!.Value.ToString().Should().Be($"sorcha:presentation:outcome-sentinel:{id}");
    }

    [Fact]
    public async Task TryClaimOutcomeSentinelAsync_TtlHonoursValidityWindow()
    {
        // Regression guard for claude-review bug #2: sentinel TTL must track the
        // blueprint's validity window plus the 1h overshoot, not a hardcoded 600s.
        TimeSpan? capturedTtl = null;
        _mockDb.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, When>((_, _, ttl, _) => capturedTtl = ttl)
            .ReturnsAsync(true);

        await _store.TryClaimOutcomeSentinelAsync(Guid.NewGuid(), "c", validityWindowSeconds: 1800);

        // 1800s validity + 1h overshoot
        capturedTtl.Should().Be(TimeSpan.FromSeconds(1800) + TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task TryClaimOutcomeSentinelAsync_SecondClaim_ReturnsFalse()
    {
        _mockDb.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .ReturnsAsync(false);

        var ok = await _store.TryClaimOutcomeSentinelAsync(Guid.NewGuid(), "claimant-2", validityWindowSeconds: 300);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task GetOutcomeSentinelAsync_ReturnsNullWhenUnset()
    {
        _mockDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var result = await _store.GetOutcomeSentinelAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetOutcomeSentinelAsync_ReturnsValue_WhenSet()
    {
        _mockDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"success");

        var result = await _store.GetOutcomeSentinelAsync(Guid.NewGuid());

        result.Should().Be("success");
    }

    [Fact]
    public async Task DeleteAsync_UsesCorrectKey()
    {
        var id = Guid.NewGuid();
        RedisKey? capturedKey = null;
        _mockDb.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, CommandFlags>((k, _) => capturedKey = k)
            .ReturnsAsync(true);

        await _store.DeleteAsync(id);

        capturedKey!.Value.ToString().Should().Be($"sorcha:presentation:pending:{id}");
    }

    private static PendingPresentation MakePending(Guid id, int validitySeconds) => new()
    {
        PresentationRequestId = id,
        InstanceId = Guid.NewGuid(),
        ActionId = 3,
        RegisterId = "reg-1",
        BlueprintId = "bp-1",
        SubmitterWallet = "ws11qcitizen",
        ConsumerName = "haip",
        DraftPayloadJson = "{}",
        CredentialRequirementDigestHex = "deadbeef",
        RecordAbandonment = false,
        OutcomeDetailLevel = "minimal",
        ValidityWindowSeconds = validitySeconds,
        CreatedAt = DateTimeOffset.UtcNow
    };
}
