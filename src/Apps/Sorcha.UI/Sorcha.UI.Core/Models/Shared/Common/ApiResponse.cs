// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.UI.Core.Models.Common;

/// <summary>
/// Generic API response wrapper
/// </summary>
/// <typeparam name="T">Response data type</typeparam>
public sealed record ApiResponse<T>
{
    /// <summary>
    /// Response data
    /// </summary>
    public T? Data { get; init; }

    /// <summary>
    /// Indicates if the request was successful
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message (if Success = false)
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Error code (if Success = false)
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Validation errors (if applicable)
    /// </summary>
    public Dictionary<string, string[]>? ValidationErrors { get; init; }

    /// <summary>
    /// Response timestamp
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a successful response
    /// </summary>
    public static ApiResponse<T> Ok(T data)
    {
        return new ApiResponse<T>
        {
            Data = data,
            Success = true
        };
    }

    /// <summary>
    /// Creates an error response
    /// </summary>
    public static ApiResponse<T> Error(string errorMessage, string? errorCode = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode
        };
    }

    /// <summary>
    /// Creates a validation error response
    /// </summary>
    public static ApiResponse<T> ValidationError(Dictionary<string, string[]> validationErrors)
    {
        return new ApiResponse<T>
        {
            Success = false,
            ErrorMessage = "Validation failed",
            ErrorCode = "VALIDATION_ERROR",
            ValidationErrors = validationErrors
        };
    }
}
