// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// The single trust authority consulted by every credential-verification path (feature 135).
/// Evaluates a resolved issuer against a <see cref="TrustPolicy"/> — combining trust sources,
/// checking assurance level, and producing a pinnable <see cref="TrustEvidence"/> record.
/// Fail-closed by default.
/// </summary>
public interface ITrustEvaluator
{
    /// <summary>
    /// Evaluates the issuer described by <paramref name="issuer"/> against <paramref name="policy"/>.
    /// When <paramref name="policy"/> is null the default policy is applied (register/DID source at
    /// low assurance — FR-026).
    /// </summary>
    Task<TrustDecision> EvaluateAsync(
        IssuerContext issuer,
        TrustPolicy? policy,
        CancellationToken cancellationToken = default);
}
