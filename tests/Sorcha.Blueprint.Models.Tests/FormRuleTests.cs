// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace Sorcha.Blueprint.Models.Tests;

public class FormRuleTests
{
    [Fact]
    public void FormRule_SerializesCorrectly()
    {
        var rule = new FormRule
        {
            Effect = RuleEffect.SHOW,
            Condition = new SchemaBasedCondition
            {
                Scope = "/approved",
                Schema = JsonNode.Parse("""{"const": false}""")
            }
        };

        var json = JsonSerializer.Serialize(rule);

        json.Should().Contain("\"effect\":\"SHOW\"");
        json.Should().Contain("\"scope\":\"/approved\"");
        json.Should().Contain("\"const\":false");
    }

    [Fact]
    public void FormRule_DeserializesCorrectly()
    {
        var json = """{"effect":"HIDE","condition":{"scope":"/status","schema":{"const":"draft"}}}""";

        var rule = JsonSerializer.Deserialize<FormRule>(json);

        rule.Should().NotBeNull();
        rule!.Effect.Should().Be(RuleEffect.HIDE);
        rule.Condition.Scope.Should().Be("/status");
        rule.Condition.Schema.Should().NotBeNull();
    }

    [Fact]
    public void FormRule_RoundTrip_PreservesValues()
    {
        var original = new FormRule
        {
            Effect = RuleEffect.ENABLE,
            Condition = new SchemaBasedCondition
            {
                Scope = "/amount",
                Schema = JsonNode.Parse("""{"type": "number", "minimum": 1}""")
            }
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<FormRule>(json);

        deserialized!.Effect.Should().Be(RuleEffect.ENABLE);
        deserialized.Condition.Scope.Should().Be("/amount");
    }

    [Fact]
    public void RuleEffect_AllValues_SerializeAsStrings()
    {
        JsonSerializer.Serialize(RuleEffect.SHOW).Should().Be("\"SHOW\"");
        JsonSerializer.Serialize(RuleEffect.HIDE).Should().Be("\"HIDE\"");
        JsonSerializer.Serialize(RuleEffect.ENABLE).Should().Be("\"ENABLE\"");
        JsonSerializer.Serialize(RuleEffect.DISABLE).Should().Be("\"DISABLE\"");
    }

    [Fact]
    public void RuleEffect_DeserializeFromString()
    {
        JsonSerializer.Deserialize<RuleEffect>("\"SHOW\"").Should().Be(RuleEffect.SHOW);
        JsonSerializer.Deserialize<RuleEffect>("\"HIDE\"").Should().Be(RuleEffect.HIDE);
        JsonSerializer.Deserialize<RuleEffect>("\"ENABLE\"").Should().Be(RuleEffect.ENABLE);
        JsonSerializer.Deserialize<RuleEffect>("\"DISABLE\"").Should().Be(RuleEffect.DISABLE);
    }

    [Fact]
    public void SchemaBasedCondition_SerializesCorrectly()
    {
        var condition = new SchemaBasedCondition
        {
            Scope = "/field",
            Schema = JsonNode.Parse("""{"enum": ["a", "b"]}""")
        };

        var json = JsonSerializer.Serialize(condition);

        json.Should().Contain("\"scope\":\"/field\"");
        json.Should().Contain("\"enum\"");
    }

    [Fact]
    public void Control_WithRule_SerializesCorrectly()
    {
        var control = new Control
        {
            ControlType = ControlTypes.TextArea,
            Title = "Rejection Reason",
            Scope = "/rejectionReason",
            Rule = new FormRule
            {
                Effect = RuleEffect.SHOW,
                Condition = new SchemaBasedCondition
                {
                    Scope = "/approved",
                    Schema = JsonNode.Parse("""{"const": false}""")
                }
            }
        };

        var json = JsonSerializer.Serialize(control);
        var deserialized = JsonSerializer.Deserialize<Control>(json);

        deserialized!.Rule.Should().NotBeNull();
        deserialized.Rule!.Effect.Should().Be(RuleEffect.SHOW);
        deserialized.Rule.Condition.Scope.Should().Be("/approved");
    }

    [Fact]
    public void Control_WithoutRule_DoesNotSerializeRuleProperty()
    {
        var control = new Control
        {
            ControlType = ControlTypes.TextLine,
            Title = "Name",
            Scope = "/name"
        };

        var json = JsonSerializer.Serialize(control);

        json.Should().NotContain("\"rule\"");
    }

    [Fact]
    public void Control_WithOptions_SerializesCorrectly()
    {
        var control = new Control
        {
            ControlType = ControlTypes.File,
            Title = "Upload",
            Scope = "/document",
            Options = JsonDocument.Parse("""{"maxFileSize": 10485760, "acceptedTypes": [".pdf", ".docx"]}""")
        };

        var json = JsonSerializer.Serialize(control);

        json.Should().Contain("\"options\"");
        json.Should().Contain("maxFileSize");
    }

    [Fact]
    public void Control_WithoutOptions_DoesNotSerializeOptionsProperty()
    {
        var control = new Control
        {
            ControlType = ControlTypes.TextLine,
            Title = "Name",
            Scope = "/name"
        };

        var json = JsonSerializer.Serialize(control);

        json.Should().NotContain("\"options\"");
    }

    [Fact]
    public void Control_DeprecatedConditions_StillDeserializes()
    {
        // Backward compatibility: old blueprints with conditions should still deserialize
        var json = """
        {
            "type": "TextLine",
            "title": "Name",
            "scope": "/name",
            "conditions": [{"==": [{"var": "approved"}, true]}]
        }
        """;

#pragma warning disable CS0612, CS0618 // Obsolete
        var control = JsonSerializer.Deserialize<Control>(json);

        control!.Conditions.Should().HaveCount(1);
#pragma warning restore CS0612, CS0618
    }
}
