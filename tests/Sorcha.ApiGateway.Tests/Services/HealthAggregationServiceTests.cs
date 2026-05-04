// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.ApiGateway.Services;
using Xunit;

namespace Sorcha.ApiGateway.Tests.Services;

/// <summary>
/// Unit tests for HealthAggregationService classification helpers.
///
/// Regression context: the gateway aggregator's per-service classifier had no branch
/// for ASP.NET Core's "Degraded" status. Validator (and any other service) reporting
/// Degraded — which in production happens whenever an audited storage interface is on
/// an in-memory backend per the Sorcha.ServiceDefaults storage-providers health check —
/// was mapped to "unknown" and the overall gateway health flipped to "degraded"
/// permanently. /api/health smoke tests therefore returned a false-negative signal
/// every time the stack ran without persistent storage configured.
/// </summary>
public class HealthAggregationServiceTests
{
    [Theory]
    [InlineData("Healthy", "healthy")]
    [InlineData("healthy", "healthy")]
    [InlineData("HEALTHY", "healthy")]
    [InlineData("Degraded", "degraded")]
    [InlineData("degraded", "degraded")]
    [InlineData("Unhealthy", "unhealthy")]
    [InlineData("unhealthy", "unhealthy")]
    [InlineData("  Healthy  ", "healthy")]
    public void ClassifyHealthResponseText_RecognisesAspireStatuses(string body, string expected)
    {
        HealthAggregationService.ClassifyHealthResponseText(body).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("OK")]
    [InlineData("running")]
    [InlineData("???")]
    public void ClassifyHealthResponseText_UnrecognisedBody_ReturnsUnknown(string body)
    {
        HealthAggregationService.ClassifyHealthResponseText(body).Should().Be("unknown");
    }

    [Fact]
    public void ClassifyHealthResponseText_NullBody_ReturnsUnknown()
    {
        HealthAggregationService.ClassifyHealthResponseText(null!).Should().Be("unknown");
    }

    [Fact]
    public void DetermineOverallStatus_AllHealthy_ReturnsHealthy()
    {
        HealthAggregationService.DetermineOverallStatus(new[] { "healthy", "healthy", "healthy" })
            .Should().Be("healthy");
    }

    [Fact]
    public void DetermineOverallStatus_AnyUnhealthy_ReturnsUnhealthy()
    {
        HealthAggregationService.DetermineOverallStatus(new[] { "healthy", "unhealthy", "healthy" })
            .Should().Be("unhealthy");
    }

    [Fact]
    public void DetermineOverallStatus_OneDegradedRestHealthy_ReturnsDegraded()
    {
        // Regression: this was previously "degraded" only because anyHealthy was true.
        // With "Degraded" now correctly classified, the same outcome holds — and we
        // pin the assertion so future refactors don't slide back to "unhealthy".
        HealthAggregationService.DetermineOverallStatus(new[] { "healthy", "degraded", "healthy" })
            .Should().Be("degraded");
    }

    [Fact]
    public void DetermineOverallStatus_AllDegraded_ReturnsDegraded()
    {
        // Regression: previously this case returned "unhealthy" because anyHealthy was false.
        HealthAggregationService.DetermineOverallStatus(new[] { "degraded", "degraded", "degraded" })
            .Should().Be("degraded");
    }

    [Fact]
    public void DetermineOverallStatus_OneUnknownRestHealthy_ReturnsDegraded()
    {
        HealthAggregationService.DetermineOverallStatus(new[] { "healthy", "unknown", "healthy" })
            .Should().Be("degraded");
    }

    [Fact]
    public void DetermineOverallStatus_UnhealthyWinsOverDegraded()
    {
        HealthAggregationService.DetermineOverallStatus(new[] { "degraded", "unhealthy", "degraded" })
            .Should().Be("unhealthy");
    }

    [Fact]
    public void DetermineOverallStatus_EmptyInput_ReturnsUnknown()
    {
        HealthAggregationService.DetermineOverallStatus(Array.Empty<string>())
            .Should().Be("unknown");
    }
}
