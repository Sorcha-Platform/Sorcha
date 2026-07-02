// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.UI.Core.Models.Forms;

/// <summary>
/// Central state object for the form renderer, passed as CascadingValue through the component tree.
/// Manages form data, validation, disclosure filtering, and calculated values.
/// </summary>
public class FormContext
{
    /// <summary>
    /// Current form values keyed by JSON Pointer scope (e.g., "/invoiceNumber")
    /// </summary>
    public Dictionary<string, object?> FormData { get; } = new();

    /// <summary>
    /// Merged JSON Schema from Action.DataSchemas
    /// </summary>
    public JsonDocument? DataSchema { get; set; }

    /// <summary>
    /// Allowed JSON Pointer paths for the current participant
    /// </summary>
    public HashSet<string> DisclosureFilter { get; set; } = [];

    /// <summary>
    /// Whether all fields are disclosed (wildcard or sender)
    /// </summary>
    public bool AllFieldsDisclosed { get; set; }

    /// <summary>
    /// Scope-keyed validation error messages
    /// </summary>
    public Dictionary<string, List<string>> ValidationErrors { get; } = new();

    /// <summary>
    /// Read-only data from prior actions
    /// </summary>
    public Dictionary<string, object?> PreviousData { get; set; } = new();

    /// <summary>
    /// JSON Logic computed values
    /// </summary>
    public Dictionary<string, object?> CalculatedValues { get; } = new();

    /// <summary>
    /// Selected credential presentations
    /// </summary>
    public List<CredentialPresentationInfo> CredentialPresentations { get; } = [];

    /// <summary>
    /// Whether all credential gate requirements have been satisfied.
    /// Set by CredentialGatePanel when user selects credentials for all requirements.
    /// True when there are no credential requirements or all are met.
    /// </summary>
    public bool CredentialGateSatisfied { get; set; } = true;

    /// <summary>
    /// Uploaded file attachments
    /// </summary>
    public List<FileAttachmentInfo> FileAttachments { get; } = [];

    /// <summary>
    /// ZKP proof attachments
    /// </summary>
    public List<ProofAttachment> ProofAttachments { get; } = [];

    /// <summary>
    /// Global read-only mode
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// Active wallet address for signing
    /// </summary>
    public string SigningWalletAddress { get; set; } = string.Empty;

    /// <summary>
    /// JSON Pointer scope of the currently focused field, or null when no
    /// field in the form has focus. Used by the contextual help panel
    /// (Feature 091 US2) to show field-level guidance.
    /// </summary>
    public string? FocusedFieldScope { get; private set; }

    /// <summary>
    /// Event invoked when any form value changes
    /// </summary>
    public event Action? OnDataChanged;

    /// <summary>
    /// Event invoked when validation errors change
    /// </summary>
    public event Action? OnValidationChanged;

    /// <summary>
    /// Event invoked when the focused field changes. The string argument is
    /// the new focused field's JSON Pointer scope, or null on blur.
    /// </summary>
    public event Action<string?>? OnFocusedFieldChanged;

    /// <summary>
    /// Updates the currently focused field scope and raises
    /// <see cref="OnFocusedFieldChanged"/>. Call with null on blur. Control
    /// renderers should invoke this from focus/blur handlers.
    /// </summary>
    public void SetFocusedField(string? scope)
    {
        if (FocusedFieldScope == scope)
            return;

        FocusedFieldScope = scope;
        OnFocusedFieldChanged?.Invoke(scope);
    }

    /// <summary>
    /// Sets a form value by scope and triggers data change
    /// </summary>
    public void SetValue(string scope, object? value)
    {
        FormData[scope] = value;
        OnDataChanged?.Invoke();
    }

    /// <summary>
    /// Gets a typed form value by scope
    /// </summary>
    public T? GetValue<T>(string scope)
    {
        if (!FormData.TryGetValue(scope, out var value) || value is null)
            return default;

        if (value is T typed)
            return typed;

        try
        {
            if (value is JsonElement jsonElement)
            {
                var json = jsonElement.GetRawText();
                return JsonSerializer.Deserialize<T>(json, JsonDefaults.Api);
            }

            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Checks if a scope is disclosed for the current participant
    /// </summary>
    public bool IsDisclosed(string scope)
    {
        if (AllFieldsDisclosed)
            return true;

        if (string.IsNullOrEmpty(scope))
            return true;

        // Exact match
        if (DisclosureFilter.Contains(scope))
            return true;

        // Parent wildcard match: /address/* matches /address/city
        var parts = scope.Split('/');
        for (int i = parts.Length - 1; i >= 1; i--)
        {
            var parentWildcard = string.Join('/', parts[..i]) + "/*";
            if (DisclosureFilter.Contains(parentWildcard))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets validation errors for a specific scope
    /// </summary>
    public List<string> GetErrors(string scope)
    {
        return ValidationErrors.TryGetValue(scope, out var errors) ? errors : [];
    }

    /// <summary>
    /// Sets validation errors for a scope and triggers validation change
    /// </summary>
    public void SetErrors(string scope, List<string> errors)
    {
        if (errors.Count == 0)
            ValidationErrors.Remove(scope);
        else
            ValidationErrors[scope] = errors;
        OnValidationChanged?.Invoke();
    }

    /// <summary>
    /// Clears all validation errors
    /// </summary>
    public void ClearErrors()
    {
        ValidationErrors.Clear();
        OnValidationChanged?.Invoke();
    }

    /// <summary>
    /// Returns true if the form has any validation errors
    /// </summary>
    public bool HasErrors => ValidationErrors.Count > 0;

    /// <summary>
    /// Notifies that data has changed (for external triggers like calculations)
    /// </summary>
    public void NotifyDataChanged() => OnDataChanged?.Invoke();
}

/// <summary>
/// Credential presentation info for the form context
/// </summary>
public class CredentialPresentationInfo
{
    public string RequirementType { get; set; } = string.Empty;
    public string CredentialId { get; set; } = string.Empty;
    public Dictionary<string, object> DisclosedClaims { get; set; } = new();
    public string RawPresentation { get; set; } = string.Empty;
    public string? KeyBindingProof { get; set; }
}
