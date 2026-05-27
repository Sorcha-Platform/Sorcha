// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Models.Credentials;

/// <summary>
/// The trust expectation attached to a <see cref="CredentialRequirement"/> (feature 135).
/// Replaces the removed flat accepted-issuer list. A credential is trusted when its
/// issuer is vouched for by the configured <see cref="Sources"/> per the
/// <see cref="Combinator"/>, at or above <see cref="MinAssuranceLevel"/>.
/// </summary>
public class TrustPolicy
{
    /// <summary>The trust sources consulted to vouch for an issuer (at least one when set).</summary>
    [JsonPropertyName("sources")]
    public IReadOnlyList<TrustSourceRef> Sources { get; set; } = [];

    /// <summary>How the <see cref="Sources"/> are combined. Default <see cref="TrustCombinator.AnyOf"/>.</summary>
    [JsonPropertyName("combinator")]
    public TrustCombinator Combinator { get; set; } = TrustCombinator.AnyOf;

    /// <summary>Minimum assurance level a credential must establish. Default <see cref="AssuranceLevel.Low"/>.</summary>
    [JsonPropertyName("minAssuranceLevel")]
    public AssuranceLevel MinAssuranceLevel { get; set; } = AssuranceLevel.Low;
}

/// <summary>
/// Helpers for reading a <see cref="TrustPolicy"/>. The transitional helpers here let
/// pre-evaluator call sites read the issuer allowlist while the full
/// <c>ITrustEvaluator</c> path is built out (feature 135).
/// </summary>
public static class TrustPolicyExtensions
{
    /// <summary>
    /// Returns the union of issuer DIDs declared on the policy's
    /// <see cref="TrustSourceKind.DidAllowlist"/> sources. Empty when the policy is
    /// null or declares no allowlist sources.
    /// </summary>
    public static IReadOnlyList<string> AllowedIssuerDids(this TrustPolicy? policy)
    {
        if (policy?.Sources is null || policy.Sources.Count == 0)
            return [];

        return policy.Sources
            .Where(s => s.Kind == TrustSourceKind.DidAllowlist && s.AllowedIssuers is { Count: > 0 })
            .SelectMany(s => s.AllowedIssuers!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Builds a default <see cref="TrustPolicy"/> from a set of legacy accepted-issuer
    /// identifiers: a single <see cref="TrustSourceKind.DidAllowlist"/> source when any
    /// are supplied, otherwise a single <see cref="TrustSourceKind.Register"/> source at
    /// <see cref="AssuranceLevel.Low"/> (feature 135, FR-026).
    /// </summary>
    public static TrustPolicy FromLegacyIssuers(IEnumerable<string>? legacyIssuers)
    {
        var issuers = legacyIssuers?.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal).ToArray();
        if (issuers is { Length: > 0 })
        {
            return new TrustPolicy
            {
                Sources = [new TrustSourceRef { Kind = TrustSourceKind.DidAllowlist, AllowedIssuers = issuers, ConfersAssurance = AssuranceLevel.Low }],
                Combinator = TrustCombinator.AnyOf,
                MinAssuranceLevel = AssuranceLevel.Low
            };
        }

        return new TrustPolicy
        {
            Sources = [new TrustSourceRef { Kind = TrustSourceKind.Register, ConfersAssurance = AssuranceLevel.Low }],
            Combinator = TrustCombinator.AnyOf,
            MinAssuranceLevel = AssuranceLevel.Low
        };
    }
}
