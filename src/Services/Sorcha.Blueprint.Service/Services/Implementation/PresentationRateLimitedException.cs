// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Thrown when a submitter wallet exceeds the per-wallet-per-register
/// presentation attempt quota (Feature 111, FR-011). Surface at the HTTP
/// boundary as 429 Too Many Requests with a Retry-After header.
/// </summary>
public sealed class PresentationRateLimitedException : Exception
{
    public PresentationRateLimitedException(TimeSpan? retryAfter)
        : base("Presentation attempt rate limit exceeded.")
    {
        RetryAfter = retryAfter;
    }

    /// <summary>Suggested Retry-After duration; null if unknown.</summary>
    public TimeSpan? RetryAfter { get; }
}
