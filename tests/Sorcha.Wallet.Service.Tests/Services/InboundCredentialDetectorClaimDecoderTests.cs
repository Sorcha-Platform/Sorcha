// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using FluentAssertions;

using Sorcha.Wallet.Service.Services.Implementation;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Covers <see cref="InboundCredentialDetector.ExtractDisclosedClaimsJson"/> —
/// the SD-JWT body + disclosure decoder that populates
/// <c>CredentialEntity.ClaimsJson</c> when the wallet receives a
/// PendingAcceptance row from the register-native delivery path.
///
/// Display-time only — signature verification is intentionally out of scope.
/// The Pending tab needs to render the offered claims before the holder
/// chooses to trust the issuer.
/// </summary>
public class InboundCredentialDetectorClaimDecoderTests
{
    /// <summary>
    /// Real Forestry Certification ForestProductDPPCredential captured from the
    /// n1 walkthrough run on 2026-04-25. Body carries forestUnit /
    /// volumeCubicMetres / processingFacility as always-disclosed; the nine
    /// remaining claim mappings are SD-disclosable and live in the ~salt~
    /// tail segments.
    /// </summary>
    private const string ForestryDppRawToken =
        "eyJhbGciOiJFZERTQSIsInR5cCI6InZjXHUwMDJCc2Qtand0In0." +
        "eyJpc3MiOiJ3czExcXBtcmxzeDY5ODB6ejBodGUzdTh0YzNrbGVobHpocm44NmEyNHJoYXNsczI3M2EzemVwOXNxN3E2eTkiLCJzdWIiOiJ3czExcXJ0bDZlcXp4M3ZtMjdqM2Z4ZXNubTV6NHc2NXF1YXVyNzh1MGZrcGNuejg4M2psd2Qwank5Zmx5NzAiLCJpYXQiOjE3NzcxMjA3MjcsIl9zZF9hbGciOiJzaGEtMjU2IiwiZXhwIjoxODA4NjU2NzI3LCJmb3Jlc3RVbml0IjoiR2xlbiBBZmZyaWMgQ29tcGFydG1lbnQgMjQiLCJ2b2x1bWVDdWJpY01ldHJlcyI6MzIwLjAsInByb2Nlc3NpbmdGYWNpbGl0eSI6IkhpZ2hsYW5kIFRpbWJlciBNaWxsLCBJbnZlcm5lc3MiLCJ0eXBlIjoiRm9yZXN0UHJvZHVjdERQUENyZWRlbnRpYWwiLCJ2Y3QiOiJGb3Jlc3RQcm9kdWN0RFBQQ3JlZGVudGlhbCIsImNyZWRlbnRpYWxTdGF0dXMiOnsiaWQiOiJodHRwczovL3NvcmNoYS5leGFtcGxlL2FwaS92MS9jcmVkZW50aWFscy9zdGF0dXMtbGlzdHMvd3MxMXFwbXJsc3g2OTgwenowaHRlM3U4dGMza2xlaGx6aHJuODZhMjRyaGFzbHMyNzNhM3plcDlzcTdxNnk5LWEzNTg5NWNjNDhiNTQ5MjZhOTE2NmRkNTU3ODAwMGI3LXJldm9jYXRpb24tMSMwIiwidHlwZSI6IkJpdHN0cmluZ1N0YXR1c0xpc3RFbnRyeSIsInN0YXR1c1B1cnBvc2UiOiJyZXZvY2F0aW9uIiwic3RhdHVzTGlzdEluZGV4IjoiMCIsInN0YXR1c0xpc3RDcmVkZW50aWFsIjoiaHR0cHM6Ly9zb3JjaGEuZXhhbXBsZS9hcGkvdjEvY3JlZGVudGlhbHMvc3RhdHVzLWxpc3RzL3dzMTFxcG1ybHN4Njk4MHp6MGh0ZTN1OHRjM2tsZWhsemhybjg2YTI0cmhhc2xzMjczYTN6ZXA5c3E3cTZ5OS1hMzU4OTVjYzQ4YjU0OTI2YTkxNjZkZDU1NzgwMDBiNy1yZXZvY2F0aW9uLTEifSwiX3NkIjpbInpfVjM2TmxpWGM1UHRUSXVudTZZeV9Mb1JZTjNyX21ydGhCeXFLMDMtaGsiLCJCWnlUbHVsbmhLc2k0c0ZmcG1oRE5EMjhnVDFqS0lkMUhIVTFtQXFiMkZRIiwicGZjSkQtTmhzNVlxbVlEcnM2WTVIb1FOLXBiT2h1Y0swUWEzX296QzFfNCIsIlAyM3B3d0JJYWZOUVZjSmt4Sk5BQmMwT1gwLUFSaFVnTDQ0V3RPNTRHMlkiLCJidjY5Y2FITWRQNElJc2RGVFFKTEtKS2pVUUFHN0cyNmMyUENjc1k3eXZ3IiwiUkIwZ3l3Vm9rNzVIcmtleXB1dXYzX21mVUFiZjZtdzdJXzhIRlJvV0dKQSIsInFiWmFxOXBrOGtZNW1ldHZ1eTNjUklESWMxZm54U2tlbWl4bzlpcWlEbkEiLCI4cElES3RIT1p0YU4tOWNMUTRDTmJVQlRFV3BMcTNCbEI5X2xtX2tNUkdzIiwiZVJoa2FYX2JFSnpRTWttVTVDUkswNUxIbjhZb1ZxY0lwd3hCMlBSNmMxRSJdfQ." +
        "SU7o33WpY_eAuHcw8Qr9T1bgnRKy8nCxmZAzi9IyZDHYgVS72Jqtrrh3pjnAleJsKRFC63XtdPHSvHNLW5uqAA" +
        "~WyJVYm5jNF93clNCMV9Hcm5uQ1hOaWF3IiwiYmF0Y2hJZCIsIkhUUy1CQVRDSC0yMDI2LTAxNDIiXQ" +
        "~WyIzV1RIWi1FQnNkN2puRjk5Tk1TcXV3Iiwic3BlY2llcyIsIlNpdGthIFNwcnVjZSJd" +
        "~WyJpd25oNVk4TGxXaFFURVFTZk5qdnZBIiwib3JpZ2luQ291bnRyeSIsIlNjb3RsYW5kIl0" +
        "~WyJaalhsZjAwZ2JjcHJjdjJqQlZyUjNnIiwiaGFydmVzdERhdGUiLCIyMDI2LTAyLTE4Il0" +
        "~WyI2VXZ6aHRHM29mN0ZoOVBsLXdRbHlnIiwiY2VydGlmaWNhdGlvblNjaGVtZSIsIkZTQyJd" +
        "~WyJfYURIb2Z1ZWlrSkJZQzI5N2ZicGpBIiwiY2VydGlmaWNhdGlvbkJvZHkiLCJGU0MgVW5pdGVkIEtpbmdkb20iXQ" +
        "~WyJPODg3X3hFbmpsRTZWMGQwUVhJUy1BIiwiZW1ib2RpZWRDYXJib25LZ0NPMmUiLDM2LjRd" +
        "~WyJzQWhXdjUwaHJjUGxtWGZiU3U4S1VnIiwic3VzdGFpbmFiaWxpdHlTY29yZSIsODdd" +
        "~WyJyMTQ1Y1RPTHlaMkNKRmNEUl9LTXpBIiwiZXhwaXJ5RGF0ZSIsIjIwMjctMDQtMTUiXQ~";

