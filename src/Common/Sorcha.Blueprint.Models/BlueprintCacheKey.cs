// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Models;

/// <summary>
/// The one home for the validator blueprint-cache key format (Feature 194).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this type exists.</b> The format had two homes: the Validator Service's
/// <c>BlueprintCache.GetBlueprintKey</c> composed it from a configured prefix, and the Blueprint
/// Service's publish path wrote the same shape as a hardcoded interpolated string. Two projects, one
/// wire format, one of them a literal — and nothing related them.
/// </para>
/// <para>
/// <b>What an inconsistency costs.</b> If the reader and the writer disagree about the key, the
/// validator's every lookup misses, falls through to the fetcher, and resolves the blueprint's
/// <i>latest</i> definition — which is precisely the defect Feature 194 exists to remove. Nothing
/// throws, nothing logs an error, and the test suite stays green, because a cache miss is an
/// ordinary event. This is the same class of hazard as the derivation contexts (CLAUDE.md §15) and
/// the cross-boundary validation codes (§16): a value one project emits and another names.
/// </para>
/// <para>
/// <b>The hash is part of the key, deliberately.</b> An entry addressed by content is immutable, so
/// several definitions of one blueprint coexist and a pinned instance resolves its own. It also
/// means such an entry never needs invalidating — only evicting.
/// </para>
/// <para>
/// <b>On the prefix.</b> <see cref="DefaultPrefix"/> is the value both sides use. The Validator
/// Service exposes it as configuration; overriding it there without matching the publisher breaks
/// the join in exactly the silent way described above, so treat the knob as reserved for a
/// deployment that changes both.
/// </para>
/// </remarks>
public static class BlueprintCacheKey
{
    /// <summary>
    /// The canonical Redis key prefix for cached blueprint definitions.
    /// </summary>
    public const string DefaultPrefix = "sorcha:validator:blueprint:";

    /// <summary>
    /// Composes the cache key for one executable definition of a blueprint.
    /// </summary>
    /// <param name="blueprintId">The blueprint identifier.</param>
    /// <param name="execDefHash">
    /// The executable-definition hash — the pin. Required: an id-only key cannot distinguish two
    /// definitions of one blueprint, which is the whole point of the feature.
    /// </param>
    /// <param name="prefix">The key prefix. Defaults to <see cref="DefaultPrefix"/>.</param>
    /// <returns>The Redis key.</returns>
    public static string For(string blueprintId, string execDefHash, string prefix = DefaultPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);
        ArgumentException.ThrowIfNullOrWhiteSpace(execDefHash);

        return $"{prefix}{blueprintId}:{execDefHash}";
    }

    /// <summary>
    /// Composes the key-space pattern matching every cached definition of one blueprint, for
    /// eviction. Content-addressed entries do not go stale, so this is for removal, not refresh.
    /// </summary>
    /// <param name="blueprintId">The blueprint identifier.</param>
    /// <param name="prefix">The key prefix. Defaults to <see cref="DefaultPrefix"/>.</param>
    /// <returns>A Redis glob pattern.</returns>
    public static string AllVersionsPattern(string blueprintId, string prefix = DefaultPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);

        return $"{prefix}{blueprintId}:*";
    }

    /// <summary>
    /// Composes the per-register index key, which tracks which blueprints a register carries.
    /// </summary>
    /// <param name="registerId">The register identifier.</param>
    /// <param name="prefix">The key prefix. Defaults to <see cref="DefaultPrefix"/>.</param>
    /// <returns>The Redis key.</returns>
    public static string RegisterIndex(string registerId, string prefix = DefaultPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        return $"{prefix}index:{registerId}";
    }
}
