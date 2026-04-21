// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Components;

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

    private bool CanSave => Context.Blueprint != null && Context.IsDirty;

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

    private void OnSaveClicked()
    {
        // TODO(T018): wire save via IBlueprintApiService.SaveAsync and call Context.MarkClean().
    }

    private void OnLoadClicked()
    {
        // TODO(T018): wire load dialog and call Context.SetBlueprint(...).
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Context.Changed -= OnContextChanged;
    }
}
