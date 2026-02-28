// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Blueprint.Fluent;

/// <summary>
/// Fluent builder for creating JSON Schema definitions for action data
/// </summary>
public class DataSchemaBuilder
{
    private readonly Dictionary<string, object> _properties = new();
    private readonly List<string> _required = new();

    /// <summary>
    /// Adds a string field to the schema
    /// </summary>
    public DataSchemaBuilder AddString(string fieldId, Action<StringFieldBuilder>? configure = null)
    {
        var builder = new StringFieldBuilder();
        configure?.Invoke(builder);
        _properties[fieldId] = builder.BuildInternal();
        if (builder.IsRequiredField)
            _required.Add(fieldId);
        return this;
    }

    /// <summary>
    /// Adds a number field to the schema
    /// </summary>
    public DataSchemaBuilder AddNumber(string fieldId, Action<NumberFieldBuilder>? configure = null)
    {
        var builder = new NumberFieldBuilder();
        configure?.Invoke(builder);
        _properties[fieldId] = builder.BuildInternal();
        if (builder.IsRequiredField)
            _required.Add(fieldId);
        return this;
    }

    /// <summary>
    /// Adds an integer field to the schema
    /// </summary>
    public DataSchemaBuilder AddInteger(string fieldId, Action<IntegerFieldBuilder>? configure = null)
    {
        var builder = new IntegerFieldBuilder();
        configure?.Invoke(builder);
        _properties[fieldId] = builder.BuildInternal();
        if (builder.IsRequiredField)
            _required.Add(fieldId);
        return this;
    }

    /// <summary>
    /// Adds a boolean field to the schema
    /// </summary>
    public DataSchemaBuilder AddBoolean(string fieldId, Action<BooleanFieldBuilder>? configure = null)
    {
        var builder = new BooleanFieldBuilder();
        configure?.Invoke(builder);
        _properties[fieldId] = builder.BuildInternal();
        if (builder.IsRequiredField)
            _required.Add(fieldId);
        return this;
    }

    /// <summary>
    /// Adds a date field to the schema
    /// </summary>
    public DataSchemaBuilder AddDate(string fieldId, Action<DateFieldBuilder>? configure = null)
    {
        var builder = new DateFieldBuilder();
        configure?.Invoke(builder);
        _properties[fieldId] = builder.BuildInternal();
        if (builder.IsRequiredField)
            _required.Add(fieldId);
        return this;
    }

    /// <summary>
    /// Adds a file field to the schema
    /// </summary>
    public DataSchemaBuilder AddFile(string fieldId, Action<FileFieldBuilder>? configure = null)
    {
        var builder = new FileFieldBuilder();
        configure?.Invoke(builder);
        _properties[fieldId] = builder.BuildInternal();
        if (builder.IsRequiredField)
            _required.Add(fieldId);
        return this;
    }

    /// <summary>
    /// Adds an object field to the schema
    /// </summary>
    public DataSchemaBuilder AddObject(string fieldId, Action<ObjectFieldBuilder> configure)
    {
        var builder = new ObjectFieldBuilder();
        configure(builder);
        _properties[fieldId] = builder.BuildInternal();
        if (builder.IsRequiredField)
            _required.Add(fieldId);
        return this;
    }

    /// <summary>
    /// Adds an array field to the schema
    /// </summary>
    public DataSchemaBuilder AddArray(string fieldId, Action<ArrayFieldBuilder> configure)
    {
        var builder = new ArrayFieldBuilder();
        configure(builder);
        _properties[fieldId] = builder.BuildInternal();
        if (builder.IsRequiredField)
            _required.Add(fieldId);
        return this;
    }

    internal JsonDocument Build()
    {
        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = _properties
        };

        if (_required.Count > 0)
        {
            schema["required"] = _required;
        }

        return JsonSerializer.SerializeToDocument(schema);
    }
}
