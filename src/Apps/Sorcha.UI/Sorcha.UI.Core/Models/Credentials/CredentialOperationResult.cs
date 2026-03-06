// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Credentials;

/// <summary>
/// Categorizes credential operation errors for UI display.
/// </summary>
public enum CredentialErrorType
{
    None,
    PermissionDenied,
    NotFound,
    Conflict,
    ServerError,
    NetworkError
}

/// <summary>
/// Result of a credential lifecycle operation with typed error information.
/// Enables the UI to display specific, actionable error messages (FR-024).
/// </summary>
public class CredentialOperationResult
{
    public bool Success { get; init; }
    public CredentialLifecycleResult? Data { get; init; }
    public CredentialErrorType ErrorType { get; init; }
    public string? ErrorMessage { get; init; }

    public static CredentialOperationResult Ok(CredentialLifecycleResult data) => new()
    {
        Success = true,
        Data = data
    };

    public static CredentialOperationResult Fail(CredentialErrorType errorType, string message) => new()
    {
        Success = false,
        ErrorType = errorType,
        ErrorMessage = message
    };

    /// <summary>
    /// Maps an HTTP status code to a typed error result with a user-facing message.
    /// </summary>
    public static CredentialOperationResult FromStatusCode(int statusCode, string? currentStatus = null) => statusCode switch
    {
        401 => Fail(CredentialErrorType.PermissionDenied,
            "Your session has expired. Please log in again."),
        403 => Fail(CredentialErrorType.PermissionDenied,
            "Permission denied: you are not the issuer of this credential"),
        404 => Fail(CredentialErrorType.NotFound,
            "Credential not found"),
        409 => Fail(CredentialErrorType.Conflict,
            string.IsNullOrEmpty(currentStatus)
                ? "Credential is already in the requested state"
                : $"Credential is already {currentStatus}"),
        _ => Fail(CredentialErrorType.ServerError,
            "An unexpected error occurred. Please try again.")
    };

    public static CredentialOperationResult NetworkError(string message) =>
        Fail(CredentialErrorType.NetworkError, $"Network error: {message}");
}
