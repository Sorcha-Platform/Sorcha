// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Haip.Service.Models;

/// <summary>
/// Represents a pending presentation request stored in Redis.
/// Created when a Blueprint action requires an external HAIP wallet presentation.
/// </summary>
public class PresentationRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Nonce { get; set; }
    public required string ClientId { get; set; }
    public required string ResponseUri { get; set; }
    public required string CredentialType { get; set; }
    public List<string>? RequiredClaims { get; set; }
    public List<string>? AcceptedIssuers { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public PresentationRequestState State { get; set; } = PresentationRequestState.Pending;
    public VerificationResult? Result { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PresentationRequestState
{
    Pending = 0,
    Submitted = 1,
    Verified = 2,
    Failed = 3,
    Expired = 4
}

/// <summary>
/// Result of verifying a HAIP presentation submission.
/// </summary>
public class VerificationResult
{
    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("verifiedClaims")]
    public Dictionary<string, object> VerifiedClaims { get; set; } = new();

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();

    [JsonPropertyName("holderKeyVerified")]
    public bool HolderKeyVerified { get; set; }

    [JsonPropertyName("x5cChainValid")]
    public bool? X5cChainValid { get; set; }

    [JsonPropertyName("statusCheckResult")]
    public string? StatusCheckResult { get; set; }

    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }
}
