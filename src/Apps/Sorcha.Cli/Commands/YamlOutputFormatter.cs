// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Sorcha.Cli.Commands;

/// <summary>
/// YAML output formatter.
/// </summary>
public class YamlOutputFormatter : IOutputFormatter
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .DisableAliases()
        .Build();

    /// <inheritdoc/>
    public string FormatSingle<T>(T data) where T : class
    {
        return Serializer.Serialize(data).TrimEnd();
    }

    /// <inheritdoc/>
    public string FormatCollection<T>(IEnumerable<T> data) where T : class
    {
        return Serializer.Serialize(data).TrimEnd();
    }

    /// <inheritdoc/>
    public string FormatMessage(string message)
    {
        return message;
    }
}
