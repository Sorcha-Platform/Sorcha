// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.PageObjects.WorkflowPages;

/// <summary>
/// Page object for submitting action data in a workflow instance.
/// Handles dynamic form rendering based on blueprint action schemas.
/// </summary>
public class ActionSubmissionPage
{
    private readonly IPage _page;

    public ActionSubmissionPage(IPage page)
    {
        _page = page;
    }

    // Page structure
    public ILocator PageTitle => _page.Locator("h1, h2, .mud-typography-h4, .mud-typography-h5").First;
    public ILocator ActionTitle => _page.Locator("[data-testid='action-title'], .action-title, .mud-card-header .mud-typography").First;
    public ILocator ActionForm => _page.Locator("form, .action-form, [data-testid='action-form']").First;

    // Form fields — dynamic, schema-driven
    public ILocator TextFields => _page.Locator(".mud-input-text input, .mud-text-field input");
    public ILocator SelectFields => _page.Locator(".mud-select");
    public ILocator DateFields => _page.Locator("input[type='date'], .mud-picker");

    // Buttons
    public ILocator SubmitButton => _page.Locator(
        "button:has-text('Submit'), button:has-text('Execute'), button:has-text('Send'), button[type='submit']").First;
    public ILocator CancelButton => _page.Locator("button:has-text('Cancel')").First;

    // Feedback
    public ILocator SuccessMessage => _page.Locator(".mud-alert-success, .mud-snackbar-success, .alert-success");
    public ILocator ErrorMessage => _page.Locator(".mud-alert-error, .mud-snackbar-error, .alert-danger");
    public ILocator LoadingIndicator => MudBlazorHelpers.CircularProgress(_page);

    // Action cards (for pending actions list)
    public ILocator ActionCards => _page.Locator(".mud-card, [data-testid='action-card']");
    public ILocator PendingActions => _page.Locator("[data-testid='pending-action'], .pending-action");

    /// <summary>
    /// Fills a text field by its label text.
    /// </summary>
    public async Task FillFieldByLabelAsync(string label, string value)
    {
        // MudBlazor text fields have labels as siblings or within the component
        var field = _page.Locator(
            $".mud-input-text:has(.mud-input-label:has-text('{label}')) input, " +
            $"label:has-text('{label}') + input, " +
            $"label:has-text('{label}') ~ input, " +
            $"[aria-label='{label}'], " +
            $"input[placeholder*='{label}']").First;

        if (await field.CountAsync() > 0)
        {
            await field.FillAsync(value);
            return;
        }

        // Fallback: find by MudTextField structure
        var mudField = _page.Locator(
            $".mud-text-field:has-text('{label}') input").First;
        if (await mudField.CountAsync() > 0)
        {
            await mudField.FillAsync(value);
            return;
        }

        throw new InvalidOperationException(
            $"Could not find form field with label '{label}'");
    }

    /// <summary>
    /// Selects a value from a MudSelect dropdown by label.
    /// </summary>
    public async Task SelectOptionByLabelAsync(string label, string value)
    {
        // Click the select to open dropdown
        var select = _page.Locator(
            $".mud-select:has(.mud-input-label:has-text('{label}')), " +
            $".mud-select:has-text('{label}')").First;

        if (await select.CountAsync() == 0)
        {
            throw new InvalidOperationException(
                $"Could not find select field with label '{label}'");
        }

        await select.ClickAsync();
        await _page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // Select the option from the popover
        var option = _page.Locator(
            $".mud-popover .mud-list-item:has-text('{value}')").First;
        await option.ClickAsync();
        await _page.WaitForTimeoutAsync(500);
    }

    /// <summary>
    /// Fills a date field by label.
    /// </summary>
    public async Task FillDateFieldAsync(string label, string dateValue)
    {
        // Try native date input first
        var dateInput = _page.Locator(
            $"label:has-text('{label}') + input[type='date'], " +
            $"label:has-text('{label}') ~ input[type='date']").First;

        if (await dateInput.CountAsync() > 0)
        {
            await dateInput.FillAsync(dateValue);
            return;
        }

        // Try MudDatePicker
        var mudPicker = _page.Locator(
            $".mud-picker:has-text('{label}') input").First;
        if (await mudPicker.CountAsync() > 0)
        {
            await mudPicker.FillAsync(dateValue);
            return;
        }

        // Fallback to text field
        await FillFieldByLabelAsync(label, dateValue);
    }

    /// <summary>
    /// Clicks the submit button and waits for the response.
    /// </summary>
    public async Task SubmitAsync()
    {
        await SubmitButton.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await _page.WaitForTimeoutAsync(TestConstants.ShortWait);
    }

    /// <summary>
    /// Checks if a success indicator is visible after submission.
    /// </summary>
    public async Task<bool> IsSuccessVisibleAsync()
    {
        return await SuccessMessage.CountAsync() > 0 && await SuccessMessage.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if an error indicator is visible after submission.
    /// </summary>
    public async Task<bool> IsErrorVisibleAsync()
    {
        return await ErrorMessage.CountAsync() > 0 && await ErrorMessage.IsVisibleAsync();
    }

    /// <summary>
    /// Gets the error message text, or null if no error is shown.
    /// </summary>
    public async Task<string?> GetErrorMessageAsync()
    {
        if (await ErrorMessage.CountAsync() > 0 && await ErrorMessage.IsVisibleAsync())
        {
            return await ErrorMessage.TextContentAsync();
        }
        return null;
    }

    /// <summary>
    /// Waits for the action form to be loaded and interactive.
    /// </summary>
    public async Task<bool> WaitForFormAsync(int timeout = 0)
    {
        timeout = timeout > 0 ? timeout : TestConstants.PageLoadTimeout;
        try
        {
            // Wait for either a form element or action cards
            await _page.Locator(
                "form, .action-form, [data-testid='action-form'], .mud-card")
                .First.WaitForAsync(new() { Timeout = timeout });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
