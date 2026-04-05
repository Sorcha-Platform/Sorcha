// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Blueprint.Engine.Models;
using Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Engine.Validation;

/// <summary>
/// Validates <c>format: "file-reference"</c> fields in blueprint action data schemas.
/// </summary>
/// <remarks>
/// Used during schema evaluation to verify that file field declarations are well-formed
/// and that runtime file reference values conform to their schema constraints.
///
/// All methods are stateless and thread-safe.
/// </remarks>
public static class FileReferenceValidator
{
    private const string FileReferenceFormat = "file-reference";
    private const string HashPrefix = "sha256:";
    private const int Sha256HexLength = 64;
    private const int MinChunks = 1;

    private static readonly string[] RequiredValueFields =
    [
        "fileName",
        "contentType",
        "size",
        "hash",
        "salt",
        "chunkTransactionIds"
    ];

    /// <summary>
    /// Validates the schema definition of a single file field.
    /// </summary>
    /// <param name="fieldSchema">
    /// The <see cref="JsonElement"/> representing the JSON Schema property definition for the field.
    /// Must be an object with <c>"format": "file-reference"</c>.
    /// </param>
    /// <param name="fieldPath">
    /// The JSON Pointer path to the field (e.g. <c>"/properties/attachment"</c>).
    /// Used as the instance location in returned <see cref="ValidationError"/> objects.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Valid()"/> when the field declaration is well-formed;
    /// otherwise a failed result containing one or more <see cref="ValidationError"/> entries.
    /// </returns>
    public static ValidationResult ValidateFileFieldSchema(JsonElement fieldSchema, string fieldPath)
    {
        var errors = new List<ValidationError>();

        // Must be an object
        if (fieldSchema.ValueKind != JsonValueKind.Object)
        {
            return ValidationResult.Invalid(ValidationError.Create(fieldPath,
                "File field schema must be a JSON object."));
        }

        // Must have format: "file-reference"
        if (!fieldSchema.TryGetProperty("format", out var formatElement) ||
            formatElement.ValueKind != JsonValueKind.String ||
            formatElement.GetString() != FileReferenceFormat)
        {
            errors.Add(ValidationError.Create(fieldPath,
                $"File field schema must declare \"format\": \"{FileReferenceFormat}\"."));
        }

        // Validate x-file extension if present
        if (FileSchemaExtension.TryParseFromSchema(fieldSchema, out var xFile) && xFile is not null)
        {
            // accept must have at least one MIME type
            if (xFile.Accept.Length == 0)
            {
                errors.Add(ValidationError.Create(fieldPath,
                    "x-file.accept must contain at least one MIME type."));
            }

            // maxSizePerFile must parse and not exceed the platform limit
            if (!string.IsNullOrWhiteSpace(xFile.MaxSizePerFile))
            {
                try
                {
                    var maxBytes = FileSchemaExtension.ParseMaxSizeBytes(xFile.MaxSizePerFile);
                    if (maxBytes > FileSchemaExtension.PlatformMaxSizeBytes)
                    {
                        errors.Add(ValidationError.Create(fieldPath,
                            $"x-file.maxSizePerFile ({xFile.MaxSizePerFile}) exceeds the platform maximum of " +
                            $"{FileSchemaExtension.PlatformMaxSizeBytes / (1024 * 1024)}MB."));
                    }
                }
                catch (ArgumentException)
                {
                    errors.Add(ValidationError.Create(fieldPath,
                        $"x-file.maxSizePerFile value \"{xFile.MaxSizePerFile}\" could not be parsed. " +
                        "Expected a value ending with MB or KB, e.g. \"16MB\" or \"512KB\"."));
                }
            }

            // maxChunks must be between 1 and PlatformMaxChunks
            if (xFile.MaxChunks < MinChunks || xFile.MaxChunks > FileSchemaExtension.PlatformMaxChunks)
            {
                errors.Add(ValidationError.Create(fieldPath,
                    $"x-file.maxChunks must be between {MinChunks} and {FileSchemaExtension.PlatformMaxChunks} " +
                    $"(got {xFile.MaxChunks})."));
            }
        }

        return errors.Count == 0
            ? ValidationResult.Valid()
            : ValidationResult.Invalid(errors);
    }

