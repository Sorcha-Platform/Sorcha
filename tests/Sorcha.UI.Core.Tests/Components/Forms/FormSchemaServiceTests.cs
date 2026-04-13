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
    public void AutoGenerateForm_StringWithXAddressLookup_DispatchesToPostcodeLookup()
    {
        // Feature 103 US3: a string field carrying `x-address-lookup: true`
        // should be routed to the PostcodeLookup control type regardless of
        // format / maxLength. Siblings without the marker stay on TextLine.
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "line1":    { "type": "string", "title": "Address line 1" },
                "town":     { "type": "string", "title": "Town" },
                "postcode": { "type": "string", "title": "Postcode", "x-address-lookup": true },
                "country":  { "type": "string", "title": "Country" }
            }
        }
        """);

        var form = _sut.AutoGenerateForm(new[] { schema });

        var postcode = form.Elements.FirstOrDefault(e => e.Scope == "/postcode");
        postcode.Should().NotBeNull();
        postcode!.ControlType.Should().Be(Sorcha.Blueprint.Models.ControlTypes.PostcodeLookup);

        form.Elements.First(e => e.Scope == "/line1").ControlType.Should().Be(Sorcha.Blueprint.Models.ControlTypes.TextLine);
        form.Elements.First(e => e.Scope == "/town").ControlType.Should().Be(Sorcha.Blueprint.Models.ControlTypes.TextLine);
        form.Elements.First(e => e.Scope == "/country").ControlType.Should().Be(Sorcha.Blueprint.Models.ControlTypes.TextLine);
    }

    [Fact]
    public void AutoGenerateForm_XAddressLookupFalse_RemainsTextLine()
    {
        // Explicit `x-address-lookup: false` should NOT dispatch to postcode lookup.
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "postcode": { "type": "string", "x-address-lookup": false }
            }
        }
        """);

        var form = _sut.AutoGenerateForm(new[] { schema });
        form.Elements.First(e => e.Scope == "/postcode").ControlType
            .Should().Be(Sorcha.Blueprint.Models.ControlTypes.TextLine);
    }

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

    // --- Object recursion (Feature 103) ---

    [Fact]
    public void AutoGenerateForm_ObjectProperty_EmitsLayoutWithChildLeaves()
    {
        // Feature 103 core primitives are schema objects with nested
        // properties. The auto-generator must recurse and emit a Layout
        // control whose Elements are the nested leaves with compound
        // JSON Pointer scopes. A single TextLine for `/name` (the old
        // behaviour) is a rendering bug because the value is a nested
        // object, not a string.
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "name": {
                    "type": "object",
                    "title": "Personal Name",
                    "properties": {
                        "givenName": { "type": "string", "title": "Given Name" },
                        "familyName": { "type": "string", "title": "Family Name" }
                    },
                    "required": ["givenName", "familyName"]
                }
            }
        }
        """);

        var form = _sut.AutoGenerateForm(new[] { schema });

        var nameControl = form.Elements.Single();
        nameControl.Scope.Should().Be("/name");
        nameControl.ControlType.Should().Be(Sorcha.Blueprint.Models.ControlTypes.Layout);
        nameControl.Layout.Should().Be(Sorcha.Blueprint.Models.LayoutTypes.VerticalLayout);
        nameControl.Elements.Should().HaveCount(2);

        var given = nameControl.Elements[0];
        given.Scope.Should().Be("/name/givenName");
        given.ControlType.Should().Be(Sorcha.Blueprint.Models.ControlTypes.TextLine);
        given.Title.Should().Be("Given Name");

        var family = nameControl.Elements[1];
        family.Scope.Should().Be("/name/familyName");
        family.ControlType.Should().Be(Sorcha.Blueprint.Models.ControlTypes.TextLine);
    }

    [Fact]
    public void AutoGenerateForm_ObjectWithDateChild_EmitsDateTimeLeaf()
    {
        // DateOfBirth/v1 is modelled as an object wrapper around a single
        // dateOfBirth field. The wrapper must recurse and the inner date
        // field should dispatch to DateTime, not TextLine.
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "dob": {
                    "type": "object",
                    "properties": {
                        "dateOfBirth": { "type": "string", "format": "date", "title": "Date of Birth" }
                    }
                }
            }
        }
        """);

        var form = _sut.AutoGenerateForm(new[] { schema });

        var dob = form.Elements.Single();
        dob.Scope.Should().Be("/dob");
        dob.ControlType.Should().Be(Sorcha.Blueprint.Models.ControlTypes.Layout);
        var child = dob.Elements.Single();
        child.Scope.Should().Be("/dob/dateOfBirth");
        child.ControlType.Should().Be(Sorcha.Blueprint.Models.ControlTypes.DateTime);
    }

    [Fact]
    public void AutoGenerateForm_ObjectWithPostcodeChild_DispatchesToPostcodeLookup()
    {
        // PostalAddress/v1 style: the object wrapper carries nested
        // properties including a postcode with x-address-lookup.
        // Recursion must pass the x-address-lookup dispatch through
        // to the nested scope.
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "address": {
                    "type": "object",
                    "properties": {
                        "line1":    { "type": "string", "title": "Address line 1" },
                        "postcode": { "type": "string", "title": "Postcode", "x-address-lookup": true }
                    }
                }
            }
        }
        """);

        var form = _sut.AutoGenerateForm(new[] { schema });

        var address = form.Elements.Single();
        address.Scope.Should().Be("/address");
        var children = address.Elements;
        children.First(c => c.Scope == "/address/line1").ControlType
            .Should().Be(Sorcha.Blueprint.Models.ControlTypes.TextLine);
        children.First(c => c.Scope == "/address/postcode").ControlType
            .Should().Be(Sorcha.Blueprint.Models.ControlTypes.PostcodeLookup);
    }

    [Fact]
    public void AutoGenerateForm_ScalarProperty_ScopeHasLeadingSlash()
    {
        // Regression guard: the pre-wave-10 output format used
        // /<propertyName> for scalar leaves. Recursion changes added
        // a parentScope parameter; the leaf behaviour must match.
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "notes": { "type": "string", "title": "Notes" }
            }
        }
        """);

        var form = _sut.AutoGenerateForm(new[] { schema });
        form.Elements.Single().Scope.Should().Be("/notes");
    }

    // --- IsRequired nested path walking (Feature 103 wave 10) ---

    [Fact]
    public void IsRequired_NestedRequiredField_ReturnsTrue()
    {
        // PersonName/v1 declares `required: ["givenName", "familyName"]`
        // on its OWN schema, not on the root. IsRequired must walk the
        // JSON Pointer segment-by-segment and check the correct nested
        // required array.
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "name": {
                    "type": "object",
                    "properties": {
                        "givenName": { "type": "string" },
                        "middleName": { "type": "string" },
                        "familyName": { "type": "string" }
                    },
                    "required": ["givenName", "familyName"]
                }
            },
            "required": ["name"]
        }
        """);

        _sut.IsRequired(schema, "/name/givenName").Should().BeTrue();
        _sut.IsRequired(schema, "/name/familyName").Should().BeTrue();
    }

    [Fact]
    public void IsRequired_NestedOptionalField_ReturnsFalse()
    {
        // middleName is NOT in PersonName/v1's required array — it must
        // return false even though the root's `required: ["name"]` might
        // have been incorrectly inherited by pre-wave-10 code.
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "name": {
                    "type": "object",
                    "properties": {
                        "givenName": { "type": "string" },
                        "middleName": { "type": "string" }
                    },
                    "required": ["givenName"]
                }
            },
            "required": ["name"]
        }
        """);

        _sut.IsRequired(schema, "/name/middleName").Should().BeFalse();
    }

    [Fact]
    public void IsRequired_DeeplyNestedField_WalksAllSegments()
    {
        // Sanity check that the walker doesn't stop at depth 2.
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "outer": {
                    "type": "object",
                    "properties": {
                        "inner": {
                            "type": "object",
                            "properties": {
                                "leaf": { "type": "string" }
                            },
                            "required": ["leaf"]
                        }
                    }
                }
            }
        }
        """);

        _sut.IsRequired(schema, "/outer/inner/leaf").Should().BeTrue();
    }

    // --- ValidateData recursive validation (Feature 103 wave 10) ---

    [Fact]
    public void ValidateData_ObjectRequiredField_MissingNestedLeaf_ReturnsLeafError()
    {
        // Precise error location: the form data store uses compound
        // scopes (/name/givenName), so a missing nested required field
        // must surface at the nested scope, not the parent object.
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "name": {
                    "type": "object",
                    "properties": {
                        "givenName": { "type": "string", "title": "Given Name" },
                        "familyName": { "type": "string", "title": "Family Name" }
                    },
                    "required": ["givenName", "familyName"]
                }
            },
            "required": ["name"]
        }
        """);

        var data = new Dictionary<string, object?>
        {
            ["/name/givenName"] = "Alice"
            // /name/familyName intentionally absent
        };

        var errors = _sut.ValidateData(schema, data);

        errors.Should().ContainKey("/name/familyName");
        errors["/name/familyName"].Should().Contain(e => e.Contains("required"));
        // The top-level /name key must NOT appear as an error; recursion
        // produces the precise leaf error instead.
        errors.Should().NotContainKey("/name");
    }

    [Fact]
    public void ValidateData_ObjectRequiredField_AllLeavesPresent_ReturnsNoErrors()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "name": {
                    "type": "object",
                    "properties": {
                        "givenName": { "type": "string" },
                        "familyName": { "type": "string" }
                    },
                    "required": ["givenName", "familyName"]
                }
            },
            "required": ["name"]
        }
        """);

        var data = new Dictionary<string, object?>
        {
            ["/name/givenName"] = "Alice",
            ["/name/familyName"] = "O'Brien"
        };

        var errors = _sut.ValidateData(schema, data);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateData_TopLevelScalarRequired_StillDetectsMissing()
    {
        // Regression guard: the recursion refactor must not regress the
        // original flat-schema required check.
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "title": { "type": "string", "title": "Title" }
            },
            "required": ["title"]
        }
        """);

        var errors = _sut.ValidateData(schema, new Dictionary<string, object?>());

        errors.Should().ContainKey("/title");
        errors["/title"].Should().Contain(e => e.Contains("required"));
    }
}
