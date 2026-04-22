// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Agent.Persona;

namespace Sorcha.Agent.Tests.Persona;

public class PersonaSchemaValidatorTests
{
    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private const string ValidOnce = """
        {
          "name": "kickoff",
          "target": { "blueprintId": "bp-1", "instanceId": "inst-1", "actionIndex": 0 },
          "trigger": { "kind": "once" },
          "payloadTemplate": { "poReference": "PO-001" }
        }
        """;

    private const string ValidInterval = """
        {
          "name": "invoice-gen",
          "target": { "blueprintId": "bp-1", "instanceId": "inst-1", "actionName": "Raise Invoice" },
          "trigger": { "kind": "interval", "everySeconds": 30, "maxIterations": 20 },
          "payloadTemplate": { "amount": "${random.int(1,100)}" }
        }
        """;

    [Fact]
    public void Validate_ValidOnceTrigger_PassesSchema()
    {
        var errors = new PersonaSchemaValidator().Validate(Parse(ValidOnce));
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ValidIntervalTrigger_PassesSchema()
    {
        var errors = new PersonaSchemaValidator().Validate(Parse(ValidInterval));
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_MissingName_Fails()
    {
        var json = """
            {
              "target": { "blueprintId": "bp-1", "instanceId": "inst-1", "actionIndex": 0 },
              "trigger": { "kind": "once" },
              "payloadTemplate": {}
            }
            """;
        new PersonaSchemaValidator().Validate(Parse(json)).Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_TargetMissingBothActionNameAndIndex_Fails()
    {
        var json = """
            {
              "name": "bad",
              "target": { "blueprintId": "bp-1", "instanceId": "inst-1" },
              "trigger": { "kind": "once" },
              "payloadTemplate": {}
            }
            """;
        new PersonaSchemaValidator().Validate(Parse(json)).Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_IntervalWithBothEverySecondsAndMinutes_Fails()
    {
        var json = """
            {
              "name": "bad",
              "target": { "blueprintId": "bp-1", "instanceId": "inst-1", "actionIndex": 0 },
              "trigger": { "kind": "interval", "everySeconds": 30, "everyMinutes": 1 },
              "payloadTemplate": {}
            }
            """;
        new PersonaSchemaValidator().Validate(Parse(json)).Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_IntervalWithNeitherCadence_Fails()
    {
        var json = """
            {
              "name": "bad",
              "target": { "blueprintId": "bp-1", "instanceId": "inst-1", "actionIndex": 0 },
              "trigger": { "kind": "interval" },
              "payloadTemplate": {}
            }
            """;
        new PersonaSchemaValidator().Validate(Parse(json)).Should().NotBeEmpty();
    }
}
