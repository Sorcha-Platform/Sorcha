// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Services.Implementation;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Tests for Feature 107 PR 2 (US2) — claim mappings sourced from
/// <c>/presentedClaims/*</c> when the hosting action has a
/// verified-presentation context. The DLA issuance action mints a
/// <c>DrivingLicenceCredential</c> whose <c>holderName</c>,
/// <c>holderDateOfBirth</c>, and (optional) <c>holderPortrait</c> claims
/// carry forward from the citizen's presented <c>AssuredIdentityCredential</c>.
/// </summary>
public class BuildClaimsFromMappingsCarryForwardTests
{
    private static IEnumerable<ClaimMapping> Mappings(params (string claim, string source)[] entries) =>
        entries.Select(e => new ClaimMapping { ClaimName = e.claim, SourceField = e.source });

    private static Dictionary<string, object?> PayloadWithPresentedClaims(
        Dictionary<string, object?> presentedClaims,
        Dictionary<string, object?>? otherFields = null)
    {
        var result = new Dictionary<string, object?> { ["presentedClaims"] = presentedClaims };
        if (otherFields is not null)
        {
            foreach (var kvp in otherFields) result[kvp.Key] = kvp.Value;
        }
        return result;
    }

    [Fact]
    public void BuildClaimsFromMappings_PresentedClaimSourceField_ResolvesThroughNestedDict()
    {
        var payload = PayloadWithPresentedClaims(new Dictionary<string, object?>
        {
            ["givenName"]   = "Alex",
            ["familyName"]  = "MacLeod",
            ["dateOfBirth"] = "1990-06-21"
        });

        var mappings = Mappings(
            ("holderName",         "/presentedClaims/givenName"),
            ("holderFamilyName",   "/presentedClaims/familyName"),
            ("holderDateOfBirth",  "/presentedClaims/dateOfBirth"));

        var claims = ActionExecutionService.BuildClaimsFromMappings(
            mappings, payload, NullLogger.Instance);

        claims["holderName"].Should().Be("Alex");
        claims["holderFamilyName"].Should().Be("MacLeod");
        claims["holderDateOfBirth"].Should().Be("1990-06-21");
    }

    [Fact]
    public void BuildClaimsFromMappings_PortraitCarriedForward_WhenPresented()
    {
        // Citizen disclosed portrait from their AssuredIdentityCredential.
        // The licence credential mints with the SAME base64 byte-for-byte.
        var sampleToken = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
        var payload = PayloadWithPresentedClaims(new Dictionary<string, object?>
        {
            ["givenName"]   = "Alex",
            ["familyName"]  = "MacLeod",
            ["dateOfBirth"] = "1990-06-21",
            ["portrait"]    = sampleToken
        });

        var mappings = Mappings(("holderPortrait", "/presentedClaims/portrait"));

        var claims = ActionExecutionService.BuildClaimsFromMappings(
            mappings, payload, NullLogger.Instance);

        claims["holderPortrait"].Should().Be(sampleToken);
    }

    [Fact]
    public void BuildClaimsFromMappings_PortraitWithheld_LicenceStillIssuedWithoutPortrait()
    {
        // Citizen ran Phase 1 without a photo; presented AssuredIdentity
        // discloses only givenName/familyName/dateOfBirth. The licence
        // blueprint maps holderPortrait from /presentedClaims/portrait,
        // which is absent — claim drops, credential still issues with
        // the other three claims.
        var payload = PayloadWithPresentedClaims(new Dictionary<string, object?>
        {
            ["givenName"]   = "Alex",
            ["familyName"]  = "MacLeod",
            ["dateOfBirth"] = "1990-06-21"
        });

        var mappings = Mappings(
            ("holderName",        "/presentedClaims/givenName"),
            ("holderPortrait",    "/presentedClaims/portrait"));

        var claims = ActionExecutionService.BuildClaimsFromMappings(
            mappings, payload, NullLogger.Instance);

        claims.Should().ContainKey("holderName");
        claims.Should().NotContainKey("holderPortrait");
    }

    [Fact]
    public void BuildClaimsFromMappings_MixedPayloadAndPresentedSources_BothResolve()
    {
        // Licence issuance draws some fields from the action payload
        // (licenceNumber, vehicleClass, dates) and others from the
        // presented credential (holder identity).
        var payload = PayloadWithPresentedClaims(
            presentedClaims: new Dictionary<string, object?>
            {
                ["givenName"]   = "Alex",
                ["familyName"]  = "MacLeod",
                ["dateOfBirth"] = "1990-06-21"
            },
            otherFields: new Dictionary<string, object?>
            {
                ["licenceNumber"] = "DLA-2026-12345",
                ["vehicleClass"]  = "Car (B)",
                ["issuedDate"]    = "2026-04-20",
                ["expiryDate"]    = "2036-04-20"
            });

        var mappings = Mappings(
            ("licenceNumber",     "/licenceNumber"),
            ("vehicleClass",      "/vehicleClass"),
            ("issuedDate",        "/issuedDate"),
            ("expiryDate",        "/expiryDate"),
            ("holderName",        "/presentedClaims/givenName"),
            ("holderFamilyName",  "/presentedClaims/familyName"),
            ("holderDateOfBirth", "/presentedClaims/dateOfBirth"));

        var claims = ActionExecutionService.BuildClaimsFromMappings(
            mappings, payload, NullLogger.Instance);

        claims["licenceNumber"].Should().Be("DLA-2026-12345");
        claims["vehicleClass"].Should().Be("Car (B)");
        claims["issuedDate"].Should().Be("2026-04-20");
        claims["expiryDate"].Should().Be("2036-04-20");
        claims["holderName"].Should().Be("Alex");
        claims["holderFamilyName"].Should().Be("MacLeod");
        claims["holderDateOfBirth"].Should().Be("1990-06-21");
    }

    [Fact]
    public void BuildClaimsFromMappings_PresentedClaimsBlockAbsent_AllMappingsDrop()
    {
        // No verified presentation was bound into the action context.
        // All /presentedClaims/* mappings must drop cleanly rather than
        // throw; the credential issues with whatever direct-payload
        // claims did resolve.
        var payload = new Dictionary<string, object?>
        {
            ["licenceNumber"] = "DLA-2026-99999"
        };

        var mappings = Mappings(
            ("licenceNumber", "/licenceNumber"),
            ("holderName",    "/presentedClaims/givenName"));

        var claims = ActionExecutionService.BuildClaimsFromMappings(
            mappings, payload, NullLogger.Instance);

        claims.Should().ContainKey("licenceNumber");
        claims.Should().NotContainKey("holderName");
    }

    [Fact]
    public void BuildClaimsFromMappings_PortraitCarryForward_RespectsPortraitSizeGate()
    {
        // The portrait size gate applies to ANY claim whose source
        // pointer ends in /tokenImageBase64 — including carry-forward
        // mappings. If an AssuredIdentity somehow presented an oversized
        // tokenImageBase64 (it shouldn't, since Feature 107 PR 1's gate
        // already caught it at its issuance), the gate fires again here
        // and the DLA licence issues without the portrait carry-forward.
        var oversize = new string('A', 27_001);
        var payload = PayloadWithPresentedClaims(new Dictionary<string, object?>
        {
            ["tokenImageBase64"] = oversize
        });

        var mappings = Mappings(("holderPortrait", "/presentedClaims/tokenImageBase64"));

        var claims = ActionExecutionService.BuildClaimsFromMappings(
            mappings, payload, NullLogger.Instance);

        claims.Should().NotContainKey("holderPortrait");
    }
}
