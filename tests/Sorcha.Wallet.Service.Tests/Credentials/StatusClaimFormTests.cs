// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using FluentAssertions;

using Sorcha.Wallet.Service.Models;

using Xunit;

namespace Sorcha.Wallet.Service.Tests.Credentials;

/// <summary>
/// Feature 095 US3 — locks in the wire shape for the new
/// <see cref="StatusClaimForm"/> enum so the Blueprint Service can set the HAIP
/// form by string name over HTTP without this diverging silently.
/// </summary>
public class StatusClaimFormTests
{
    [Fact]
    public void Enum_Default_IsW3cBitstringStatusListEntry()
    {
        // Default covers callers that haven't been migrated — spec 093 behaviour
        // must continue to be the fallback shape.
        default(StatusClaimForm).Should().Be(StatusClaimForm.W3cBitstringStatusListEntry);
    }

    [Fact]
    public void Enum_SerializesAsString_NotInteger()
    {
        // The JsonStringEnumConverter annotation means requests carry the string
        // names on the wire, not ordinal ints. Accidentally losing the converter
        // would break every HAIP caller silently.
        var payload = JsonSerializer.Serialize(StatusClaimForm.IetfTokenStatusList);

        payload.Should().Be("\"IetfTokenStatusList\"");
    }

    [Fact]
    public void Enum_RoundTripsFromWire()
    {
        var wire = "\"IetfTokenStatusList\"";
        var parsed = JsonSerializer.Deserialize<StatusClaimForm>(wire);
        parsed.Should().Be(StatusClaimForm.IetfTokenStatusList);

        wire = "\"W3cBitstringStatusListEntry\"";
        parsed = JsonSerializer.Deserialize<StatusClaimForm>(wire);
        parsed.Should().Be(StatusClaimForm.W3cBitstringStatusListEntry);
    }
}
