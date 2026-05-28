// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Sorcha.Blueprint.Models.Forms;
using Sorcha.UI.Core.Services.Designer;
using BlueprintAction = Sorcha.Blueprint.Models.Action;

namespace Sorcha.UI.Web.Client.Pages.DesignerShell.Panes;

/// <summary>
/// Designer-preview pane (Feature 109 / US2). Hosts a single-action pager on
/// row 1 and a <c>SorchaFormRenderer</c> in preview mode on row 2. Cursor is
/// driven by <see cref="DesignerContext.ActiveActionId"/> — AI edits auto-focus
/// via <see cref="DesignerContext.ApplyAiUpdate"/>, pager clicks call
/// <see cref="DesignerContext.SetActiveActionManual"/>, and the Follow AI
/// toggle calls <see cref="DesignerContext.FollowAi"/>.
/// </summary>
public partial class FormPreviewPane : ComponentBase, IDisposable
{
    // Context is injected via the .razor file's @inject directive.
    private bool _preventDefaultOnKey;
    private FormAuthoringMode _editMode = FormAuthoringMode.Preview;

    private void HandleEditModeChanged(FormAuthoringMode mode)
    {
        _editMode = mode;
        StateHasChanged();
    }

    private void HandleLayoutChanged(BlueprintAction updatedAction)
    {
        var bp = Context.Blueprint;
        if (bp is null)
        {
            return;
        }
        Context.SetBlueprint(bp);
        StateHasChanged();
    }

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        Context.Changed += OnContextChanged;
    }

    private void OnContextChanged() => InvokeAsync(StateHasChanged);

    private List<BlueprintAction> GetActions()
    {
        return Context.Blueprint?.Actions?.ToList() ?? [];
    }

    private BlueprintAction? GetCurrentAction(List<BlueprintAction> actions)
    {
        if (actions.Count == 0)
        {
            return null;
        }
        if (string.IsNullOrEmpty(Context.ActiveActionId))
        {
            return actions[0];
        }
        return actions.FirstOrDefault(a => a.Id.ToString() == Context.ActiveActionId) ?? actions[0];
    }

    private bool CanGoPrevious(List<BlueprintAction> actions, BlueprintAction? current)
    {
        if (current is null)
        {
            return false;
        }
        return PreviewPagerLogic.Previous(actions, current.Id.ToString()) is not null;
    }

    private bool CanGoNext(List<BlueprintAction> actions, BlueprintAction? current)
    {
        if (current is null)
        {
            return false;
        }
        return PreviewPagerLogic.Next(actions, current.Id.ToString()) is not null;
    }

    private void HandlePrevious()
    {
        var actions = GetActions();
        var current = GetCurrentAction(actions);
        if (current is null)
        {
            return;
        }
        var target = PreviewPagerLogic.Previous(actions, current.Id.ToString());
        if (target is not null)
        {
            Context.SetActiveActionManual(target);
        }
    }

    private void HandleNext()
    {
        var actions = GetActions();
        var current = GetCurrentAction(actions);
        if (current is null)
        {
            return;
        }
        var target = PreviewPagerLogic.Next(actions, current.Id.ToString());
        if (target is not null)
        {
            Context.SetActiveActionManual(target);
        }
    }

    private void HandleJump(string? targetId)
    {
        if (string.IsNullOrEmpty(targetId))
        {
            return;
        }
        Context.SetActiveActionManual(targetId);
    }

    private void HandleFollowAiChanged(bool followAi)
    {
        // Switch reflects !IsManualCursor. The user can only meaningfully flip
        // from manual-off to follow-on; the reverse is a no-op so we never end
        // up in manual mode without an explicit pager/jump interaction.
        if (followAi && Context.IsManualCursor)
        {
            Context.FollowAi();
        }
    }

    private void HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "[")
        {
            _preventDefaultOnKey = true;
            HandlePrevious();
        }
        else if (e.Key == "]")
        {
            _preventDefaultOnKey = true;
            HandleNext();
        }
        else
        {
            _preventDefaultOnKey = false;
        }
    }

    private string ParticipantNameFor(string? senderId)
    {
        if (string.IsNullOrEmpty(senderId))
        {
            return string.Empty;
        }
        var participant = Context.Blueprint?.Participants?.FirstOrDefault(p => p.Id == senderId);
        return participant?.Name ?? senderId;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Context.Changed -= OnContextChanged;
    }
}
