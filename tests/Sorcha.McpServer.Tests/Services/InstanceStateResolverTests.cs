// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Service.Models;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tests.Services;

/// <summary>
/// <see cref="InstanceStateResolver"/> mirrors <see cref="InstanceState"/> BY ORDINAL, which
/// nothing in the compiler can check. The mirror used to exist twice, byte-identically, in
/// WorkflowStatusTool and WorkflowInstancesTool — two copies of an unenforced ordinal table.
/// It now has one home, and this test is what enforces it: reordering, inserting or renaming an
/// InstanceState member fails here rather than silently mislabelling every instance an agent reads.
/// </summary>
public class InstanceStateResolverTests
{
    [Fact]
    public void StateNames_MirrorTheRealEnum_InDeclarationOrder()
    {
        InstanceStateResolver.StateNames.Should().Equal(
            Enum.GetNames<InstanceState>(),
            "the resolver indexes this array with the wire's integer state value");
    }

    [Theory]
    [InlineData(InstanceState.Active)]
    [InlineData(InstanceState.Completed)]
    [InlineData(InstanceState.Rejected)]
    [InlineData(InstanceState.TimedOut)]
    [InlineData(InstanceState.Cancelled)]
    public void Resolve_NumericWireValue_YieldsTheEnumMemberName(InstanceState state)
    {
        var element = JsonSerializer.Deserialize<JsonElement>(((int)state).ToString());

        InstanceStateResolver.Resolve(element).Should().Be(state.ToString());
    }

    [Fact]
    public void Resolve_StringWireValue_IsPassedThrough()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("\"Completed\"");

        InstanceStateResolver.Resolve(element).Should().Be("Completed");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("true")]
    public void Resolve_MissingOrUnrecognisedValue_IsUnknown(string? json)
    {
        JsonElement? element = json is null ? null : JsonSerializer.Deserialize<JsonElement>(json);

        InstanceStateResolver.Resolve(element).Should().Be("Unknown");
    }
}
