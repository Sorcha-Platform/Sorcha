// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Sorcha.Blueprint.Models.Tests;

public class InstanceReferenceTemplateTests
{
    private static List<ValidationResult> Validate(object obj)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(obj, new ValidationContext(obj), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Constructor_ShouldInitializeWithDefaults()
    {
        var template = new InstanceReferenceTemplate();

        template.Prefix.Should().BeEmpty();
        template.Components.Should().NotBeNull().And.BeEmpty();
    }

    [Theory]
    [InlineData("CP")]
    [InlineData("A")]
    [InlineData("ABCDE")]
    public void Prefix_ValidValues_PassesValidation(string prefix)
    {
        var template = new InstanceReferenceTemplate
        {
            Prefix = prefix,
            Components = [new ReferenceComponent { Field = "/name", Transform = ReferenceTransform.FirstWord }]
        };

        Validate(template).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("cp")]
    [InlineData("ABCDEF")]
    [InlineData("CP1")]
    [InlineData("C-P")]
    public void Prefix_InvalidValues_FailsValidation(string prefix)
    {
        var template = new InstanceReferenceTemplate
        {
            Prefix = prefix,
            Components = [new ReferenceComponent { Field = "/name", Transform = ReferenceTransform.FirstWord }]
        };

        Validate(template).Should().NotBeEmpty();
    }

    [Fact]
    public void Components_EmptyList_FailsValidation()
    {
        var template = new InstanceReferenceTemplate
        {
            Prefix = "CP",
            Components = []
        };

        Validate(template).Should().Contain(r => r.ErrorMessage!.Contains("At least one"));
    }

    [Fact]
    public void Components_MaxFive_PassesValidation()
    {
        var template = new InstanceReferenceTemplate
        {
            Prefix = "CP",
            Components = Enumerable.Range(0, 5)
                .Select(i => new ReferenceComponent { Field = $"/field{i}", Transform = ReferenceTransform.Truncate })
                .ToList()
        };

        Validate(template).Should().BeEmpty();
    }

    [Fact]
    public void Components_SixItems_FailsValidation()
    {
        var template = new InstanceReferenceTemplate
        {
            Prefix = "CP",
            Components = Enumerable.Range(0, 6)
                .Select(i => new ReferenceComponent { Field = $"/field{i}", Transform = ReferenceTransform.Truncate })
                .ToList()
        };

        Validate(template).Should().Contain(r => r.ErrorMessage!.Contains("Maximum 5"));
    }

    [Fact]
    public void ReferenceComponent_DefaultChars_IsThree()
    {
        var component = new ReferenceComponent();
        component.Chars.Should().Be(3);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public void ReferenceComponent_ValidChars_PassesValidation(int chars)
    {
        var component = new ReferenceComponent
        {
            Field = "/name",
            Transform = ReferenceTransform.FirstWord,
            Chars = chars
        };

        Validate(component).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(11)]
    public void ReferenceComponent_InvalidChars_FailsValidation(int chars)
    {
        var component = new ReferenceComponent
        {
            Field = "/name",
            Transform = ReferenceTransform.FirstWord,
            Chars = chars
        };

        Validate(component).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("/projectName")]
    [InlineData("/address/city")]
    [InlineData("/a")]
    public void ReferenceComponent_ValidField_PassesValidation(string field)
    {
        var component = new ReferenceComponent
        {
            Field = field,
            Transform = ReferenceTransform.FirstWord
        };

        Validate(component).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("projectName")]
    public void ReferenceComponent_InvalidField_FailsValidation(string field)
    {
        var component = new ReferenceComponent
        {
            Field = field,
            Transform = ReferenceTransform.FirstWord
        };

        Validate(component).Should().NotBeEmpty();
    }

    [Fact]
    public void ReferenceTransform_AllValues_Defined()
    {
        Enum.GetValues<ReferenceTransform>().Should().HaveCount(3);
        Enum.GetValues<ReferenceTransform>().Should().Contain(ReferenceTransform.FirstWord);
        Enum.GetValues<ReferenceTransform>().Should().Contain(ReferenceTransform.Truncate);
        Enum.GetValues<ReferenceTransform>().Should().Contain(ReferenceTransform.Uppercase);
    }

    [Fact]
    public void JsonSerialization_RoundTrip_PreservesValues()
    {
        var template = new InstanceReferenceTemplate
        {
            Prefix = "CP",
            Components =
            [
                new ReferenceComponent { Field = "/projectName", Transform = ReferenceTransform.FirstWord, Chars = 3 },
                new ReferenceComponent { Field = "/siteAddress", Transform = ReferenceTransform.Truncate, Chars = 5 }
            ]
        };

        var json = JsonSerializer.Serialize(template);
        var deserialized = JsonSerializer.Deserialize<InstanceReferenceTemplate>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Prefix.Should().Be("CP");
        deserialized.Components.Should().HaveCount(2);
        deserialized.Components[0].Field.Should().Be("/projectName");
        deserialized.Components[0].Transform.Should().Be(ReferenceTransform.FirstWord);
        deserialized.Components[1].Transform.Should().Be(ReferenceTransform.Truncate);
        deserialized.Components[1].Chars.Should().Be(5);
    }

    [Fact]
    public void JsonDeserialization_FromBlueprintJson_ParsesCorrectly()
    {
        var blueprintJson = """
        {
            "id": "test-bp",
            "title": "Test Blueprint",
            "description": "A test blueprint with instance reference",
            "version": 1,
            "participants": [],
            "actions": [],
            "instanceReference": {
                "prefix": "TB",
                "components": [
                    { "field": "/name", "transform": "FirstWord", "chars": 3 }
                ]
            }
        }
        """;

        var blueprint = JsonSerializer.Deserialize<Blueprint>(blueprintJson);

        blueprint.Should().NotBeNull();
        blueprint!.InstanceReference.Should().NotBeNull();
        blueprint.InstanceReference!.Prefix.Should().Be("TB");
        blueprint.InstanceReference.Components.Should().HaveCount(1);
        blueprint.InstanceReference.Components[0].Transform.Should().Be(ReferenceTransform.FirstWord);
    }

    [Fact]
    public void JsonDeserialization_NullInstanceReference_ParsesAsNull()
    {
        var blueprintJson = """
        {
            "id": "test-bp",
            "title": "Test Blueprint",
            "description": "A test blueprint",
            "version": 1,
            "participants": [],
            "actions": []
        }
        """;

        var blueprint = JsonSerializer.Deserialize<Blueprint>(blueprintJson);

        blueprint.Should().NotBeNull();
        blueprint!.InstanceReference.Should().BeNull();
    }
}
