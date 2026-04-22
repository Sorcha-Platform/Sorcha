// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Agent.Persona;

namespace Sorcha.Agent.Tests.Persona;

public class PersonaDefinitionLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public PersonaDefinitionLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sorcha-persona-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
        GC.SuppressFinalize(this);
    }

    private string WritePersona(string json)
    {
        var path = Path.Combine(_tempDir, "test.persona.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Load_ValidOnceTrigger_ReturnsSuccess()
    {
        var path = WritePersona("""
            {
              "name": "kickoff",
              "target": { "blueprintId": "bp-1", "instanceId": "inst-1", "actionIndex": 0 },
              "trigger": { "kind": "once", "delaySeconds": 2 },
              "payloadTemplate": { "poReference": "PO-001" }
            }
            """);

        var result = PersonaDefinitionLoader.Load(path);

        result.IsSuccess.Should().BeTrue();
        result.Definition!.Name.Should().Be("kickoff");
        result.Definition.Trigger.Should().BeOfType<OnceTrigger>()
            .Which.DelaySeconds.Should().Be(2);
        result.Definition.Target.ActionIndex.Should().Be(0);
    }

    [Fact]
    public void Load_ValidIntervalTrigger_ReturnsSuccess()
    {
        var path = WritePersona("""
            {
              "name": "gen",
              "target": { "blueprintId": "bp-1", "instanceId": "inst-1", "actionIndex": 0 },
              "trigger": { "kind": "interval", "everySeconds": 30, "maxIterations": 5 },
              "payloadTemplate": { "amount": "${random.int(1,100)}" }
            }
            """);

        var result = PersonaDefinitionLoader.Load(path);
        result.IsSuccess.Should().BeTrue();
        var interval = result.Definition!.Trigger.Should().BeOfType<IntervalTrigger>().Subject;
        interval.EverySeconds.Should().Be(30);
        interval.MaxIterations.Should().Be(5);
        interval.IntervalSeconds.Should().Be(30);
    }

    [Fact]
    public void Load_MissingFile_ReturnsFailure()
    {
        var result = PersonaDefinitionLoader.Load(Path.Combine(_tempDir, "does-not-exist.json"));
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("not found");
    }

    [Fact]
    public void Load_TokenTypo_ReturnsFailureAtLoadTime()
    {
        var path = WritePersona("""
            {
              "name": "bad",
              "target": { "blueprintId": "bp-1", "instanceId": "inst-1", "actionIndex": 0 },
              "trigger": { "kind": "once" },
              "payloadTemplate": { "x": "${randm.int(1,2)}" }
            }
            """);

        var result = PersonaDefinitionLoader.Load(path);
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Unknown token"));
    }

    [Fact]
    public void Load_PersonaFileWithDollarSchemaKey_Accepted()
    {
        // Regression: $schema is an IDE-IntelliSense hint. The root schema declares
        // additionalProperties: false, so $schema must be explicitly permitted.
        var path = WritePersona("""
            {
              "$schema": "../../../specs/110-agent-persona-mode/contracts/persona-schema.json",
              "name": "ok",
              "target": { "blueprintId": "bp-1", "instanceId": "inst-1", "actionIndex": 0 },
              "trigger": { "kind": "once" },
              "payloadTemplate": { "x": 1 }
            }
            """);

        var result = PersonaDefinitionLoader.Load(path);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Load_InstanceIdWithSlash_Rejected()
    {
        // Regression: env-var-sourced IDs can contain path separators that would
        // silently rewrite the submission endpoint. Must be caught at load time.
        var path = WritePersona("""
            {
              "name": "bad",
              "target": { "blueprintId": "bp-1", "instanceId": "../other-instance/execute?x=", "actionIndex": 0 },
              "trigger": { "kind": "once" },
              "payloadTemplate": {}
            }
            """);

        var result = PersonaDefinitionLoader.Load(path);
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("instanceId") && e.Contains("disallowed characters"));
    }

    [Fact]
    public void Load_ActionNameOnly_RejectedAtLoadTime()
    {
        // v1 requires actionIndex. actionName is an additional property the schema
        // does not allow; the validator surfaces the failure before semantics run.
        var path = WritePersona("""
            {
              "name": "bad",
              "target": { "blueprintId": "bp-1", "instanceId": "inst-1", "actionName": "Raise PO" },
              "trigger": { "kind": "once" },
              "payloadTemplate": {}
            }
            """);

        var result = PersonaDefinitionLoader.Load(path);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Load_SchemaViolation_ReturnsFailure()
    {
        var path = WritePersona("""
            {
              "name": "bad",
              "target": { "blueprintId": "bp-1", "instanceId": "inst-1" },
              "trigger": { "kind": "once" },
              "payloadTemplate": {}
            }
            """);

        var result = PersonaDefinitionLoader.Load(path);
        result.IsSuccess.Should().BeFalse();
    }
}
