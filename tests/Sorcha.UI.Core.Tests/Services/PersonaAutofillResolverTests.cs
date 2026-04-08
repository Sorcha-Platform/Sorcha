// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Tenant.Models.Persona;
using Sorcha.UI.Core.Models.Forms;
using Sorcha.UI.Core.Services.Forms;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Unit tests for <see cref="PersonaAutofillResolver"/>. Feature 092 / US1 / T035.
/// </summary>
public sealed class PersonaAutofillResolverTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static PersonaReadModelV1 SeededPersona() => new()
    {
        GivenName = new PersonaAttribute<string>(
            "Ada", PersonaAttributeSource.SelfAsserted, null, Now),
        FamilyName = new PersonaAttribute<string>(
            "Lovelace", PersonaAttributeSource.SelfAsserted, null, Now),
        FullName = new PersonaAttribute<string>(
            "Ada Lovelace", PersonaAttributeSource.SelfAsserted, null, Now),
        DateOfBirth = new PersonaAttribute<DateOnly>(
            new DateOnly(1815, 12, 10), PersonaAttributeSource.SelfAsserted, null, Now),
        DefaultEmail = new PersonaAttribute<PersonaEmail>(
            new PersonaEmail("ada@example.com", true, "Personal"),
            PersonaAttributeSource.SelfAsserted, null, Now),
        AllEmails =
        [
            new PersonaEmail("ada@example.com", true, "Personal"),
        ],
        DefaultPhone = new PersonaAttribute<PersonaPhone>(
            new PersonaPhone("+441234567890", true, "Mobile", PersonaPhoneKind.Mobile),
            PersonaAttributeSource.SelfAsserted, null, Now),
        AllPhones =
        [
            new PersonaPhone("+441234567890", true, "Mobile", PersonaPhoneKind.Mobile),
        ],
        DefaultAddress = new PersonaAttribute<PersonaAddress>(
            new PersonaAddress(
                "1 Analytical Way", null, "London", null, "SW1A 1AA", "GB", true, "Home"),
            PersonaAttributeSource.SelfAsserted, null, Now),
        AllAddresses =
        [
            new PersonaAddress(
                "1 Analytical Way", null, "London", null, "SW1A 1AA", "GB", true, "Home"),
        ],
        DefaultNationality = new PersonaAttribute<string>(
            "GB", PersonaAttributeSource.SelfAsserted, null, Now),
        AllNationalities = ["GB"],
    };

    private static JsonDocument Schema(string json) => JsonDocument.Parse(json);

    [Fact]
    public void Resolve_NullSchema_ReturnsEmpty()
    {
        var resolver = new PersonaAutofillResolver();
        resolver.Resolve(null, SeededPersona()).Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NullPersona_ReturnsEmpty()
    {
        var resolver = new PersonaAutofillResolver();
        using var schema = Schema("""{"type":"object","properties":{"email":{"type":"string","format":"email"}}}""");
        resolver.Resolve(schema, null).Should().BeEmpty();
    }

    [Fact]
    public void Resolve_EmptyPersona_ReturnsEmpty()
    {
        var resolver = new PersonaAutofillResolver();
        using var schema = Schema("""{"type":"object","properties":{"email":{"type":"string","format":"email"}}}""");
        resolver.Resolve(schema, new PersonaReadModelV1()).Should().BeEmpty();
    }

    [Fact]
    public void Resolve_ExplicitXPersonaString_Wins()
    {
        var resolver = new PersonaAutofillResolver();
        using var schema = Schema("""
            {
              "type":"object",
              "properties":{
                "applicantFirstName":{"type":"string","x-persona":"givenName"}
              }
            }
            """);

        var result = resolver.Resolve(schema, SeededPersona());

        result.Should().ContainKey("/applicantFirstName");
        var fill = result["/applicantFirstName"];
        fill.AttributeName.Should().Be("givenName");
        fill.Value.Should().Be("Ada");
        fill.MatchedBy.Should().Be(PersonaMatchMode.ExplicitExtension);
        fill.Source.Should().Be(PersonaAttributeSource.SelfAsserted);
    }

    [Fact]
    public void Resolve_ExplicitXPersonaObject_Wins()
    {
        var resolver = new PersonaAutofillResolver();
        using var schema = Schema("""
            {
              "type":"object",
              "properties":{
                "surname":{"type":"string","x-persona":{"attribute":"familyName"}}
              }
            }
            """);

        var result = resolver.Resolve(schema, SeededPersona());

        result.Should().ContainKey("/surname");
        result["/surname"].AttributeName.Should().Be("familyName");
        result["/surname"].Value.Should().Be("Lovelace");
    }

    [Fact]
    public void Resolve_XPersonaFalse_BlocksInference()
    {
        var resolver = new PersonaAutofillResolver();
        using var schema = Schema("""
            {
              "type":"object",
              "properties":{
                "nextOfKinEmail":{"type":"string","format":"email","x-persona":false}
              }
            }
            """);

        resolver.Resolve(schema, SeededPersona()).Should().NotContainKey("/nextOfKinEmail");
    }

    [Fact]
    public void Resolve_FormatEmail_InfersDefaultEmail()
    {
        var resolver = new PersonaAutofillResolver();
        using var schema = Schema("""
            {"type":"object","properties":{"email":{"type":"string","format":"email"}}}
            """);

        var result = resolver.Resolve(schema, SeededPersona());

        result.Should().ContainKey("/email");
        var fill = result["/email"];
        fill.AttributeName.Should().Be("defaultEmail");
        fill.Value.Should().Be("ada@example.com");
        fill.MatchedBy.Should().Be(PersonaMatchMode.Inference);
    }

    [Fact]
    public void Resolve_FormatTel_InfersDefaultPhone()
    {
        var resolver = new PersonaAutofillResolver();
        using var schema = Schema("""
            {"type":"object","properties":{"mobile":{"type":"string","format":"tel"}}}
            """);

        resolver.Resolve(schema, SeededPersona())["/mobile"].Value.Should().Be("+441234567890");
    }

    [Theory]
    [InlineData("dateOfBirth")]
    [InlineData("dob")]
    [InlineData("birthDate")]
    [InlineData("BIRTHDATE")]
    public void Resolve_DateOfBirthFieldAliases_InfersDob(string fieldName)
    {
        var resolver = new PersonaAutofillResolver();
        var json = "{\"type\":\"object\",\"properties\":{\"" + fieldName + "\":{\"type\":\"string\"}}}";
        using var schema = Schema(json);

        var result = resolver.Resolve(schema, SeededPersona());
        result.Should().ContainKey("/" + fieldName);
        result["/" + fieldName].AttributeName.Should().Be("dateOfBirth");
        result["/" + fieldName].Value.Should().BeOfType<DateOnly>();
    }

    [Fact]
    public void Resolve_PostalAddressObject_InfersDefaultAddress()
    {
        var resolver = new PersonaAutofillResolver();
        using var schema = Schema("""
            {
              "type":"object",
              "properties":{
                "address":{
                  "type":"object",
                  "properties":{
                    "line1":{"type":"string"},
                    "city":{"type":"string"},
                    "postalCode":{"type":"string"},
                    "country":{"type":"string"}
                  }
                }
              }
            }
            """);

        var result = resolver.Resolve(schema, SeededPersona());

        result.Should().ContainKey("/address");
        result["/address"].AttributeName.Should().Be("defaultAddress");
        result["/address"].Value.Should().BeOfType<PersonaAddress>();
    }

    [Fact]
    public void Resolve_NonAddressObject_RecursesIntoChildProperties()
    {
        var resolver = new PersonaAutofillResolver();
        using var schema = Schema("""
            {
              "type":"object",
              "properties":{
                "contact":{
                  "type":"object",
                  "properties":{
                    "email":{"type":"string","format":"email"}
                  }
                }
              }
            }
            """);

        var result = resolver.Resolve(schema, SeededPersona());

        result.Should().ContainKey("/contact/email");
        result["/contact/email"].AttributeName.Should().Be("defaultEmail");
    }

    [Fact]
    public void Resolve_SchemaDefault_BlocksPersona()
    {
        var resolver = new PersonaAutofillResolver();
        using var schema = Schema("""
            {
              "type":"object",
              "properties":{
                "email":{"type":"string","format":"email","default":"placeholder@example.com"}
              }
            }
            """);

        resolver.Resolve(schema, SeededPersona()).Should().NotContainKey("/email");
    }

    [Fact]
    public void Resolve_ArrayField_IsNotAutofilled()
    {
        var resolver = new PersonaAutofillResolver();
        using var schema = Schema("""
            {"type":"object","properties":{"aliases":{"type":"array","items":{"type":"string"}}}}
            """);

        resolver.Resolve(schema, SeededPersona()).Should().BeEmpty();
    }

    [Fact]
    public void Resolve_MalformedXPersona_IgnoredGracefully_FallsThroughToInference()
    {
        var resolver = new PersonaAutofillResolver();
        using var schema = Schema("""
            {
              "type":"object",
              "properties":{
                "email":{"type":"string","format":"email","x-persona":42}
              }
            }
            """);

        var result = resolver.Resolve(schema, SeededPersona());
        result.Should().ContainKey("/email");
        result["/email"].MatchedBy.Should().Be(PersonaMatchMode.Inference);
    }

    [Fact]
    public void Resolve_NonEmailStringField_IsNotInferred()
    {
        var resolver = new PersonaAutofillResolver();
        using var schema = Schema("""
            {"type":"object","properties":{"projectName":{"type":"string"}}}
            """);

        resolver.Resolve(schema, SeededPersona()).Should().BeEmpty();
    }
}