    [Fact]
    public void ExtractDisclosedClaimsJson_RealForestryDpp_ReturnsAllTwelveClaims()
    {
        var json = InboundCredentialDetector.ExtractDisclosedClaimsJson(ForestryDppRawToken);

        json.Should().NotBeNull();
        using var doc = JsonDocument.Parse(json!);
        var root = doc.RootElement;

        // Always-disclosed (from the JWT body, after stripping protocol fields)
        root.GetProperty("forestUnit").GetString().Should().Be("Glen Affric Compartment 24");
        root.GetProperty("volumeCubicMetres").GetDouble().Should().Be(320.0);
        root.GetProperty("processingFacility").GetString().Should().Be("Highland Timber Mill, Inverness");

        // Selectively disclosed (from each ~salt~ tail segment)
        root.GetProperty("batchId").GetString().Should().Be("HTS-BATCH-2026-0142");
        root.GetProperty("species").GetString().Should().Be("Sitka Spruce");
        root.GetProperty("originCountry").GetString().Should().Be("Scotland");
        root.GetProperty("harvestDate").GetString().Should().Be("2026-02-18");
        root.GetProperty("certificationScheme").GetString().Should().Be("FSC");
        root.GetProperty("certificationBody").GetString().Should().Be("FSC United Kingdom");
        root.GetProperty("embodiedCarbonKgCO2e").GetDouble().Should().Be(36.4);
        root.GetProperty("sustainabilityScore").GetInt32().Should().Be(87);
        root.GetProperty("expiryDate").GetString().Should().Be("2027-04-15");
    }

    [Fact]
    public void ExtractDisclosedClaimsJson_RealForestryDpp_StripsProtocolFields()
    {
        var json = InboundCredentialDetector.ExtractDisclosedClaimsJson(ForestryDppRawToken);

        using var doc = JsonDocument.Parse(json!);
        var props = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();

        // SD-JWT / JWT plumbing must not surface as user-visible claims
        props.Should().NotContain("iss");
        props.Should().NotContain("sub");
        props.Should().NotContain("iat");
        props.Should().NotContain("exp");
        props.Should().NotContain("vct");
        props.Should().NotContain("type");
        props.Should().NotContain("_sd");
        props.Should().NotContain("_sd_alg");
        props.Should().NotContain("credentialStatus");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    [InlineData("only.one.dot")]
    [InlineData("garbage~~tilde")]
    public void ExtractDisclosedClaimsJson_MalformedInput_ReturnsNullOrEmptyObject(string? input)
    {
        var json = InboundCredentialDetector.ExtractDisclosedClaimsJson(input!);

        // Null or empty-object both acceptable — caller falls back to "{}".
        if (json is not null)
        {
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.EnumerateObject().Should().BeEmpty();
        }
    }

    [Fact]
    public void ExtractDisclosedClaimsJson_TolerateBadDisclosure_ReturnsOtherClaims()
    {
        // Take the real token and corrupt one disclosure segment.
        // The decoder should still surface the other eight.
        var corrupted = ForestryDppRawToken.Replace(
            "WyIzV1RIWi1FQnNkN2puRjk5Tk1TcXV3Iiwic3BlY2llcyIsIlNpdGthIFNwcnVjZSJd",
            "this-is-not-base64url-at-all!!!");

        var json = InboundCredentialDetector.ExtractDisclosedClaimsJson(corrupted);

        json.Should().NotBeNull();
        using var doc = JsonDocument.Parse(json!);
        // batchId still extracts (different disclosure)
        doc.RootElement.GetProperty("batchId").GetString().Should().Be("HTS-BATCH-2026-0142");
        // species disclosure was the corrupted one — it should be missing, not a thrown exception
        doc.RootElement.TryGetProperty("species", out _).Should().BeFalse();
    }
}
