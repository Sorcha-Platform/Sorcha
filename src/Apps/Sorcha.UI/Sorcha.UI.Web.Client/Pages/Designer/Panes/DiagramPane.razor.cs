// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using Blazor.Diagrams.Options;
using Microsoft.AspNetCore.Components;
using Sorcha.UI.Core.Components.Designer;
using Sorcha.UI.Core.Models.Designer;
using Sorcha.UI.Core.Services.Designer;
using BlueprintAction = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.UI.Web.Client.Pages.DesignerShell.Panes;

/// <summary>
/// Blazor Diagrams canvas pane for the designer shell. Reads the current
/// blueprint from <see cref="DesignerContext"/>, mutates it in place on
/// node-drag / rename / add / delete, and marks the context dirty so the
/// shared toolbar's Save button activates.
/// </summary>
public partial class DiagramPane : ComponentBase, IDisposable
{
    private BlazorDiagram? _diagram;
    private string? _renderedBlueprintId;
    private bool _rebuildRequested;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        var options = new BlazorDiagramOptions
        {
            AllowMultiSelection = false
        };
        options.Zoom.Enabled = true;
        options.Links.DefaultColor = "#666666";

        _diagram = new BlazorDiagram(options);
        _diagram.RegisterComponent<ActionNodeModel, ActionNodeWidget>();
        _diagram.SelectionChanged += OnSelectionChanged;

        Context.Changed += OnContextChanged;

        if (Context.Blueprint is not null)
        {
            RebuildDiagramFromBlueprint();
        }
    }

    /// <inheritdoc />
    protected override void OnAfterRender(bool firstRender)
    {
        if (_rebuildRequested)
        {
            _rebuildRequested = false;
            RebuildDiagramFromBlueprint();
            StateHasChanged();
        }
    }

    private void OnContextChanged()
    {
        // If the blueprint identity changed, queue a canvas rebuild.
        if (Context.Blueprint?.Id != _renderedBlueprintId)
        {
            _rebuildRequested = true;
        }
        InvokeAsync(StateHasChanged);
    }

    private void RebuildDiagramFromBlueprint()
    {
        if (_diagram is null)
        {
            return;
        }

        _diagram.Nodes.Clear();
        _diagram.Links.Clear();

        var bp = Context.Blueprint;
        _renderedBlueprintId = bp?.Id;
        if (bp is null)
        {
            return;
        }

        var y = 100;
        foreach (var participant in bp.Participants)
        {
            var pnode = new ParticipantNodeModel(participant, new Point(100, y))
            {
                Title = participant.Name
            };
            pnode.AddPort(PortAlignment.Bottom);
            _diagram.Nodes.Add(pnode);
            y += 150;
        }

        y = 100;
        ActionNodeModel? prev = null;
        foreach (var action in bp.Actions)
        {
            var anode = new ActionNodeModel(action, new Point(400, y))
            {
                Title = BuildActionTitle(action)
            };
            anode.Locked = false;
            anode.AddPort(PortAlignment.Top);
            anode.AddPort(PortAlignment.Bottom);
            _diagram.Nodes.Add(anode);

            if (prev is not null)
            {
                var source = prev.Ports.FirstOrDefault(p => p.Alignment == PortAlignment.Bottom);
                var target = anode.Ports.FirstOrDefault(p => p.Alignment == PortAlignment.Top);
                if (source is not null && target is not null)
                {
                    _diagram.Links.Add(new LinkModel(source, target));
                }
            }
            prev = anode;
            y += 150;
        }
    }

    private void AddAction()
    {
        if (_diagram is null || Context.Blueprint is null)
        {
            return;
        }

        var nextId = Context.Blueprint.Actions.Count == 0
            ? 0
            : Context.Blueprint.Actions.Max(a => a.Id) + 1;

        var newAction = new BlueprintAction
        {
            Id = nextId,
            Title = $"Action {nextId}",
            Description = $"Description for action {nextId}",
            BlueprintId = Context.Blueprint.Id,
            Participants = [],
            Calculations = new Dictionary<string, JsonNode>(),
            Condition = JsonNode.Parse("{\"==\":[0,0]}")
        };

        var previousActionNode = _diagram.Nodes.OfType<ActionNodeModel>().LastOrDefault();
        var position = previousActionNode is null
            ? new Point(400, 200)
            : new Point(previousActionNode.Position.X, previousActionNode.Position.Y + 200);

        var node = new ActionNodeModel(newAction, position)
        {
            Title = BuildActionTitle(newAction)
        };
        node.Locked = false;
        node.AddPort(PortAlignment.Top);
        node.AddPort(PortAlignment.Bottom);
        _diagram.Nodes.Add(node);

        if (previousActionNode is not null)
        {
            var source = previousActionNode.Ports.FirstOrDefault(p => p.Alignment == PortAlignment.Bottom);
            var target = node.Ports.FirstOrDefault(p => p.Alignment == PortAlignment.Top);
            if (source is not null && target is not null)
            {
                _diagram.Links.Add(new LinkModel(source, target));
            }
        }

        Context.Blueprint.Actions.Add(newAction);
        Context.SetActiveActionManual(newAction.Id.ToString());
        Context.MarkDirty();
        // TODO(T017): revalidate once IBlueprintApiService exposes a blueprint-object
        // validation endpoint. The current interface only validates by persisted id.
    }

    private static string BuildActionTitle(BlueprintAction action)
    {
        var participantCount = action.Participants?.Count() ?? 0;
        var disclosureCount = action.Disclosures?.Count() ?? 0;
        var calculationCount = action.Calculations?.Count ?? 0;
        var dataSchemaCount = action.DataSchemas?.Count() ?? 0;
        return $"{action.Title}\nP:{participantCount} D:{disclosureCount} C:{calculationCount} DS:{dataSchemaCount}";
    }

    /// <summary>
    /// Writes the clicked action's id into <see cref="DesignerContext"/> so
    /// switching to the Preview pane lands on the node the designer was looking at.
    /// </summary>
    private void OnSelectionChanged(Blazor.Diagrams.Core.Models.Base.Model model)
    {
        if (model is ActionNodeModel node && node.Selected)
        {
            Context.SetActiveActionManual(node.Action.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_diagram is not null)
        {
            _diagram.SelectionChanged -= OnSelectionChanged;
        }
        Context.Changed -= OnContextChanged;
    }
}
