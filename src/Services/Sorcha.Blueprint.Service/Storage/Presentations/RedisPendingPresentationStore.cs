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
            new("createdAt",                      pending.CreatedAt.ToString("o")),
            new("initiatedTxId",                  pending.InitiatedTransactionId ?? string.Empty),
            // #1195 Phase 2 (Task 6b, T032) — verifier-session fields. Nonce/VerifierClientId/
            // CredentialType follow the same nullable-string convention as delegationToken above
            // (empty string on the wire ⇒ null on read). RequiredClaimNames is a List<string>? —
            // Redis hash values are flat strings, so it is JSON-encoded; JsonSerializer.Serialize(null)
            // round-trips as the literal "null" (distinct from "[]"), so null vs empty-list survives.
            new("nonce",                          pending.Nonce ?? string.Empty),
            new("verifierClientId",               pending.VerifierClientId ?? string.Empty),
            new("credentialType",                 pending.CredentialType ?? string.Empty),
            new("requiredClaimNames",             JsonSerializer.Serialize(pending.RequiredClaimNames))
        };

        // Pipeline the HSET and EXPIRE so a crash between them cannot leave a
        // hash without a TTL. Both commands are dispatched atomically from the
        // client's perspective and their results are observed together.
        var batch = db.CreateBatch();
        var hashTask = batch.HashSetAsync(key, fields);
        var expireTask = batch.KeyExpireAsync(key, TimeSpan.FromSeconds(pending.ValidityWindowSeconds));
        batch.Execute();
        await Task.WhenAll(hashTask, expireTask);

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

        // Guard against partial writes (crash after HashSet but before KeyExpire,
        // or field schema drift). Missing required fields → null + warning
        // rather than an unhandled KeyNotFoundException bubbling up through the
        // callback endpoint.
        string? Required(string field)
        {
            if (map.TryGetValue(field, out var v) && !string.IsNullOrEmpty(v)) return v;
            _logger.LogWarning(
                "Pending presentation {RequestId} missing required field {Field} — treating as expired/corrupt",
                presentationRequestId, field);
            return null;
        }

        var instanceIdStr = Required("instanceId");
        var actionIdStr = Required("actionId");
        var registerId = Required("registerId");
        var blueprintId = Required("blueprintId");
        var submitterWallet = Required("submitterWallet");
        var consumerName = Required("consumerName");
        var digest = Required("credentialRequirementDigest");
        var validityStr = Required("validityWindowSeconds");
        var createdAtStr = Required("createdAt");

        if (instanceIdStr is null || actionIdStr is null || registerId is null ||
            blueprintId is null || submitterWallet is null || consumerName is null ||
            digest is null || validityStr is null || createdAtStr is null)
        {
            return null;
        }

        if (!Guid.TryParse(instanceIdStr, out var instanceId) ||
            !int.TryParse(actionIdStr, out var actionId) ||
            !int.TryParse(validityStr, out var validity) ||
            !DateTimeOffset.TryParse(createdAtStr, out var createdAt))
        {
            _logger.LogWarning(
                "Pending presentation {RequestId} has malformed field values — treating as expired/corrupt",
                presentationRequestId);
            return null;
        }

        return new PendingPresentation
        {
            PresentationRequestId = presentationRequestId,
            InstanceId = instanceId,
            ActionId = actionId,
            RegisterId = registerId,
            BlueprintId = blueprintId,
            SubmitterWallet = submitterWallet,
            ConsumerName = consumerName,
            DraftPayloadJson = map.GetValueOrDefault("draftPayload", string.Empty),
            CredentialRequirementDigestHex = digest,
            DelegationToken = string.IsNullOrEmpty(map.GetValueOrDefault("delegationToken"))
                ? null
                : map["delegationToken"],
            RecordAbandonment = map.GetValueOrDefault("recordAbandonment") == "true",
            OutcomeDetailLevel = map.GetValueOrDefault("outcomeDetailLevel", "minimal"),
            ValidityWindowSeconds = validity,
            CreatedAt = createdAt,
            InitiatedTransactionId = string.IsNullOrEmpty(map.GetValueOrDefault("initiatedTxId"))
                ? null
                : map["initiatedTxId"],
            // #1195 Phase 2 (Task 6b, T032) — verifier-session fields. Missing field (entries
            // written before this fix, or genuinely absent values) reads back as null, matching
            // the "legacy pending entry" fallback the consumer already handles.
            Nonce = string.IsNullOrEmpty(map.GetValueOrDefault("nonce"))
                ? null
                : map["nonce"],
            VerifierClientId = string.IsNullOrEmpty(map.GetValueOrDefault("verifierClientId"))
                ? null
                : map["verifierClientId"],
            CredentialType = string.IsNullOrEmpty(map.GetValueOrDefault("credentialType"))
                ? null
                : map["credentialType"],
            RequiredClaimNames = string.IsNullOrEmpty(map.GetValueOrDefault("requiredClaimNames"))
                ? null
                : JsonSerializer.Deserialize<List<string>?>(map["requiredClaimNames"])
        };
    }

    public Task DeleteAsync(Guid presentationRequestId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        return db.KeyDeleteAsync(PendingKey(presentationRequestId));
    }

    public async Task<bool> TryClaimOutcomeSentinelAsync(
        Guid presentationRequestId,
        string claimantValue,
        int validityWindowSeconds,
        CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = SentinelKey(presentationRequestId);
        // TTL overshoots the pending window so late callbacks after abandonment
        // still find the sentinel (research R6).
        var ttl = TimeSpan.FromSeconds(validityWindowSeconds) + SentinelOvershootTtl;
        return await db.StringSetAsync(key, claimantValue, ttl, When.NotExists);
    }

    public async Task<string?> GetOutcomeSentinelAsync(Guid presentationRequestId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(SentinelKey(presentationRequestId));
        return value.HasValue ? value.ToString() : null;
    }

    public async Task SetOutcomeSentinelAsync(
        Guid presentationRequestId,
        string value,
        int validityWindowSeconds,
        CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var ttl = TimeSpan.FromSeconds(validityWindowSeconds) + SentinelOvershootTtl;
        await db.StringSetAsync(SentinelKey(presentationRequestId), value, ttl);
    }

    public Task DeleteOutcomeSentinelAsync(Guid presentationRequestId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        return db.KeyDeleteAsync(SentinelKey(presentationRequestId));
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
