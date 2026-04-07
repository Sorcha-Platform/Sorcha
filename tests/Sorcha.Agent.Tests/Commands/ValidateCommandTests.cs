// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Agent.Configuration;

namespace Sorcha.Agent.Tests.Commands;

public class ValidateCommandTests : IDisposable
{
    private readonly string _tempDir;

    public ValidateCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sorcha-validate-test-{Guid.NewGuid():N}");
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

    [Fact]
    public void LoadResult_ValidConfig_IsSuccess()
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
              "rules": [{ "actionName": "Submit", "decision": "approve" }]
            }
            """;
        var path = WriteConfig(json);
        var result = ActorDefinitionLoader.Load(path);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void LoadResult_MissingEnvVar_ReportsUnresolved()
    {
        Environment.SetEnvironmentVariable("TEST_VALIDATE_MISSING", null);
        var json = """
            {
              "actor": { "name": "test-actor" },
              "connection": {
                "gatewayUrl": "http://localhost",
                "registerId": "reg-1",
                "credentials": { "email": "test@example.com", "password": "$env:TEST_VALIDATE_MISSING", "organizationId": "org-1" },
                "walletAddress": "wallet-1"
              },
              "inbox": { "polling": { "enabled": true } },
              "mode": "rules",
              "rules": [{ "actionName": "Submit", "decision": "approve" }]
            }
            """;
        var path = WriteConfig(json);
        var result = ActorDefinitionLoader.Load(path);
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("*Unresolved*TEST_VALIDATE_MISSING*");
    }

    [Fact]
    public void LoadResult_UnresolvedPlaceholder_NoState_ReportsUnresolved()
    {
        var json = """
            {
              "actor": { "name": "test-actor" },
              "connection": {
                "gatewayUrl": "http://localhost",
                "registerId": "{{registerId}}",
                "credentials": { "email": "test@example.com", "password": "pass", "organizationId": "org-1" },
                "walletAddress": "wallet-1"
              },
              "inbox": { "polling": { "enabled": true } },
              "mode": "rules",
              "rules": [{ "actionName": "Submit", "decision": "approve" }]
            }
            """;
        var path = WriteConfig(json);
        var result = ActorDefinitionLoader.Load(path); // No state file
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("*Unresolved*registerId*");
    }

    [Fact]
    public void LoadResult_InvalidMode_ReportsError()
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
              "mode": "invalid"
            }
            """;
        var path = WriteConfig(json);
        var result = ActorDefinitionLoader.Load(path);
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("*mode must be*");
    }
}
