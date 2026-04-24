// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Renders a named email template pair (HTML + plaintext) against a strongly-typed
/// view model. Templates are pre-compiled at startup; rendering is a pure function.
/// </summary>
public interface IEmailTemplateRenderer
{
    /// <summary>
    /// Renders the template pair registered under <paramref name="templateName"/>
    /// against <paramref name="model"/>, returning both HTML and plaintext bodies.
    /// </summary>
    /// <param name="templateName">
    /// Template name without extension — one of: <c>verify</c>, <c>invite</c>,
    /// <c>reset</c>, <c>welcome-public</c>, <c>welcome-invited</c>.
    /// </param>
    /// <param name="model">
    /// Strongly-typed view model. Scriban resolves snake_case template field
    /// references against PascalCase .NET properties.
    /// </param>
    /// <returns>Rendered HTML body and plaintext body.</returns>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no template pair is registered under <paramref name="templateName"/>.
    /// </exception>
    (string Html, string Text) Render(string templateName, object model);
}
