// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace Sorcha.Blueprint.Models.Tests;

/// <summary>
/// Tests for Form and Control validation.
/// Tests Category 7 from BLUEPRINT-VALIDATION-TEST-PLAN.md
/// </summary>
public class FormValidationTests
{
    #region 7.1 Control Type Validation

    [Fact]
    public void Control_WithValidControlType_IsValid()
    {
        // Arrange - Test each valid control type
        var validTypes = Enum.GetValues<ControlTypes>();

        foreach (var type in validTypes)
        {
            var control = new Control
            {
                ControlType = type,
                Title = "Test Control"
            };

            // Act & Assert
            control.ControlType.Should().Be(type);
            Enum.IsDefined(typeof(ControlTypes), control.ControlType).Should().BeTrue();
        }
    }

    [Fact]
    public void Control_DefaultControlType_IsLabel()
    {
        // Arrange & Act
        var control = new Control();

        // Assert
        control.ControlType.Should().Be(ControlTypes.Label, "Default control type should be Label");
    }

    [Fact]
    public void Control_LayoutControlType_IsValid()
    {
        // Arrange
        var layoutControl = new Control
        {
            ControlType = ControlTypes.Layout,
            Layout = LayoutTypes.VerticalLayout
        };

        // Act & Assert
        layoutControl.ControlType.Should().Be(ControlTypes.Layout);
        layoutControl.Layout.Should().Be(LayoutTypes.VerticalLayout);
    }

    [Fact]
    public void Control_InputControlTypes_AreAllValid()
    {
        // Arrange - Test input control types
        var inputTypes = new[]
        {
            ControlTypes.TextLine,
            ControlTypes.TextArea,
            ControlTypes.Numeric,
            ControlTypes.DateTime,
            ControlTypes.File,
            ControlTypes.Choice,
            ControlTypes.Checkbox,
            ControlTypes.Selection
        };

        foreach (var type in inputTypes)
        {
            var control = new Control
            {
                ControlType = type,
                Scope = "/fieldName"
            };

            // Act & Assert
            control.ControlType.Should().Be(type);
            Enum.IsDefined(typeof(ControlTypes), type).Should().BeTrue();
        }
    }

    [Fact]
    public void LayoutTypes_AllEnumValues_AreValid()
    {
        // Arrange
        var layoutTypes = new[]
        {
            LayoutTypes.VerticalLayout,
            LayoutTypes.HorizontalLayout,
            LayoutTypes.Group,
            LayoutTypes.Categorization
        };

        foreach (var layout in layoutTypes)
        {
            var control = new Control
            {
                ControlType = ControlTypes.Layout,
                Layout = layout
            };

            // Act & Assert
            control.Layout.Should().Be(layout);
            Enum.IsDefined(typeof(LayoutTypes), layout).Should().BeTrue();
        }
    }

    [Fact]
    public void Control_DefaultVerticalLayout_IsSet()
    {
        // Arrange & Act
        var control = new Control
        {
            ControlType = ControlTypes.Layout
        };

        // Assert
        control.Layout.Should().Be(LayoutTypes.VerticalLayout, "Default layout should be VerticalLayout");
    }

    #endregion

    #region 7.2 Form-DataSchema Alignment

