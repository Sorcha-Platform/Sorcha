// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// Feature 142 (T009): the computed <see cref="Register.Sandbox"/> flag derived from
/// control-record metadata, and that both it and <see cref="Register.Advertise"/> surface
/// on the JSON read shape.
/// </summary>
public class RegisterSandboxTests
{
    [Fact]
    public void Sandbox_WhenMetadataTagTrue_ReturnsTrue()
    {
        var register = new Register
        {
            Id = new string('a', 32),
            Name = "Org Sandbox",
            InitialControlRecord = new RegisterControlRecord
            {
                Metadata = new Dictionary<string, string> { [Register.SandboxMetadataKey] = "true" }
            }
        };

        register.Sandbox.Should().BeTrue();
    }

    [Fact]
    public void Sandbox_WhenMetadataTagTrueMixedCase_ReturnsTrue()
    {
        var register = new Register
        {
            Id = new string('a', 32),
            Name = "Org Sandbox",
            InitialControlRecord = new RegisterControlRecord
            {
                Metadata = new Dictionary<string, string> { [Register.SandboxMetadataKey] = "True" }
            }
        };

        register.Sandbox.Should().BeTrue();
    }

    [Fact]
    public void Sandbox_WhenNoControlRecord_ReturnsFalse()
    {
        var register = new Register { Id = new string('a', 32), Name = "Live Register" };

        register.Sandbox.Should().BeFalse();
    }

    [Fact]
    public void Sandbox_WhenMetadataMissingTag_ReturnsFalse()
    {
        var register = new Register
        {
            Id = new string('a', 32),
            Name = "Live Register",
            InitialControlRecord = new RegisterControlRecord
            {
                Metadata = new Dictionary<string, string> { ["category"] = "general" }
            }
        };

        register.Sandbox.Should().BeFalse();
    }

    [Fact]
    public void Sandbox_WhenMetadataTagFalse_ReturnsFalse()
    {
        var register = new Register
        {
            Id = new string('a', 32),
            Name = "Live Register",
            InitialControlRecord = new RegisterControlRecord
            {
                Metadata = new Dictionary<string, string> { [Register.SandboxMetadataKey] = "false" }
            }
        };

        register.Sandbox.Should().BeFalse();
    }

    [Fact]
    public void Serialization_SurfacesAdvertiseAndSandbox_AndRoundTrips()
    {
        var register = new Register
        {
            Id = new string('a', 32),
            Name = "Org Sandbox",
            Advertise = true,
            InitialControlRecord = new RegisterControlRecord
            {
                Metadata = new Dictionary<string, string> { [Register.SandboxMetadataKey] = "true" }
            }
        };

        var json = JsonSerializer.Serialize(register, RegisterSerializationOptions.Canonical);

        // Both fields must appear on the wire (camelCase per canonical options).
        json.Should().Contain("\"advertise\":true");
        json.Should().Contain("\"sandbox\":true");

        // Round-trip: Sandbox recomputes from the deserialized control record.
        var roundTripped = JsonSerializer.Deserialize<Register>(json, RegisterSerializationOptions.Canonical);
        roundTripped.Should().NotBeNull();
        roundTripped!.Advertise.Should().BeTrue();
        roundTripped.Sandbox.Should().BeTrue();
    }
}
