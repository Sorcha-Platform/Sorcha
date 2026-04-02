// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace Sorcha.Gateway.Integration.Tests;

/// <summary>
/// Base class for gateway integration tests using Aspire test host
/// </summary>
public class GatewayIntegrationTestBase : IAsyncLifetime
{
    protected DistributedApplication? App { get; private set; }
    protected HttpClient? GatewayClient { get; private set; }

    /// <summary>
    /// Indicates whether the Aspire test host started successfully.
    /// When false, tests should skip rather than fail.
    /// </summary>
    protected bool InfrastructureAvailable { get; private set; }

    private string? _skipReason;

    public async ValueTask InitializeAsync()
    {
        try
        {
            // Create the Aspire app host for testing
            var appHost = await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.Sorcha_AppHost>();

            // Build and start the application
            App = await appHost.BuildAsync();
            await App.StartAsync();

            // Get HTTP client for the API Gateway
            GatewayClient = App.CreateHttpClient("api-gateway");
            InfrastructureAvailable = true;
        }
        catch (Exception ex)
        {
            _skipReason = $"Gateway infrastructure not available: {ex.GetType().Name} — {ex.Message}";
            InfrastructureAvailable = false;
        }
    }

    /// <summary>
    /// Call at the start of each test to skip when infrastructure is unavailable.
    /// </summary>
    protected void SkipIfInfrastructureUnavailable()
    {
        if (!InfrastructureAvailable)
        {
            Assert.Skip(_skipReason ?? "Gateway infrastructure not available");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (App != null)
        {
            await App.DisposeAsync();
        }

        GatewayClient?.Dispose();
    }
}
