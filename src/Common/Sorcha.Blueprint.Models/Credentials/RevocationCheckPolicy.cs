// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Models.Credentials;

/// <summary>
/// Defines the policy for handling credential revocation checks when the
/// revocation registry is unreachable or returns an inconclusive result.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RevocationCheckPolicy
{
    /// <summary>
    /// Block the action until revocation status can be confirmed.
    /// This is the default and recommended setting for high-security scenarios.
    /// </summary>
    [JsonStringEnumMemberName("FailClosed")]
    FailClosed = 0,

    /// <summary>
    /// Allow the action to proceed with an audit warning recorded on the ledger.
    /// The credential is flagged as "revocation status unverified" in the transaction record.
    /// </summary>
    [JsonStringEnumMemberName("FailOpen")]
    FailOpen = 1
}
