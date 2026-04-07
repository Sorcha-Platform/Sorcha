// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Agent.Configuration;

namespace Sorcha.Agent.Tests.Configuration;

public class VariableResolverTests
{
    [Fact]
    public void Resolve_EnvVar_Present_ReplacesValue()
    {
        Environment.SetEnvironmentVariable("TEST_AGENT_VAR", "secret123");
        try
        {
            var result = VariableResolver.Resolve("{\"password\": \"$env:TEST_AGENT_VAR\"}");
            result.ResolvedJson.Should().Contain("secret123");
            result.HasUnresolved.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_AGENT_VAR", null);
        }
    }

    [Fact]
    public void Resolve_EnvVar_Missing_ReportsUnresolved()
    {
        Environment.SetEnvironmentVariable("TEST_AGENT_MISSING", null);
        var result = VariableResolver.Resolve("{\"password\": \"$env:TEST_AGENT_MISSING\"}");
        result.HasUnresolved.Should().BeTrue();
        result.UnresolvedVariables.Should().Contain("$env:TEST_AGENT_MISSING");
    }

    [Fact]
    public void Resolve_Placeholder_WithState_ReplacesValue()
    {
        var state = new Dictionary<string, string> { ["registerId"] = "reg-abc-123" };
        var result = VariableResolver.Resolve("{\"id\": \"{{registerId}}\"}", state);
        result.ResolvedJson.Should().Contain("reg-abc-123");
        result.HasUnresolved.Should().BeFalse();
    }

    [Fact]
    public void Resolve_Placeholder_NoState_ReportsUnresolved()
    {
        var result = VariableResolver.Resolve("{\"id\": \"{{registerId}}\"}");
        result.HasUnresolved.Should().BeTrue();
        result.UnresolvedVariables.Should().Contain("{{registerId}}");
    }

    [Fact]
    public void Resolve_Placeholder_MissingKey_ReportsUnresolved()
    {
        var state = new Dictionary<string, string> { ["other"] = "value" };
        var result = VariableResolver.Resolve("{\"id\": \"{{registerId}}\"}", state);
        result.HasUnresolved.Should().BeTrue();
    }

    [Fact]
    public void Resolve_MixedVars_ResolvesAll()
    {
        Environment.SetEnvironmentVariable("TEST_AGENT_PW", "pw123");
        try
        {
            var state = new Dictionary<string, string> { ["orgId"] = "org-456" };
            var json = "{\"password\": \"$env:TEST_AGENT_PW\", \"orgId\": \"{{orgId}}\"}";
            var result = VariableResolver.Resolve(json, state);
            result.ResolvedJson.Should().Contain("pw123");
            result.ResolvedJson.Should().Contain("org-456");
            result.HasUnresolved.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_AGENT_PW", null);
        }
    }

    [Fact]
    public void Resolve_NoVars_ReturnsUnchanged()
    {
        var json = "{\"name\": \"actor1\"}";
        var result = VariableResolver.Resolve(json);
        result.ResolvedJson.Should().Be(json);
        result.HasUnresolved.Should().BeFalse();
    }

    [Fact]
    public void LoadStateFile_NullPath_ReturnsNull()
    {
        var result = VariableResolver.LoadStateFile(null);
        result.Should().BeNull();
    }

    [Fact]
    public void LoadStateFile_NonExistent_ReturnsNull()
    {
        var result = VariableResolver.LoadStateFile("/nonexistent/path.json");
        result.Should().BeNull();
    }

    [Fact]
    public void LoadStateFile_ValidFile_ReturnsFlattenedValues()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "{\"registerId\": \"reg-1\", \"nested\": {\"value\": \"deep\"}}");
            var result = VariableResolver.LoadStateFile(tempFile);
            result.Should().NotBeNull();
            result!["registerId"].Should().Be("reg-1");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
