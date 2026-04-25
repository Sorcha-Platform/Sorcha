-- SPDX-License-Identifier: MIT
-- Copyright (c) 2026 Sorcha Contributors
--
-- claim-and-release.lua
-- Contract reference for feature 113-storage-durability-audit.
--
-- Atomic claim operation for RedisVerifiedTransactionQueue. Walks the
-- claimed set for expired leases (returning them to available), then
-- claims up to N highest-priority transactions, marking each with a new
-- lease expiry.
--
-- Keys:
--   KEYS[1] = sorcha:vtq:{registerId}:available
--   KEYS[2] = sorcha:vtq:{registerId}:claimed
--   KEYS[3] = sorcha:vtq:{registerId}:payload
--
-- Args:
--   ARGV[1] = nowUnixMs               -- current timestamp
--   ARGV[2] = leaseExpiresAtUnixMs    -- nowUnixMs + leaseDurationMs
--   ARGV[3] = maxClaim                -- maximum number of transactions to claim
--
-- Returns: array of strings — the JSON payloads of claimed transactions,
-- in priority order. Empty if nothing was available.

local now = tonumber(ARGV[1])
local leaseExpiresAt = tonumber(ARGV[2])
local maxClaim = tonumber(ARGV[3])

-- Step 1: Walk claimed set for expired leases, return to available.
local expired = redis.call('ZRANGEBYSCORE', KEYS[2], '-inf', now)
for _, txId in ipairs(expired) do
    -- Read score from payload-derived priority. We stash it in the payload
    -- JSON; for simplicity, we re-score using current time as the FIFO
    -- tiebreaker (a released lease re-enters the pool at "now"). This
    -- means a chronically failing seal never starves newer transactions.
    local payload = redis.call('HGET', KEYS[3], txId)
    if payload then
        -- Re-score based on the payload's recorded priority.
        local priorityScore = tonumber(string.match(payload, '"priorityScore":(%d+)'))
        if priorityScore == nil then
            -- Fallback: append to current time so it's not lost.
            priorityScore = now
        end
        redis.call('ZADD', KEYS[1], priorityScore, txId)
        redis.call('ZREM', KEYS[2], txId)
    else
        -- Orphaned claim record (payload gone) — just drop it.
        redis.call('ZREM', KEYS[2], txId)
    end
end

-- Step 2: Claim up to maxClaim highest-priority transactions.
local toClaim = redis.call('ZRANGE', KEYS[1], 0, maxClaim - 1)
local results = {}
for _, txId in ipairs(toClaim) do
    redis.call('ZREM', KEYS[1], txId)
    redis.call('ZADD', KEYS[2], leaseExpiresAt, txId)
    local payload = redis.call('HGET', KEYS[3], txId)
    if payload then
        table.insert(results, payload)
    end
end

return results
