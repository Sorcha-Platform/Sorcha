// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using StackExchange.Redis;

namespace Sorcha.Blueprint.Service.Storage.Presentations;

/// <summary>
/// Redis-backed <see cref="IPendingPresentationStore"/>. Keys per
/// <c>specs/111-presentation-lifecycle/data-model.md</c> §2.
/// </summary>
public sealed class RedisPendingPresentationStore : IPendingPresentationStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisPendingPresentationStore> _logger;

    private const string PendingPrefix = "sorcha:presentation:pending:";
    private const string SentinelPrefix = "sorcha:presentation:outcome-sentinel:";

    // Sentinel outlives the pending hash by 1 hour so late callbacks after
    // abandonment still find something to override.
    private static readonly TimeSpan SentinelOvershootTtl = TimeSpan.FromHours(1);

    public RedisPendingPresentationStore(
        IConnectionMultiplexer redis,
        ILogger<RedisPendingPresentationStore> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StoreAsync(PendingPresentation pending, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = PendingKey(pending.PresentationRequestId);
        var fields = new HashEntry[]
        {
            new("instanceId",                     pending.InstanceId.ToString()),
            new("actionId",                       pending.ActionId),
            new("registerId",                     pending.RegisterId),
            new("blueprintId",                    pending.BlueprintId),
            new("submitterWallet",                pending.SubmitterWallet),
            new("consumerName",                   pending.ConsumerName),
            new("draftPayload",                   pending.DraftPayloadJson),
            new("credentialRequirementDigest",    pending.CredentialRequirementDigestHex),
            new("delegationToken",                pending.DelegationToken ?? string.Empty),
            new("recordAbandonment",              pending.RecordAbandonment ? "true" : "false"),
            new("outcomeDetailLevel",             pending.OutcomeDetailLevel),
            new("validityWindowSeconds",          pending.ValidityWindowSeconds),
            new("createdAt",                      pending.CreatedAt.ToString("o"))
        };

        await db.HashSetAsync(key, fields);
        await db.KeyExpireAsync(key, TimeSpan.FromSeconds(pending.ValidityWindowSeconds));

        _logger.LogDebug(
            "Stored pending presentation {RequestId} for instance {InstanceId} action {ActionId}, TTL {TtlSec}s",
            pending.PresentationRequestId, pending.InstanceId, pending.ActionId, pending.ValidityWindowSeconds);
    }

    public async Task<PendingPresentation?> GetAsync(Guid presentationRequestId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = PendingKey(presentationRequestId);
        var entries = await db.HashGetAllAsync(key);
        if (entries.Length == 0) return null;

        var map = entries.ToDictionary(e => (string)e.Name!, e => (string)e.Value!);

        return new PendingPresentation
        {
            PresentationRequestId = presentationRequestId,
            InstanceId = Guid.Parse(map["instanceId"]),
            ActionId = int.Parse(map["actionId"]),
            RegisterId = map["registerId"],
            BlueprintId = map["blueprintId"],
            SubmitterWallet = map["submitterWallet"],
            ConsumerName = map["consumerName"],
            DraftPayloadJson = map["draftPayload"],
            CredentialRequirementDigestHex = map["credentialRequirementDigest"],
            DelegationToken = string.IsNullOrEmpty(map["delegationToken"]) ? null : map["delegationToken"],
            RecordAbandonment = map["recordAbandonment"] == "true",
            OutcomeDetailLevel = map["outcomeDetailLevel"],
            ValidityWindowSeconds = int.Parse(map["validityWindowSeconds"]),
            CreatedAt = DateTimeOffset.Parse(map["createdAt"])
        };
    }

    public Task DeleteAsync(Guid presentationRequestId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        return db.KeyDeleteAsync(PendingKey(presentationRequestId));
    }

    public async Task<bool> TryClaimOutcomeSentinelAsync(Guid presentationRequestId, string claimantValue, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = SentinelKey(presentationRequestId);
        // Use conditional SET to realise NX semantics. TTL overshoots the pending
        // TTL so late callbacks after abandonment can still read the sentinel.
        var ttl = TimeSpan.FromSeconds(600) + SentinelOvershootTtl; // placeholder; caller usually aligns separately
        return await db.StringSetAsync(key, claimantValue, ttl, When.NotExists);
    }

    public async Task<string?> GetOutcomeSentinelAsync(Guid presentationRequestId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(SentinelKey(presentationRequestId));
        return value.HasValue ? value.ToString() : null;
    }

    public async Task SetOutcomeSentinelAsync(Guid presentationRequestId, string value, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var ttl = TimeSpan.FromSeconds(600) + SentinelOvershootTtl;
        await db.StringSetAsync(SentinelKey(presentationRequestId), value, ttl);
    }

    public async Task<IReadOnlyList<Guid>> ListPendingNearExpiryAsync(TimeSpan withinDuration, int max, CancellationToken ct = default)
    {
        var results = new List<Guid>();
        var endpoints = _redis.GetEndPoints();
        if (endpoints.Length == 0) return results;

        var server = _redis.GetServer(endpoints[0]);
        var db = _redis.GetDatabase();

        await foreach (var key in server.KeysAsync(pattern: PendingPrefix + "*", pageSize: 250).WithCancellation(ct))
        {
            var ttl = await db.KeyTimeToLiveAsync(key);
            if (!ttl.HasValue) continue;
            if (ttl.Value <= withinDuration)
            {
                var idStr = key.ToString().Substring(PendingPrefix.Length);
                if (Guid.TryParse(idStr, out var id))
                {
                    results.Add(id);
                    if (results.Count >= max) break;
                }
            }
        }

        return results;
    }

    private static string PendingKey(Guid id) => PendingPrefix + id;
    private static string SentinelKey(Guid id) => SentinelPrefix + id;
}
