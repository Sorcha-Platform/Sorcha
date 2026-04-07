// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Agent.Configuration;

namespace Sorcha.Agent.Tests.Configuration;

public class ActorDefinitionLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public ActorDefinitionLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sorcha-agent-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
        GC.SuppressFinalize(this);
    }

    private string WriteConfig(string json)
    {
        var path = Path.Combine(_tempDir, "actor.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string ValidActorJson => """
        {
          "actor": { "name": "test-actor" },
          "connection": {
            "gatewayUrl": "http://localhost",
            "registerId": "reg-1",
            "credentials": { "email": "test@example.com", "password": "pass", "organizationId": "org-1" },
            "walletAddress": "wallet-1"
          },
          "inbox": { "polling": { "enabled": true, "intervalSeconds": 30 } },
          "mode": "rules",
          "rules": [
            { "actionName": "Submit", "decision": "approve", "payload": { "value": "ok" } }
          ]
        }
        """;

    [Fact]
    public void Load_ValidConfig_ReturnsSuccess()
    {
        var path = WriteConfig(ValidActorJson);
        var result = ActorDefinitionLoader.Load(path);
        result.IsSuccess.Should().BeTrue();
        result.Definition!.Actor.Name.Should().Be("test-actor");
        result.Definition.Mode.Should().Be("rules");
        result.Definition.Rules.Should().HaveCount(1);
    }

    [Fact]
    public void Load_MissingFile_ReturnsFailure()
    {
        var result = ActorDefinitionLoader.Load("/nonexistent/actor.json");
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("*not found*");
    }

    [Fact]
    public void Load_InvalidJson_ReturnsFailure()
    {
        var path = WriteConfig("{ invalid json }");
        var result = ActorDefinitionLoader.Load(path);
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("*Invalid JSON*");
    }

    [Fact]
    public void Load_RulesMode_NoRules_ReturnsFailure()
    {
        var json = """
            {
              "actor": { "name": "test-actor" },
              "connection": {
                "gatewayUrl": "http://localhost",
                "registerId": "reg-1",
                "credentials": { "email": "test@example.com", "password": "pass", "organizationId": "org-1" },
                "walletAddress": "wallet-1"
              },
              "inbox": { "polling": { "enabled": true } },
              "mode": "rules",
              "rules": []
            }
            """;
        var path = WriteConfig(json);
        var result = ActorDefinitionLoader.Load(path);
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("*no rules*");
    }

    [Fact]
    public void Load_NoInboxChannels_ReturnsFailure()
    {
        var json = ValidActorJson.Replace(
            "\"polling\": { \"enabled\": true, \"intervalSeconds\": 30 }",
            "\"polling\": { \"enabled\": false }");
        var path = WriteConfig(json);
        var result = ActorDefinitionLoader.Load(path);
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("*inbox channel*");
    }

    [Fact]
    public void Load_UnresolvedEnvVar_ReturnsFailure()
    {
        Environment.SetEnvironmentVariable("TEST_LOADER_UNRESOLVED", null);
        var json = ValidActorJson.Replace("\"pass\"", "\"$env:TEST_LOADER_UNRESOLVED\"");
        var path = WriteConfig(json);
        var result = ActorDefinitionLoader.Load(path);
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("*Unresolved*TEST_LOADER_UNRESOLVED*");
    }

    [Fact]
    public void Load_WithStateFile_ResolvesPlaceholders()
    {
        var json = ValidActorJson.Replace("\"reg-1\"", "\"{{registerId}}\"");
        var configPath = WriteConfig(json);
        var statePath = Path.Combine(_tempDir, "state.json");
        File.WriteAllText(statePath, "{\"registerId\": \"resolved-reg\"}");

        var result = ActorDefinitionLoader.Load(configPath, statePath);
        result.IsSuccess.Should().BeTrue();
        result.Definition!.Connection.RegisterId.Should().Be("resolved-reg");
    }

    [Fact]
    public void Load_InvalidMode_ReturnsFailure()
    {
        var json = ValidActorJson.Replace("\"rules\"", "\"invalid\"");
        var path = WriteConfig(json);
        var result = ActorDefinitionLoader.Load(path);
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("*mode must be*");
    }
}
