// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.UI.Core.Services.Forms;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms;

public class FormSchemaServiceTests
{
    private readonly FormSchemaService _sut = new();

    [Fact]
    public void MergeSchemas_SingleSchema_ReturnsSameDocument()
    {
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}}}""");

        var result = _sut.MergeSchemas([schema]);

        result.Should().NotBeNull();
        result!.RootElement.GetProperty("properties").GetProperty("name").GetProperty("type").GetString()
            .Should().Be("string");
    }

    [Fact]
    public void MergeSchemas_MultipleSchemas_CombinesProperties()
    {
        var schema1 = JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""");
        var schema2 = JsonDocument.Parse("""{"type":"object","properties":{"age":{"type":"integer"}},"required":["age"]}""");

        var result = _sut.MergeSchemas([schema1, schema2]);

        result.Should().NotBeNull();
        var props = result!.RootElement.GetProperty("properties");
        props.TryGetProperty("name", out _).Should().BeTrue();
        props.TryGetProperty("age", out _).Should().BeTrue();

        var required = result.RootElement.GetProperty("required");
        required.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void MergeSchemas_Null_ReturnsNull()
    {
        _sut.MergeSchemas(null).Should().BeNull();
    }

    [Fact]
    public void MergeSchemas_Empty_ReturnsNull()
    {
        _sut.MergeSchemas([]).Should().BeNull();
    }

    [Fact]
    public void GetSchemaForScope_ExistingProperty_ReturnsPropertySchema()
    {
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"amount":{"type":"number","minimum":0.01}}}""");

        var result = _sut.GetSchemaForScope(schema, "/amount");

        result.Should().NotBeNull();
        result!.Value.GetProperty("type").GetString().Should().Be("number");
        result!.Value.GetProperty("minimum").GetDecimal().Should().Be(0.01m);
    }

    [Fact]
    public void GetSchemaForScope_MissingProperty_ReturnsNull()
    {
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}}}""");

        _sut.GetSchemaForScope(schema, "/nonexistent").Should().BeNull();
    }

    [Fact]
    public void NormalizeScope_WithoutLeadingSlash_AddsSlash()
    {
        _sut.NormalizeScope("name").Should().Be("/name");
    }

    [Fact]
    public void NormalizeScope_WithLeadingSlash_Unchanged()
    {
        _sut.NormalizeScope("/name").Should().Be("/name");
    }

    [Fact]
    public void ValidateData_MissingRequired_ReturnsError()
    {
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""");

        var errors = _sut.ValidateData(schema, new Dictionary<string, object?>());

        errors.Should().ContainKey("/name");
        errors["/name"].Should().Contain(e => e.Contains("required"));
    }

    [Fact]
    public void ValidateData_AllPresent_ReturnsNoErrors()
    {
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""");

        var errors = _sut.ValidateData(schema, new Dictionary<string, object?> { ["/name"] = "John" });

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateField_MinLength_ReturnsError()
    {
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"code":{"type":"string","minLength":3}},"required":["code"]}""");

        var errors = _sut.ValidateField(schema, "/code", "AB");

        errors.Should().Contain(e => e.Contains("at least 3"));
    }

    [Fact]
    public void ValidateField_MinimumNumber_ReturnsError()
    {
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"amount":{"type":"number","minimum":0.01}}}""");

        var errors = _sut.ValidateField(schema, "/amount", -5m);

        errors.Should().Contain(e => e.Contains("at least 0.01"));
    }

    [Fact]
    public void ValidateField_RequiredEmpty_ReturnsError()
    {
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""");

        var errors = _sut.ValidateField(schema, "/name", "");

        errors.Should().Contain(e => e.Contains("required"));
    }

    [Fact]
    public void GetEnumValues_WithEnum_ReturnsList()
    {
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"currency":{"type":"string","enum":["USD","EUR","GBP"]}}}""");

        var values = _sut.GetEnumValues(schema, "/currency");

        values.Should().BeEquivalentTo(["USD", "EUR", "GBP"]);
    }

    [Fact]
    public void GetEnumValues_NoEnum_ReturnsEmpty()
    {
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}}}""");

        var values = _sut.GetEnumValues(schema, "/name");

        values.Should().BeEmpty();
    }

    [Fact]
    public void IsRequired_RequiredField_ReturnsTrue()
    {
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""");

        _sut.IsRequired(schema, "/name").Should().BeTrue();
    }

    [Fact]
    public void IsRequired_OptionalField_ReturnsFalse()
    {
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"notes":{"type":"string"}},"required":["name"]}""");

        _sut.IsRequired(schema, "/notes").Should().BeFalse();
    }

    // --- x-rule parsing (Feature 091) ---

    [Fact]
    public void AutoGenerateForm_PropertyWithXRule_PopulatesControlRule()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "hasMedicalCondition": { "type": "boolean" },
                "medicalConditionsList": {
                    "type": "string",
                    "x-rule": {
                        "effect": "SHOW",
                        "condition": {
                            "scope": "/hasMedicalCondition",
                            "schema": { "const": true }
                        }
                    }
                }
            }
        }
        """);

        var form = _sut.AutoGenerateForm(new[] { schema });

        var medicalControl = form.Elements.FirstOrDefault(e => e.Scope == "/medicalConditionsList");
        medicalControl.Should().NotBeNull();
        medicalControl!.Rule.Should().NotBeNull();
        medicalControl.Rule!.Effect.Should().Be(Sorcha.Blueprint.Models.RuleEffect.SHOW);
        medicalControl.Rule.Condition.Scope.Should().Be("/hasMedicalCondition");
        medicalControl.Rule.Condition.Schema.Should().NotBeNull();
    }

    [Fact]
    public void AutoGenerateForm_PropertyWithHideEffect_ParsesEffect()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "field1": {
                    "type": "string",
                    "x-rule": {
                        "effect": "HIDE",
                        "condition": {
                            "scope": "/toggle",
                            "schema": { "const": false }
                        }
                    }
                }
            }
        }
        """);

        var form = _sut.AutoGenerateForm(new[] { schema });
        var control = form.Elements.First();

        control.Rule.Should().NotBeNull();
        control.Rule!.Effect.Should().Be(Sorcha.Blueprint.Models.RuleEffect.HIDE);
    }

    [Fact]
    public void AutoGenerateForm_PropertyWithEnumCondition_ParsesEnumCondition()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "details": {
                    "type": "string",
                    "x-rule": {
                        "effect": "SHOW",
                        "condition": {
                            "scope": "/category",
                            "schema": { "enum": ["A", "B"] }
                        }
                    }
                }
            }
        }
        """);

        var form = _sut.AutoGenerateForm(new[] { schema });
        var control = form.Elements.First();

        control.Rule.Should().NotBeNull();
        control.Rule!.Condition.Schema!.ToJsonString().Should().Contain("enum");
    }

    [Fact]
    public void AutoGenerateForm_PropertyWithoutXRule_LeavesRuleNull()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "name": { "type": "string" }
            }
        }
        """);

        var form = _sut.AutoGenerateForm(new[] { schema });
        form.Elements.First().Rule.Should().BeNull();
    }

    [Fact]
    public void AutoGenerateForm_MalformedXRule_ReturnsNullRuleWithoutThrowing()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "field1": {
                    "type": "string",
                    "x-rule": "not an object"
                }
            }
        }
        """);

        var form = _sut.AutoGenerateForm(new[] { schema });
        form.Elements.First().Rule.Should().BeNull();
    }

    [Fact]
    public void AutoGenerateForm_XRuleMissingCondition_ReturnsNullRule()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "field1": {
                    "type": "string",
                    "x-rule": {
                        "effect": "SHOW"
                    }
                }
            }
        }
        """);

        var form = _sut.AutoGenerateForm(new[] { schema });
        form.Elements.First().Rule.Should().BeNull();
    }

    [Fact]
    public void AutoGenerateForm_XRuleCaseInsensitiveEffect_Parses()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "field1": {
                    "type": "string",
                    "x-rule": {
                        "effect": "show",
                        "condition": {
                            "scope": "/other",
                            "schema": { "const": true }
                        }
                    }
                }
            }
        }
        """);

        var form = _sut.AutoGenerateForm(new[] { schema });
        form.Elements.First().Rule!.Effect.Should().Be(Sorcha.Blueprint.Models.RuleEffect.SHOW);
    }
}
