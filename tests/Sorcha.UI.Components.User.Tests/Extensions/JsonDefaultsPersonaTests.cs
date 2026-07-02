// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Tenant.Models.Persona;
using Sorcha.UI.Core.Extensions;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Extensions;

/// <summary>
/// Regression for the "My Profile is empty even though it saved" bug: the services serialize enums
/// as kebab-case strings (JsonStringEnumConverter with KebabCaseLower — PersonaAttributeSource
/// becomes "self-asserted"), so the client's read options MUST carry the same converter or the whole
/// persona response fails to deserialize and the caller silently gets a blank form.
/// </summary>
public sealed class JsonDefaultsPersonaTests
{
    // Exactly the shape the Tenant Service returns from GET /api/me/persona.
    private const string ServerPersonaJson = """
    {
      "givenName": { "value": "Stuart", "source": "self-asserted", "verifiedBy": null, "lastUpdated": "2026-07-02T10:01:44.21+00:00" },
      "familyName": { "value": "Fraser", "source": "self-asserted", "verifiedBy": null, "lastUpdated": "2026-07-02T10:01:44.21+00:00" },
      "defaultEmail": { "value": { "value": "stuart@example.test", "isDefault": true, "label": null }, "source": "self-asserted", "verifiedBy": null, "lastUpdated": "2026-07-02T10:01:44.21+00:00" },
      "allEmails": [ { "value": "stuart@example.test", "isDefault": true, "label": null } ],
      "defaultPhone": { "value": { "value": "+447966242717", "isDefault": true, "label": null, "kind": null }, "source": "self-asserted", "verifiedBy": null, "lastUpdated": "2026-07-02T10:01:44.21+00:00" },
      "allPhones": [ { "value": "+447966242717", "isDefault": true, "label": null, "kind": null } ]
    }
    """;

    [Fact]
    public void ApiOptions_DeserialisesServerPersona_WithKebabCaseEnumStrings()
    {
        var model = JsonSerializer.Deserialize<PersonaReadModelV1>(ServerPersonaJson, JsonDefaults.Api);

        model.Should().NotBeNull();
        model!.GivenName.Should().NotBeNull("the kebab-case enum string must not break deserialization");
        model.GivenName!.Value.Should().Be("Stuart");
        model.GivenName.Source.Should().Be(PersonaAttributeSource.SelfAsserted);
        model.FamilyName!.Value.Should().Be("Fraser");
        model.DefaultEmail!.Value.Value.Should().Be("stuart@example.test");
        model.DefaultPhone!.Value.Value.Should().Be("+447966242717");
    }
}
