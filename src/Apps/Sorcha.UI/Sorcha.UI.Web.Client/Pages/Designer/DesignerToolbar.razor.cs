// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Sorcha.UI.Core.Components.Designer;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.Feedback;

namespace Sorcha.UI.Web.Client.Pages.DesignerShell;

/// <summary>
/// Shared toolbar for the AI Designer unified shell. Renders the blueprint title,
/// dirty indicator, Load/Save/Export buttons and the validation popover. Subscribes
/// to <see cref="Sorcha.UI.Core.Services.Designer.DesignerContext.Changed"/> so changes
/// from any pane re-render the chrome.
/// </summary>
public partial class DesignerToolbar : ComponentBase, IDisposable
{
    private bool _validationPopoverOpen;
    private bool _saving;

    [Inject] private IBlueprintApiService BlueprintApi { get; set; } = default!;
    [Inject] private IInlineFeedback Feedback { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private ILogger<DesignerToolbar> Logger { get; set; } = default!;

    /// <summary>Inline-edit binding for the blueprint title; marks the context dirty on change.</summary>
    private string BlueprintTitle
    {
        get => Context.Blueprint?.Title ?? string.Empty;
        set
        {
            if (Context.Blueprint is null || Context.Blueprint.Title == value)
            {
                return;
            }
            Context.Blueprint.Title = value;
            Context.MarkDirty();
        }
    }

    private bool CanSave => Context.Blueprint != null && Context.IsDirty && !_saving;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        Context.Changed += OnContextChanged;
    }

    private void OnContextChanged() => InvokeAsync(StateHasChanged);

    private void OnTitleChanged(string newValue)
    {
        BlueprintTitle = newValue;
    }

    private async Task OnSaveClicked()
    {
        if (Context.Blueprint is null || _saving)
        {
            return;
        }

        _saving = true;
        try
        {
            Context.Blueprint.UpdatedAt = DateTimeOffset.UtcNow;

            Sorcha.UI.Core.Models.Blueprints.BlueprintListItemViewModel? result;
            if (string.IsNullOrEmpty(Context.Blueprint.Id))
            {
                result = await BlueprintApi.SaveBlueprintAsync(Context.Blueprint).ConfigureAwait(true);
                if (result is not null)
                {
                    Context.Blueprint.Id = result.Id;
                }
            }
            else
            {
                result = await BlueprintApi.UpdateBlueprintAsync(Context.Blueprint.Id, Context.Blueprint).ConfigureAwait(true);
            }

            if (result is not null)
            {
                Context.MarkClean();
                Feedback.ShowSuccess($"Blueprint '{Context.Blueprint.Title}' saved successfully");
            }
            else
            {
                Feedback.ShowWarning("Save failed — please try again");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to save blueprint");
            Feedback.ShowError($"Error saving blueprint: {ex.Message}", autoDismissMs: 0);
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>
    /// Opens the shared <see cref="LoadBlueprintDialog"/> (which lists the caller's drafts via
    /// <c>IBlueprintApiService.GetBlueprintsAsync</c>); on OK with a selected blueprint id, fetches
    /// the full <see cref="Sorcha.Blueprint.Models.Blueprint"/> and adopts it via
    /// <c>DesignerContext.SetBlueprint</c>. The context's exec-def hash recompute auto-re-locks Go
    /// live for a freshly-loaded draft whose hash has no recorded RehearsalPass (FR-023 / US6).
    /// </summary>
    private async Task OnLoadClicked()
    {
        try
        {
            var options = new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true };
            var dialog = await DialogService.ShowAsync<LoadBlueprintDialog>("Load Blueprint", options);
            var result = await dialog.Result;

            if (result is null || result.Canceled || result.Data is null)
            {
                return;
            }

            if (result.Data is not string selectedBlueprintId || string.IsNullOrWhiteSpace(selectedBlueprintId))
            {
                Logger.LogWarning("Load dialog returned a non-string result; ignoring.");
                return;
            }

            var bp = await BlueprintApi.GetBlueprintDetailAsync(selectedBlueprintId).ConfigureAwait(true);
            if (bp is null)
            {
                Feedback.ShowWarning("Could not load that blueprint — please try again.");
                return;
            }

            Context.SetBlueprint(bp);
            Feedback.ShowSuccess($"Loaded '{bp.Title}'");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to open Load Blueprint dialog");
            Feedback.ShowError($"Error opening Load dialog: {ex.Message}", autoDismissMs: 0);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Context.Changed -= OnContextChanged;
    }
}
