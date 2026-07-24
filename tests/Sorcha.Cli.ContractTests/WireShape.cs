// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Cli.ContractTests;

/// <summary>
/// Computes the JSON property names a type actually serialises to, so a client DTO and the server
/// DTO it exchanges bodies with can be compared without constructing either.
/// </summary>
/// <remarks>
/// Comparing <i>names</i> rather than round-tripping instances matters: most of these records use
/// <c>required</c> members, so building a valid instance would mean inventing values for every
/// field of both sides. Names are also what actually breaks — a renamed property binds to nothing
/// and the receiver silently sees a default.
/// </remarks>
public static class WireShape
{
    /// <summary>
    /// The effective JSON names of a type's public instance properties: an explicit
    /// <see cref="JsonPropertyNameAttribute"/> wins, otherwise the camelCase policy the services
    /// serialise with.
    /// </summary>
    public static HashSet<string> Of(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var policy = JsonNamingPolicy.CamelCase;

        return type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            // Records synthesise EqualityContract; it is never serialised.
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                         ?? policy.ConvertName(p.Name))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Names present on <paramref name="a"/> but not <paramref name="b"/>.</summary>
    public static IReadOnlyList<string> OnlyOn(Type a, Type b) =>
        Of(a).Except(Of(b), StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

    /// <summary>A human-readable diff for assertion messages.</summary>
    public static string Describe(Type cli, Type server)
    {
        var cliOnly = OnlyOn(cli, server);
        var serverOnly = OnlyOn(server, cli);

        var lines = new List<string>();
        if (cliOnly.Count > 0)
        {
            lines.Add($"    CLI sends/expects, server does not have : {string.Join(", ", cliOnly)}");
        }

        if (serverOnly.Count > 0)
        {
            lines.Add($"    server has, CLI does not              : {string.Join(", ", serverOnly)}");
        }

        return lines.Count == 0
            ? "    (shapes agree)"
            : $"{cli.Name}  [CLI]  vs  {server.Name}  [{server.Assembly.GetName().Name}]\n" +
              string.Join("\n", lines);
    }
}
