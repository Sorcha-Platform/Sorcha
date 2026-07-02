// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Json;
using Sorcha.Tenant.Models.Persona;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Extensions;

/// <summary>
/// Contract tests for <see cref="SorchaJson"/> — the single source of truth for the JSON wire format
/// shared by services (serialization) and UI clients (deserialization). Enums must serialize as
/// kebab-case strings, and reads must tolerate BOTH kebab strings and legacy numeric enum values so a
/// client using these options is compatible with every service regardless of how it emits enums.
/// </summary>
public sealed class SorchaJsonTests
{
    private sealed record Sample(PersonaAttributeSource Source, string GivenName);

    [Fact]
    public void Serialize_Enum_UsesKebabCaseString()
    {
        var json = JsonSerializer.Serialize(new Sample(PersonaAttributeSource.SelfAsserted, "Stuart"), SorchaJson.Options);

        json.Should().Contain("\"source\":\"self-asserted\"");
        json.Should().Contain("\"givenName\":\"Stuart\"", "properties are camelCase");
    }

    [Fact]
    public void Deserialize_KebabCaseEnumString_Succeeds()
    {
        var model = JsonSerializer.Deserialize<Sample>(
            """{ "source": "self-asserted", "givenName": "Stuart" }""", SorchaJson.Options);

        model!.Source.Should().Be(PersonaAttributeSource.SelfAsserted);
        model.GivenName.Should().Be("Stuart");
    }

    [Fact]
    public void Deserialize_NumericEnum_AlsoSucceeds()
    {
        // Services that still emit numeric enums must remain readable — a client on these options is
        // forward/backward compatible, which is what makes the shared source safe to adopt.
        var model = JsonSerializer.Deserialize<Sample>(
            """{ "source": 0, "givenName": "Stuart" }""", SorchaJson.Options);

        model!.Source.Should().Be(PersonaAttributeSource.SelfAsserted);
    }

    [Fact]
    public void Configure_IsIdempotent_NoDuplicateEnumConverter()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        SorchaJson.Configure(options);
        SorchaJson.Configure(options);

        options.Converters.OfType<System.Text.Json.Serialization.JsonStringEnumConverter>()
            .Should().HaveCount(1);
    }
}
