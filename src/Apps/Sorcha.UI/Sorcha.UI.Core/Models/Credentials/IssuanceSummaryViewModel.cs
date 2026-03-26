// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Credentials;

/// <summary>
/// Summary of a credential issuance event shown after an action completes.
/// </summary>
public class IssuanceSummaryViewModel
{
    /// <summary>The credential type identifier.</summary>
    public required string CredentialType { get; init; }

    /// <summary>DID of the credential recipient.</summary>
    public required string IssuedToDid { get; init; }

    /// <summary>Display name of the credential recipient.</summary>
    public required string IssuedToName { get; init; }

    /// <summary>Name of the organisation that signed the credential.</summary>
    public required string SignedByOrg { get; init; }

    /// <summary>Name of the participant who processed the issuance action.</summary>
    public required string ProcessedByName { get; init; }

    /// <summary>Role of the participant who processed the issuance action.</summary>
    public required string ProcessedByRole { get; init; }

    /// <summary>Total number of claims in the credential.</summary>
    public required int TotalClaims { get; init; }

    /// <summary>Number of claims that can be selectively disclosed.</summary>
    public required int DisclosableClaims { get; init; }

    /// <summary>Human-readable usage policy (e.g. "Reusable", "Single-use").</summary>
    public required string UsagePolicy { get; init; }

    /// <summary>Optional expiry timestamp for the issued credential.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Name of the blueprint that drove the issuance.</summary>
    public required string BlueprintName { get; init; }

    /// <summary>Name of the specific action within the blueprint.</summary>
    public required string ActionName { get; init; }
}
