// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Sorcha.Blueprint.Models;
using BlueprintAction = Sorcha.Blueprint.Models.Action;

namespace Sorcha.UI.Core.Models.Designer;

/// <summary>
/// Base class for blueprint nodes
/// </summary>
public abstract class BlueprintNodeModel : NodeModel
{
    public string NodeId { get; set; } = Guid.NewGuid().ToString();
    public abstract string NodeType { get; }

    protected BlueprintNodeModel(Point? position = null) : base(position)
    {
    }
}

/// <summary>
/// Node representing a Participant
/// </summary>
public class ParticipantNodeModel : BlueprintNodeModel
{
    public Participant Participant { get; set; }
    public override string NodeType => "Participant";

    public ParticipantNodeModel(Participant participant, Point? position = null) : base(position)
    {
        Participant = participant;
        Title = participant.Name;
        NodeId = participant.Id;
    }

    public ParticipantNodeModel(Point? position = null) : base(position)
    {
        Participant = new Participant
        {
            Id = Guid.NewGuid().ToString(),
            Name = "New Participant",
            Organisation = "Organization",
            WalletAddress = string.Empty
        };
        Title = Participant.Name;
        NodeId = Participant.Id;
    }
}

/// <summary>
/// Node representing an Action
/// </summary>
public class ActionNodeModel : BlueprintNodeModel
{
    public BlueprintAction Action { get; set; }
    public override string NodeType => "Action";

    // Events for toolbar actions
    public static event Action<BlueprintAction>? AddParticipantRequested;
    public static event Action<BlueprintAction>? AddConditionRequested;
    public static event Action<BlueprintAction>? ShowPropertiesRequested;

    public ActionNodeModel(BlueprintAction action, Point? position = null) : base(position)
    {
        Action = action;
        Title = action.Title;
        NodeId = action.Id.ToString();
    }

    public ActionNodeModel(Point? position = null) : base(position)
    {
        Action = new BlueprintAction
        {
            Id = 0,
            Title = "New Action",
            Description = string.Empty,
            BlueprintId = string.Empty
        };
        Title = Action.Title;
    }

    // Methods to raise events
    public static void RaiseAddParticipant(BlueprintAction action) => AddParticipantRequested?.Invoke(action);
    public static void RaiseAddCondition(BlueprintAction action) => AddConditionRequested?.Invoke(action);
    public static void RaiseShowProperties(BlueprintAction action) => ShowPropertiesRequested?.Invoke(action);
}
