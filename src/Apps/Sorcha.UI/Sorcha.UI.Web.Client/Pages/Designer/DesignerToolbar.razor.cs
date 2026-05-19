// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;
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

    private void OnLoadClicked()
    {
        // TODO(US1 follow-up): open LoadBlueprintDialog and call Context.SetBlueprint(...).
        Feedback.ShowInfo("Load dialog wiring is scheduled for a follow-up PR");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Context.Changed -= OnContextChanged;
    }
}
