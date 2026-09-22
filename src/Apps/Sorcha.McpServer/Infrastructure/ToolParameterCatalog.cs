// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using ModelContextProtocol.Server;

namespace Sorcha.McpServer.Infrastructure;

/// <summary>
/// Reflects the tool methods in this assembly (the same ones <c>WithToolsFromAssembly()</c> wires
/// up) into a name → parameter-list lookup, so an argument-binding failure can name every
/// parameter the tool actually expects instead of sending the caller to make a second
/// <c>tools/list</c> call to find out (#1685(b)).
/// </summary>
/// <remarks>
/// Cold-start run #7: <c>sorcha_user_list</c>, <c>sorcha_wallet_info</c>,
/// <c>sorcha_workflow_status</c> and <c>sorcha_jsonlogic_test</c> each cost the agent a wasted,
/// sub-millisecond call because the parameter it guessed (<c>orgId</c>, <c>address</c>,
/// <c>instanceId</c>, <c>logic</c>/<c>data</c>) did not match the name the tool actually binds
/// (<c>organizationId</c>, <c>walletAddress</c>, <c>workflowInstanceId</c>,
/// <c>ruleJson</c>/<c>dataJson</c>). Renaming those parameters is a published-contract change and
/// is deliberately out of scope here (see the PR report); this makes the FIRST wrong guess
/// self-correcting instead of the second.
/// </remarks>
public static class ToolParameterCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<ParameterInfo>>> Catalog =
        new(() => Build(typeof(ToolParameterCatalog).Assembly));

    /// <summary>
    /// Returns a human-readable, comma-separated description of the named tool's parameters
    /// (name, type, and required/optional-with-default), or null when the tool name is not one
    /// this assembly declares.
    /// </summary>
    public static string? DescribeParameters(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return null;

        if (!Catalog.Value.TryGetValue(toolName, out var parameters))
            return null;

        return parameters.Count == 0
            ? "this tool takes no arguments"
            : string.Join(", ", parameters.Select(Describe));
    }

    /// <summary>
    /// Test-only entry point so the reflection can be exercised against an arbitrary assembly
    /// without depending on the full tool catalog.
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<ParameterInfo>> Build(Assembly assembly)
    {
        var map = new Dictionary<string, IReadOnlyList<ParameterInfo>>(StringComparer.Ordinal);

        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var toolAttribute = method.GetCustomAttribute<McpServerToolAttribute>();
                if (toolAttribute is null)
                    continue;

                var name = toolAttribute.Name ?? method.Name;

                // Excluded: the cancellation token is supplied by the SDK, never bound from the
                // caller's arguments, so listing it would tell an agent to pass something it can't.
                var parameters = method.GetParameters()
                    .Where(p => p.ParameterType != typeof(CancellationToken))
                    .ToArray();

                map[name] = parameters;
            }
        }

        return map;
    }

    private static string Describe(ParameterInfo parameter)
    {
        var typeName = FriendlyTypeName(parameter.ParameterType);

        if (!parameter.HasDefaultValue)
            return $"{parameter.Name} ({typeName}, required)";

        var defaultText = parameter.DefaultValue switch
        {
            null => "null",
            string s => $"\"{s}\"",
            bool b => b ? "true" : "false",
            var v => v.ToString()
        };

        return $"{parameter.Name} ({typeName}, optional, default {defaultText})";
    }

    private static string FriendlyTypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return FriendlyTypeName(underlying) + "?";

        if (type == typeof(string)) return "string";
        if (type == typeof(int)) return "int";
        if (type == typeof(long)) return "long";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(double)) return "double";

        return type.Name;
    }
}
