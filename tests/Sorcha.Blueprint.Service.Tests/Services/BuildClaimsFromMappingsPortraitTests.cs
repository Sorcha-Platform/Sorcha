// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Services.Implementation;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Tests for the portrait-specific behaviour of
/// <see cref="ActionExecutionService.BuildClaimsFromMappings"/>: the server is
/// the authoritative size gate — client-side resize is convenience. The claim
/// is included when the token is ≤27KB base64, omitted when absent, and
/// omitted with a warning when oversize. Feature 107 Assured Identity v1 (T026).
/// </summary>
public class BuildClaimsFromMappingsPortraitTests
{
    private const int Base64BoundBytes = 27_000;

    private static IEnumerable<ClaimMapping> Mappings(params (string claim, string source)[] entries) =>
        entries.Select(e => new ClaimMapping { ClaimName = e.claim, SourceField = e.source });

    private static string Base64String(int rawLength) =>
        Convert.ToBase64String(new byte[rawLength]);

    [Fact]
    public void BuildClaimsFromMappings_PortraitAtBoundary_Included()
    {
        // A base64 string exactly at the 27,000-char bound passes.
        var payload = new Dictionary<string, object?>
        {
            ["portrait"] = new Dictionary<string, object?>
            {
                ["tokenImageBase64"] = new string('A', Base64BoundBytes)
            },
            ["givenName"] = "Alex"
        };

        var mappings = Mappings(
            ("portrait", "/portrait/tokenImageBase64"),
            ("givenName", "/givenName"));

        var claims = ActionExecutionService.BuildClaimsFromMappings(
            mappings, payload, NullLogger.Instance);

        claims.Should().ContainKey("portrait");
        claims.Should().ContainKey("givenName");
        claims["givenName"].Should().Be("Alex");
    }

    [Fact]
    public void BuildClaimsFromMappings_PortraitOversize_Omitted()
    {
        var payload = new Dictionary<string, object?>
        {
            ["portrait"] = new Dictionary<string, object?>
            {
                // One char over the bound — must be dropped with warning.
                ["tokenImageBase64"] = new string('A', Base64BoundBytes + 1)
            },
            ["givenName"] = "Alex"
        };

        var mappings = Mappings(
            ("portrait", "/portrait/tokenImageBase64"),
            ("givenName", "/givenName"));

        var claims = ActionExecutionService.BuildClaimsFromMappings(
            mappings, payload, NullLogger.Instance);

        claims.Should().NotContainKey("portrait");
        // Other claims continue through.
        claims.Should().ContainKey("givenName");
        claims["givenName"].Should().Be("Alex");
    }

    [Fact]
    public void BuildClaimsFromMappings_PortraitAbsent_ClaimOmittedWithoutError()
    {
        // Citizen skipped the photo — credential issued without the claim.
        var payload = new Dictionary<string, object?>
        {
            ["givenName"] = "Alex"
        };

        var mappings = Mappings(
            ("portrait", "/portrait/tokenImageBase64"),
            ("givenName", "/givenName"));

        var claims = ActionExecutionService.BuildClaimsFromMappings(
            mappings, payload, NullLogger.Instance);

        claims.Should().NotContainKey("portrait");
        claims["givenName"].Should().Be("Alex");
    }

    [Fact]
    public void BuildClaimsFromMappings_PortraitWellUnderBound_Included()
    {
        var payload = new Dictionary<string, object?>
        {
            ["portrait"] = new Dictionary<string, object?>
            {
                ["tokenImageBase64"] = Base64String(10_000) // ~13,400 base64 chars
            }
        };

        var mappings = Mappings(("portrait", "/portrait/tokenImageBase64"));

        var claims = ActionExecutionService.BuildClaimsFromMappings(
            mappings, payload, NullLogger.Instance);

        claims.Should().ContainKey("portrait");
    }

    [Fact]
    public void BuildClaimsFromMappings_PortraitOversize_CollectsClientWarning()
    {
        // #340 — when a warnings sink is supplied, the dropped portrait must surface a client-facing
        // message (not only a server log line) so the submitter can be told the image was omitted.
        var payload = new Dictionary<string, object?>
        {
            ["portrait"] = new Dictionary<string, object?>
            {
                ["tokenImageBase64"] = new string('A', Base64BoundBytes + 1)
            },
            ["givenName"] = "Alex"
        };

        var mappings = Mappings(
            ("portrait", "/portrait/tokenImageBase64"),
            ("givenName", "/givenName"));

        var warnings = new List<string>();
        var claims = ActionExecutionService.BuildClaimsFromMappings(
            mappings, payload, NullLogger.Instance, warnings);

        claims.Should().NotContainKey("portrait");
        warnings.Should().ContainSingle()
            .Which.Should().Contain("portrait").And.Contain("omitted");
    }

    [Fact]
    public void BuildClaimsFromMappings_NoDroppedClaims_NoWarnings()
    {
        var payload = new Dictionary<string, object?>
        {
            ["portrait"] = new Dictionary<string, object?> { ["tokenImageBase64"] = Base64String(5_000) },
            ["givenName"] = "Alex"
        };

        var mappings = Mappings(
            ("portrait", "/portrait/tokenImageBase64"),
            ("givenName", "/givenName"));

        var warnings = new List<string>();
        ActionExecutionService.BuildClaimsFromMappings(mappings, payload, NullLogger.Instance, warnings);

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void BuildClaimsFromMappings_NonPortraitClaimsUnaffectedBySizeGate()
    {
        // A non-portrait claim with a very long value is NOT size-gated —
        // the gate only applies to tokenImageBase64-shaped portrait claims.
        var payload = new Dictionary<string, object?>
        {
            ["notes"] = new string('N', Base64BoundBytes * 2)
        };

        var mappings = Mappings(("notes", "/notes"));

        var claims = ActionExecutionService.BuildClaimsFromMappings(
            mappings, payload, NullLogger.Instance);

        claims.Should().ContainKey("notes");
    }
}