    /// <summary>
    /// Validates a runtime file reference value contained in an action payload.
    /// </summary>
    /// <param name="value">
    /// The <see cref="JsonElement"/> representing the <c>FileReference</c> object in the action payload.
    /// </param>
    /// <param name="schemaExtension">
    /// The parsed <c>x-file</c> extension from the field schema, or <see langword="null"/> if
    /// no extension is present. When supplied, additional content-type, size, and chunk
    /// constraints are enforced.
    /// </param>
    /// <param name="fieldPath">
    /// The JSON Pointer path to the field in the payload (e.g. <c>"/attachment"</c>).
    /// Used as the instance location in returned <see cref="ValidationError"/> objects.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Valid()"/> when the value conforms to all constraints;
    /// otherwise a failed result containing one or more <see cref="ValidationError"/> entries.
    /// </returns>
    public static ValidationResult ValidateFileReferenceValue(
        JsonElement value,
        FileSchemaExtension? schemaExtension,
        string fieldPath)
    {
        var errors = new List<ValidationError>();

        if (value.ValueKind != JsonValueKind.Object)
        {
            return ValidationResult.Invalid(ValidationError.Create(fieldPath,
                "File reference value must be a JSON object."));
        }

        // Check all required fields are present
        foreach (var required in RequiredValueFields)
        {
            if (!value.TryGetProperty(required, out _))
            {
                errors.Add(ValidationError.Create(fieldPath,
                    $"File reference is missing required field \"{required}\"."));
            }
        }

        // Validate hash format: "sha256:" + 64 hex chars
        if (value.TryGetProperty("hash", out var hashElement) &&
            hashElement.ValueKind == JsonValueKind.String)
        {
            var hash = hashElement.GetString() ?? string.Empty;
            if (!IsValidSha256Hash(hash))
            {
                errors.Add(ValidationError.Create(fieldPath,
                    $"File reference hash must start with \"{HashPrefix}\" followed by " +
                    $"{Sha256HexLength} lowercase hexadecimal characters."));
            }
        }

        // Validate chunkTransactionIds has 1-10 items
        if (value.TryGetProperty("chunkTransactionIds", out var chunksElement) &&
            chunksElement.ValueKind == JsonValueKind.Array)
        {
            var chunkCount = chunksElement.GetArrayLength();
            if (chunkCount < MinChunks || chunkCount > FileSchemaExtension.PlatformMaxChunks)
            {
                errors.Add(ValidationError.Create(fieldPath,
                    $"chunkTransactionIds must contain between {MinChunks} and " +
                    $"{FileSchemaExtension.PlatformMaxChunks} items (got {chunkCount})."));
            }
        }

        // Validate size > 0
        long fileSize = 0;
        if (value.TryGetProperty("size", out var sizeElement) &&
            sizeElement.TryGetInt64(out fileSize))
        {
            if (fileSize <= 0)
            {
                errors.Add(ValidationError.Create(fieldPath,
                    "File reference size must be greater than zero."));
            }
        }

        // Platform-level size cap (always enforced)
        if (fileSize > FileSchemaExtension.PlatformMaxSizeBytes)
        {
            errors.Add(ValidationError.Create(fieldPath,
                $"File size ({fileSize} bytes) exceeds the platform maximum of " +
                $"{FileSchemaExtension.PlatformMaxSizeBytes / (1024 * 1024)}MB."));
        }

        // Schema extension constraints
        if (schemaExtension is not null)
        {
            // contentType must be in the accept list
            if (schemaExtension.Accept.Length > 0 &&
                value.TryGetProperty("contentType", out var contentTypeElement) &&
                contentTypeElement.ValueKind == JsonValueKind.String)
            {
                var contentType = contentTypeElement.GetString() ?? string.Empty;
                if (!schemaExtension.Accept.Contains(contentType, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add(ValidationError.Create(fieldPath,
                        $"Content type \"{contentType}\" is not permitted. " +
                        $"Allowed types: {string.Join(", ", schemaExtension.Accept)}."));
                }
            }

            // size must not exceed maxSizePerFile
            if (!string.IsNullOrWhiteSpace(schemaExtension.MaxSizePerFile) && fileSize > 0)
            {
                try
                {
                    var maxBytes = FileSchemaExtension.ParseMaxSizeBytes(schemaExtension.MaxSizePerFile);
                    if (fileSize > maxBytes)
                    {
                        errors.Add(ValidationError.Create(fieldPath,
                            $"File size ({fileSize} bytes) exceeds the schema maximum of " +
                            $"{schemaExtension.MaxSizePerFile} ({maxBytes} bytes)."));
                    }
                }
                catch (ArgumentException)
                {
                    // Malformed maxSizePerFile in schema; schema validation should have caught this.
                    // At runtime we skip the check rather than surface a confusing error.
                }
            }

            // chunkTransactionIds.Length must not exceed maxChunks
            if (value.TryGetProperty("chunkTransactionIds", out var schemaChunksElement) &&
                schemaChunksElement.ValueKind == JsonValueKind.Array)
            {
                var chunkCount = schemaChunksElement.GetArrayLength();
                if (chunkCount > schemaExtension.MaxChunks)
                {
                    errors.Add(ValidationError.Create(fieldPath,
                        $"chunkTransactionIds length ({chunkCount}) exceeds the schema maximum " +
                        $"of {schemaExtension.MaxChunks} chunks."));
                }
            }
        }

        return errors.Count == 0
            ? ValidationResult.Valid()
            : ValidationResult.Invalid(errors);
    }

    /// <summary>
    /// Validates the schema definition of an array-type file field whose items have
    /// <c>"format": "file-reference"</c>.
    /// </summary>
    /// <param name="arraySchema">
    /// The <see cref="JsonElement"/> representing the JSON Schema property definition for the array field.
    /// Must be an object with <c>"type": "array"</c> and an <c>"items"</c> sub-schema.
    /// </param>
    /// <param name="fieldPath">
    /// The JSON Pointer path to the array field (e.g. <c>"/properties/attachments"</c>).
    /// Used as the instance location in returned <see cref="ValidationError"/> objects.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Valid()"/> when the array field declaration is well-formed;
    /// otherwise a failed result containing one or more <see cref="ValidationError"/> entries.
    /// </returns>
    public static ValidationResult ValidateArrayFileField(JsonElement arraySchema, string fieldPath)
    {
        var errors = new List<ValidationError>();

        if (arraySchema.ValueKind != JsonValueKind.Object)
        {
            return ValidationResult.Invalid(ValidationError.Create(fieldPath,
                "Array file field schema must be a JSON object."));
        }

        // items must be present and have format "file-reference"
        if (!arraySchema.TryGetProperty("items", out var itemsElement))
        {
            errors.Add(ValidationError.Create(fieldPath,
                "Array file field schema must define an \"items\" sub-schema."));
        }
        else
        {
            var itemsPath = $"{fieldPath}/items";
            var itemResult = ValidateFileFieldSchema(itemsElement, itemsPath);
            if (!itemResult.IsValid)
            {
                errors.AddRange(itemResult.Errors);
            }
        }

        // minItems must be >= 0 if present
        int? minItems = null;
        if (arraySchema.TryGetProperty("minItems", out var minItemsElement) &&
            minItemsElement.TryGetInt32(out var minItemsValue))
        {
            if (minItemsValue < 0)
            {
                errors.Add(ValidationError.Create(fieldPath,
                    $"minItems must be greater than or equal to 0 (got {minItemsValue})."));
            }
            else
            {
                minItems = minItemsValue;
            }
        }

        // maxItems must be >= minItems if both are present
        if (arraySchema.TryGetProperty("maxItems", out var maxItemsElement) &&
            maxItemsElement.TryGetInt32(out var maxItemsValue))
        {
            if (minItems.HasValue && maxItemsValue < minItems.Value)
            {
                errors.Add(ValidationError.Create(fieldPath,
                    $"maxItems ({maxItemsValue}) must be greater than or equal to minItems ({minItems.Value})."));
            }
        }

        return errors.Count == 0
            ? ValidationResult.Valid()
            : ValidationResult.Invalid(errors);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static bool IsValidSha256Hash(string value)
    {
        if (!value.StartsWith(HashPrefix, StringComparison.Ordinal))
            return false;

        var hex = value.AsSpan(HashPrefix.Length);
        if (hex.Length != Sha256HexLength)
            return false;

        foreach (var ch in hex)
        {
            if (!Uri.IsHexDigit(ch))
                return false;
        }

        return true;
    }
}