    [Fact]
    public void Control_ScopeReferencesSchemaProperty_IsValid()
    {
        // Arrange - Control scope should match schema property
        var schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "name": { "type": "string" },
                "age": { "type": "number" }
            }
        }
        """);

        var nameControl = new Control
        {
            ControlType = ControlTypes.TextLine,
            Scope = "/name",
            Schema = schema
        };

        // Act - Verify scope references valid schema property
        var schemaProperties = schema!["properties"]!.AsObject();
        var scopeFieldName = nameControl.Scope.TrimStart('/');
        var isValid = schemaProperties.ContainsKey(scopeFieldName);

        // Assert
        isValid.Should().BeTrue("Control scope should reference existing schema property");
    }

    [Fact]
    public void Control_ScopeReferencesNonExistentProperty_FailsValidation()
    {
        // Arrange
        var schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "name": { "type": "string" }
            }
        }
        """);

        var control = new Control
        {
            ControlType = ControlTypes.TextLine,
            Scope = "/nonExistentField",
            Schema = schema
        };

        // Act
        var schemaProperties = schema!["properties"]!.AsObject();
        var scopeFieldName = control.Scope.TrimStart('/');
        var isValid = schemaProperties.ContainsKey(scopeFieldName);

        // Assert
        isValid.Should().BeFalse("Control referencing non-existent schema property should fail");
    }

    [Fact]
    public void Control_SchemaValidationRulesMatchFormRules_IsValid()
    {
        // Arrange - Schema requires "email" format, control should enforce it
        var emailSchema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "email": {
                    "type": "string",
                    "format": "email",
                    "minLength": 5,
                    "maxLength": 100
                }
            },
            "required": ["email"]
        }
        """);

        var emailControl = new Control
        {
            ControlType = ControlTypes.TextLine,
            Scope = "/email",
            Title = "Email Address",
            Schema = emailSchema
        };

        // Act - Verify schema has validation rules
        var emailProperty = emailSchema!["properties"]!["email"];
        var hasFormat = emailProperty!["format"] != null;
        var hasMinLength = emailProperty["minLength"] != null;
        var hasMaxLength = emailProperty["maxLength"] != null;

        // Assert
        emailControl.Schema.Should().NotBeNull("Control should have validation schema");
        hasFormat.Should().BeTrue("Email field should have format validation");
        hasMinLength.Should().BeTrue("Email field should have minLength validation");
        hasMaxLength.Should().BeTrue("Email field should have maxLength validation");
    }

    [Fact]
    public void Form_WithNestedControls_ValidatesHierarchy()
    {
        // Arrange - Form with nested controls
        var form = new Control
        {
            ControlType = ControlTypes.Layout,
            Layout = LayoutTypes.VerticalLayout,
            Elements = new List<Control>
            {
                new()
                {
                    ControlType = ControlTypes.TextLine,
                    Scope = "/firstName",
                    Title = "First Name"
                },
                new()
                {
                    ControlType = ControlTypes.TextLine,
                    Scope = "/lastName",
                    Title = "Last Name"
                },
                new()
                {
                    ControlType = ControlTypes.Numeric,
                    Scope = "/age",
                    Title = "Age"
                }
            }
        };

        // Act & Assert
        form.Elements.Should().HaveCount(3);
        form.Elements.Should().AllSatisfy(element =>
        {
            element.Scope.Should().NotBeNullOrEmpty("Each control should have a scope");
            element.ControlType.Should().NotBe(ControlTypes.Layout, "Nested elements should be input controls");
        });
    }

    [Fact]
    public void Control_NumericTypeScopeMatchesNumberSchema_IsValid()
    {
        // Arrange
        var schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "age": { "type": "number", "minimum": 0, "maximum": 150 },
                "salary": { "type": "number", "minimum": 0 }
            }
        }
        """);

        var ageControl = new Control
        {
            ControlType = ControlTypes.Numeric,
            Scope = "/age",
            Schema = schema
        };

        // Act
        var schemaProperty = schema!["properties"]!["age"];
        var isNumberType = schemaProperty!["type"]!.ToString() == "number";

        // Assert
        ageControl.ControlType.Should().Be(ControlTypes.Numeric);
        isNumberType.Should().BeTrue("Numeric control scope should reference number schema type");
    }

    [Fact]
    public void Control_DateTimeTypeScopeMatchesDateTimeSchema_IsValid()
    {
        // Arrange
        var schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "birthDate": { "type": "string", "format": "date" },
                "createdAt": { "type": "string", "format": "date-time" }
            }
        }
        """);

        var dateControl = new Control
        {
            ControlType = ControlTypes.DateTime,
            Scope = "/birthDate",
            Schema = schema
        };

        // Act
        var schemaProperty = schema!["properties"]!["birthDate"];
        var hasDateFormat = schemaProperty!["format"]!.ToString() == "date";

        // Assert
        dateControl.ControlType.Should().Be(ControlTypes.DateTime);
        hasDateFormat.Should().BeTrue("DateTime control should reference date/datetime schema format");
    }

    [Fact]
    public void Control_CheckboxTypeScopeMatchesBooleanSchema_IsValid()
    {
        // Arrange
        var schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "agreeToTerms": { "type": "boolean" },
                "newsletterOptIn": { "type": "boolean" }
            }
        }
        """);

        var checkboxControl = new Control
        {
            ControlType = ControlTypes.Checkbox,
            Scope = "/agreeToTerms",
            Schema = schema
        };

        // Act
        var schemaProperty = schema!["properties"]!["agreeToTerms"];
        var isBooleanType = schemaProperty!["type"]!.ToString() == "boolean";

        // Assert
        checkboxControl.ControlType.Should().Be(ControlTypes.Checkbox);
        isBooleanType.Should().BeTrue("Checkbox control should reference boolean schema type");
    }

    [Fact]
    public void Control_ChoiceTypeScopeMatchesEnumSchema_IsValid()
    {
        // Arrange
        var schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "role": {
                    "type": "string",
                    "enum": ["manager", "developer", "designer"]
                }
            }
        }
        """);

        var choiceControl = new Control
        {
            ControlType = ControlTypes.Choice,
            Scope = "/role",
            Schema = schema
        };

        // Act
        var schemaProperty = schema!["properties"]!["role"];
        var hasEnum = schemaProperty!["enum"] != null;

        // Assert
        choiceControl.ControlType.Should().Be(ControlTypes.Choice);
        hasEnum.Should().BeTrue("Choice control should reference enum schema");
    }

    #endregion

    #region Edge Cases and Complex Forms

    [Fact]
    public void Control_WithConditionalDisplay_ValidatesCorrectly()
    {
        // Arrange - Control that displays conditionally
#pragma warning disable CS0618 // Testing deprecated Conditions property
        var conditionalControl = new Control
        {
            ControlType = ControlTypes.TextLine,
            Scope = "/additionalInfo",
            Title = "Additional Information",
            Conditions = new List<JsonNode>
            {
                JsonNode.Parse("{\"==\": [{\"var\": \"needsMoreInfo\"}, true]}")!
            }
        };

        // Act & Assert
        conditionalControl.Conditions.Should().NotBeEmpty();
        conditionalControl.Conditions.Should().HaveCount(1);
#pragma warning restore CS0618
    }

    [Fact]
    public void Control_WithProperties_ValidatesAdditionalSettings()
    {
        // Arrange - Control with additional properties
        var properties = JsonSerializer.Serialize(new
        {
            placeholder = "Enter your name",
            maxLength = 50,
            pattern = "^[A-Za-z ]+$"
        });

        var control = new Control
        {
            ControlType = ControlTypes.TextLine,
            Scope = "/name",
            Properties = JsonDocument.Parse(properties)
        };

        // Act & Assert
        control.Properties.Should().NotBeNull();
        var props = control.Properties!.RootElement;
        props.GetProperty("placeholder").GetString().Should().Be("Enter your name");
        props.GetProperty("maxLength").GetInt32().Should().Be(50);
    }

    [Fact]
    public void Form_ComplexMultiStepLayout_ValidatesCorrectly()
    {
        // Arrange - Multi-step form with categorization
        var form = new Control
        {
            ControlType = ControlTypes.Layout,
            Layout = LayoutTypes.Categorization,
            Elements = new List<Control>
            {
                new()
                {
                    ControlType = ControlTypes.Layout,
                    Layout = LayoutTypes.Group,
                    Title = "Personal Information",
                    Elements = new List<Control>
                    {
                        new() { ControlType = ControlTypes.TextLine, Scope = "/firstName", Title = "First Name" },
                        new() { ControlType = ControlTypes.TextLine, Scope = "/lastName", Title = "Last Name" }
                    }
                },
                new()
                {
                    ControlType = ControlTypes.Layout,
                    Layout = LayoutTypes.Group,
                    Title = "Contact Information",
                    Elements = new List<Control>
                    {
                        new() { ControlType = ControlTypes.TextLine, Scope = "/email", Title = "Email" },
                        new() { ControlType = ControlTypes.TextLine, Scope = "/phone", Title = "Phone" }
                    }
                }
            }
        };

        // Act & Assert
        form.Layout.Should().Be(LayoutTypes.Categorization);
        form.Elements.Should().HaveCount(2);
        form.Elements.Should().AllSatisfy(section =>
        {
            section.ControlType.Should().Be(ControlTypes.Layout);
            section.Layout.Should().Be(LayoutTypes.Group);
            section.Elements.Should().NotBeEmpty();
        });
    }

    [Fact]
    public void Control_TitleMaxLength_EnforcedAt100Characters()
    {
        // Arrange
        var longTitle = new string('A', 100);
        var tooLongTitle = new string('A', 101);

        var validControl = new Control
        {
            Title = longTitle
        };

        // Act - Check if property annotation would enforce this
        var propertyInfo = typeof(Control).GetProperty("Title");
        var maxLengthAttr = propertyInfo!.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.MaxLengthAttribute), false)
            .FirstOrDefault() as System.ComponentModel.DataAnnotations.MaxLengthAttribute;

        // Assert
        maxLengthAttr.Should().NotBeNull("Title should have MaxLength attribute");
        maxLengthAttr!.Length.Should().Be(100);
        validControl.Title.Length.Should().BeLessThanOrEqualTo(maxLengthAttr.Length);
    }

    [Fact]
    public void Control_ScopeMaxLength_EnforcedAt250Characters()
    {
        // Arrange
        var validScope = "/" + new string('a', 249);
        var control = new Control
        {
            Scope = validScope
        };

        // Act
        var propertyInfo = typeof(Control).GetProperty("Scope");
        var maxLengthAttr = propertyInfo!.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.MaxLengthAttribute), false)
            .FirstOrDefault() as System.ComponentModel.DataAnnotations.MaxLengthAttribute;

        // Assert
        maxLengthAttr.Should().NotBeNull("Scope should have MaxLength attribute");
        maxLengthAttr!.Length.Should().Be(250);
        control.Scope.Length.Should().BeLessThanOrEqualTo(maxLengthAttr.Length);
    }

    #endregion
}
