// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Services.Implementation;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="ActionExecutionService.BuildClaimsFromMappings"/> — the
/// shared claim-mapping extractor used by both the internal issuance path
/// and the HAIP external-wallet path. Feature 103 US4 round 2 review item 8.
/// </summary>
public class BuildClaimsFromMappingsTests
{
    private readonly Mock<ILogger> _logger = new();

    [Fact]
    public void NullMappings_ReturnsEmptyClaims()
    {
        var data = new Dictionary<string, object?> { ["givenName"] = "Alice" };

        var claims = ActionExecutionService.BuildClaimsFromMappings(null, data, NullLogger.Instance);

        claims.Should().BeEmpty();
    }

    [Fact]
    public void EmptyMappings_ReturnsEmptyClaims()
    {
        var data = new Dictionary<string, object?> { ["givenName"] = "Alice" };

        var claims = ActionExecutionService.BuildClaimsFromMappings(
            Array.Empty<ClaimMapping>(), data, NullLogger.Instance);

        claims.Should().BeEmpty();
    }

    [Fact]
    public void FlatMapping_PopulatesClaim()
    {
        var data = new Dictionary<string, object?> { ["givenName"] = "Alice" };
        var mappings = new[]
        {
            new ClaimMapping { ClaimName = "givenName", SourceField = "/givenName" }
        };

        var claims = ActionExecutionService.BuildClaimsFromMappings(mappings, data, NullLogger.Instance);

        claims.Should().ContainKey("givenName");
        claims["givenName"].Should().Be("Alice");
    }

    [Fact]
    public void NestedMapping_WalksJsonPointer()
    {
        // Feature 103: nested PersonName/v1 reference resolves via /name/givenName
        var data = new Dictionary<string, object?>
        {
            ["name"] = new Dictionary<string, object?>
            {
                ["givenName"] = "Alice",
                ["familyName"] = "O'Brien"
            }
        };
        var mappings = new[]
        {
            new ClaimMapping { ClaimName = "givenName", SourceField = "/name/givenName" },
            new ClaimMapping { ClaimName = "familyName", SourceField = "/name/familyName" }
        };

        var claims = ActionExecutionService.BuildClaimsFromMappings(mappings, data, NullLogger.Instance);

        claims.Should().HaveCount(2);
        claims["givenName"].Should().Be("Alice");
        claims["familyName"].Should().Be("O'Brien");
    }

    [Fact]
    public void MissingSource_DropsClaimAndLogsWarning()
    {
        var data = new Dictionary<string, object?> { ["givenName"] = "Alice" };
        var mappings = new[]
        {
            new ClaimMapping { ClaimName = "givenName", SourceField = "/givenName" },
            new ClaimMapping { ClaimName = "missing", SourceField = "/notThere" }
        };

        var claims = ActionExecutionService.BuildClaimsFromMappings(mappings, data, _logger.Object);

        claims.Should().ContainKey("givenName");
        claims.Should().NotContainKey("missing");

        // Verify the warning was emitted — silently dropped claims would
        // produce a credential with fewer attributes than the action
        // promised, so the log is a critical diagnostic anchor.
        _logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("/notThere") && v.ToString()!.Contains("missing")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ObjectSubtreeSource_ReturnsWholeSubtree()
    {
        // /address should return the full address object so the credential
        // carries a structured 'address' claim (e.g. for vCard-style payloads).
        var address = new Dictionary<string, object?>
        {
            ["line1"] = "42 Grafton Street",
            ["town"] = "Dublin"
        };
        var data = new Dictionary<string, object?> { ["address"] = address };
        var mappings = new[]
        {
            new ClaimMapping { ClaimName = "address", SourceField = "/address" }
        };

        var claims = ActionExecutionService.BuildClaimsFromMappings(mappings, data, NullLogger.Instance);

        claims["address"].Should().BeSameAs(address);
    }
}
