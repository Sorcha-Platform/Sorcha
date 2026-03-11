// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Sorcha.Tenant.Service.Pages.Auth;

/// <summary>
/// Server-rendered auth Error page model.
/// Displays a sanitized error message from the query string.
/// </summary>
public class ErrorModel : PageModel
{
    private const string DefaultMessage = "Something went wrong. Please try again.";

    /// <summary>
    /// The sanitized error message to display.
    /// </summary>
    public string Message { get; set; } = DefaultMessage;

    /// <summary>
    /// Handles GET requests — reads and sanitizes the optional message query param.
    /// </summary>
    /// <param name="message">Optional error message from the query string.</param>
    public void OnGet(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            // Sanitize: truncate to 500 chars and rely on Razor's automatic HTML encoding
            // to prevent XSS — never render as @Html.Raw
            Message = message.Length > 500 ? message[..500] : message;
        }
    }
}
