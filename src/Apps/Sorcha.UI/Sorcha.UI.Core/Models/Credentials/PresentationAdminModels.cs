// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Credentials;

/// <summary>
/// Request model for creating a verifiable presentation request (verifier side).
/// </summary>
public class CreatePresentationRequestViewModel
{
    public string CredentialType { get; set; } = string.Empty;
    public string[] AcceptedIssuers { get; set; } = [];
    public string[] RequiredClaims { get; set; } = [];
    public string CallbackUrl { get; set; } = string.Empty;
    public string? TargetWalletAddress { get; set; }
    public int TtlSeconds { get; set; } = 300;
    public string? VerifierIdentity { get; set; }
}

/// <summary>
/// Result returned after creating a presentation request, including QR code URL.
/// </summary>
public class PresentationRequestResultViewModel
{
    public string RequestId { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string QrCodeUrl { get; set; } = string.Empty;
    public string RequestUrl { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public object? VerificationResult { get; set; }

    public bool IsExpired => ExpiresAt <= DateTimeOffset.UtcNow;

    public TimeSpan TimeRemaining => ExpiresAt > DateTimeOffset.UtcNow
        ? ExpiresAt - DateTimeOffset.UtcNow
        : TimeSpan.Zero;
}
