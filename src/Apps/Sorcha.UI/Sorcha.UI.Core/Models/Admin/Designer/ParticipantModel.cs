// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models;

namespace Sorcha.UI.Core.Models.Designer;

/// <summary>
/// UI-friendly model for participant editing in the Blueprint Designer.
/// </summary>
public class ParticipantModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DisplayName { get; set; } = string.Empty;
    public string? WalletAddress { get; set; }
    public ParticipantRole Role { get; set; } = ParticipantRole.Member;
    public string? Description { get; set; }
    public bool IsNew { get; set; } = true;

    /// <summary>
    /// Validates that required fields are present.
    /// </summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(DisplayName);

    /// <summary>
    /// Organisation the participant belongs to.
    /// </summary>
    public string Organisation { get; set; } = string.Empty;

    /// <summary>
    /// Converts the UI model to the domain Participant model.
    /// </summary>
    public Participant ToParticipant() => new()
    {
        Id = Id,
        Name = DisplayName,
        WalletAddress = WalletAddress ?? string.Empty,
        Organisation = !string.IsNullOrEmpty(Organisation) ? Organisation : Role.ToString()
    };

    /// <summary>
    /// Creates a UI model from a domain Participant.
    /// </summary>
    public static ParticipantModel FromParticipant(Participant p) => new()
    {
        Id = p.Id,
        DisplayName = p.Name ?? string.Empty,
        WalletAddress = p.WalletAddress,
        Organisation = p.Organisation ?? string.Empty,
        Role = ParseRole(p.Organisation),
        IsNew = false
    };

    private static ParticipantRole ParseRole(string? organisation)
    {
        if (string.IsNullOrEmpty(organisation)) return ParticipantRole.Member;
        return Enum.TryParse<ParticipantRole>(organisation, ignoreCase: true, out var r) ? r : ParticipantRole.Member;
    }
}

/// <summary>
/// Roles that participants can have in a blueprint workflow.
/// </summary>
public enum ParticipantRole
{
    /// <summary>Initiates the workflow.</summary>
    Initiator,

    /// <summary>Approves or rejects actions.</summary>
    Approver,

    /// <summary>Can view but not modify workflow state.</summary>
    Observer,

    /// <summary>Standard workflow participant.</summary>
    Member,

    /// <summary>Full administrative access.</summary>
    Administrator
}
