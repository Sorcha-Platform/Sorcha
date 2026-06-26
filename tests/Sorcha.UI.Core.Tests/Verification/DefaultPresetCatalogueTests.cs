// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Options;
using Sorcha.UI.Components.User.Services.Verification;
using Xunit;

namespace Sorcha.UI.Core.Tests.Verification;

/// <summary>
/// Tests for the config-driven verification preset catalogue (Verify-unification PR B2) — proving
/// presets are editable via configuration without a code change, with a builtin fallback.
/// </summary>
public class DefaultPresetCatalogueTests
{
    private static DefaultPresetCatalogue Build(VerifierPresetsOptions? options = null)
        => new(Options.Create(options ?? new VerifierPresetsOptions()));

    [Fact]
    public void GetAll_NoConfig_ReturnsBuiltinFallback()
    {
        var catalogue = Build();

        catalogue.GetAll().Should().BeEquivalentTo(DefaultPresetCatalogue.Builtin);
        catalogue.GetAll().Should().Contain(p => p.Key == "age-over-18");
        catalogue.GetAll().Should().Contain(p => p.Key == "confirm-identity");
    }

    [Fact]
    public void GetAll_WithConfiguredPresets_ReturnsConfiguredOverride()
    {
        // SC-001: presets can be changed by editing configuration only (no code change).
        var options = new VerifierPresetsOptions
        {
            Presets =
            {
                new VerificationPresetConfig
                {
                    Key = "council-licence",
                    Label = "Driving licence check",
                    Purpose = "Confirm a valid driving licence",
                    RequiredVct = "https://council.example/vc/licence/v1",
                    RequiredClaims = { "licenceNumber", "categories" },
                    OptionalClaims = { "expiry" },
                    KnownCredentialClaims = { "licenceNumber", "categories", "expiry", "fullName" },
                }
            }
        };

        var catalogue = Build(options);

        catalogue.GetAll().Should().ContainSingle();
        catalogue.GetAll()[0].Key.Should().Be("council-licence");
        catalogue.GetAll()[0].RequiredClaims.Should().Equal("licenceNumber", "categories");
        // Builtin presets are NOT returned once a config set is supplied.
        catalogue.GetByKey("age-over-18").Should().BeNull();
    }

    [Fact]
    public void GetByKey_KnownKey_ReturnsPreset()
    {
        var catalogue = Build();

        var preset = catalogue.GetByKey("age-over-18");

        preset.Should().NotBeNull();
        preset!.Label.Should().Be("Age over 18?");
        preset.RequiredClaims.Should().Contain("age_over_18");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("does-not-exist")]
    public void GetByKey_MissingOrEmpty_ReturnsNull(string? key)
    {
        Build().GetByKey(key).Should().BeNull();
    }

    [Fact]
    public void BuildCustom_DerivesKnownClaimsFromRequiredAndOptional()
    {
        var catalogue = Build();

        var preset = catalogue.BuildCustom(
            purpose: "Ad-hoc check",
            requiredVct: "https://example/vc/x/v1",
            requiredClaims: new[] { "a", "b" },
            optionalClaims: new[] { "b", "c" });

        preset.Key.Should().Be("custom");
        preset.Purpose.Should().Be("Ad-hoc check");
        preset.RequiredClaims.Should().Equal("a", "b");
        preset.OptionalClaims.Should().Equal("b", "c");
        preset.KnownCredentialClaims.Should().BeEquivalentTo(new[] { "a", "b", "c" });
    }
}
