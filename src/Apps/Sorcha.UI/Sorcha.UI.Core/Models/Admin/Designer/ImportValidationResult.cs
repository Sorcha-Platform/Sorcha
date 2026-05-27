// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Designer;

/// <summary>
/// Result of validating an imported blueprint file.
/// </summary>
public class ImportValidationResult
{
    /// <summary>Whether the import passed all validations.</summary>
    public bool IsValid { get; set; }

    /// <summary>The validated and parsed blueprint, if valid.</summary>
    public Blueprint.Models.Blueprint? Blueprint { get; set; }

    /// <summary>Validation errors that prevent import.</summary>
    public List<ImportValidationError> Errors { get; set; } = [];

    /// <summary>Non-blocking warnings about the import.</summary>
    public List<ImportValidationWarning> Warnings { get; set; } = [];

    /// <summary>Creates a successful validation result.</summary>
    public static ImportValidationResult Success(Blueprint.Models.Blueprint blueprint) => new()
    {
        IsValid = true,
        Blueprint = blueprint
    };

    /// <summary>Creates a failed validation result with errors.</summary>
    public static ImportValidationResult Failure(params ImportValidationError[] errors) => new()
    {
        IsValid = false,
        Errors = [..errors]
    };
}

/// <summary>
/// A validation error that prevents blueprint import.
/// </summary>
public class ImportValidationError
{
    /// <summary>Path within the file where the error occurred (JSON Pointer or line number).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Human-readable error message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>The type of validation error.</summary>
    public ImportErrorType Type { get; set; }

    public ImportValidationError() { }

    public ImportValidationError(string path, string message, ImportErrorType type)
    {
        Path = path;
        Message = message;
        Type = type;
    }
}

/// <summary>
/// Types of import validation errors.
/// </summary>
public enum ImportErrorType
{
    /// <summary>A required field is missing.</summary>
    MissingRequiredField,

    /// <summary>The file format is invalid (not valid JSON/YAML).</summary>
    InvalidFormat,

    /// <summary>A reference to another entity is invalid.</summary>
    InvalidReference,

    /// <summary>Circular dependencies detected in actions.</summary>
    CircularDependency,

    /// <summary>A referenced schema was not found.</summary>
    SchemaNotFound,

    /// <summary>The export format version is not supported.</summary>
    UnsupportedVersion
}

/// <summary>
/// A non-blocking warning about the import.
/// </summary>
public class ImportValidationWarning
{
    /// <summary>Path within the file where the warning relates to.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Human-readable warning message.</summary>
    public string Message { get; set; } = string.Empty;

    public ImportValidationWarning() { }

    public ImportValidationWarning(string path, string message)
    {
        Path = path;
        Message = message;
    }
}
