// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Credentials;

/// <summary>
/// View model for a presentation request in the wallet inbox.
/// </summary>
public class PresentationRequestViewModel
{
    public string RequestId { get; set; } = string.Empty;
    public string VerifierIdentity { get; set; } = string.Empty;
    public string CredentialType { get; set; } = string.Empty;
    public List<string> RequestedClaims { get; set; } = new();
    public List<MatchingCredentialViewModel> MatchingCredentials { get; set; } = new();
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = "Pending";
    public string? Nonce { get; set; }

    /// <summary>
    /// Time remaining before the request expires.
    /// </summary>
    public TimeSpan TimeRemaining => ExpiresAt > DateTimeOffset.UtcNow
        ? ExpiresAt - DateTimeOffset.UtcNow
        : TimeSpan.Zero;

    public bool IsExpired => ExpiresAt <= DateTimeOffset.UtcNow;
}

/// <summary>
/// A credential that matches a presentation request's requirements.
/// </summary>
public class MatchingCredentialViewModel
{
    public string CredentialId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string IssuerDid { get; set; } = string.Empty;
    public List<string> AvailableClaims { get; set; } = new();
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>
/// Result of submitting a presentation (approve/deny).
/// </summary>
public class PresentationSubmitResult
{
    public bool Success { get; set; }
    public string? Status { get; set; }
    public string? ErrorMessage { get; set; }
}
