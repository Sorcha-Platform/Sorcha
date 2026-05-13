// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Participants;

/// <summary>
/// Request to publish a participant to a register.
/// </summary>
public class PublishParticipantViewModel
{
    public Guid ParticipantId { get; set; }
    public string RegisterId { get; set; } = string.Empty;
    public List<PublishAddressViewModel> Addresses { get; set; } = [];
    public string SignerWalletAddress { get; set; } = string.Empty;
}

/// <summary>
/// Address to include in the published participant record.
/// </summary>
public class PublishAddressViewModel
{
    public string WalletAddress { get; set; } = string.Empty;
    public bool Primary { get; set; }
}

/// <summary>
/// Result of a publish operation.
/// </summary>
public class ParticipantPublishResultViewModel
{
    public string? TransactionId { get; set; }
    public string? RegisterId { get; set; }
    public Guid ParticipantId { get; set; }
    public int Version { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Published participant record for display in the UI.
/// </summary>
public class PublishedParticipantViewModel
{
    public string ParticipantId { get; set; } = string.Empty;
    public string ParticipantName { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public string RegisterId { get; set; } = string.Empty;
    public string RegisterName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Version { get; set; }
    public string LatestTxId { get; set; } = string.Empty;
    public int AddressCount { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
}
