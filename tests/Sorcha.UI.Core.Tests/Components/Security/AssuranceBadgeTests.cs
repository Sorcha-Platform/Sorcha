// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Sorcha.Tenant.Models.Auth;
using Sorcha.UI.Core.Components.Security;
using Sorcha.UI.Core.Models;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Security;

/// <summary>
/// Feature 150 — bUnit tests for <see cref="AssuranceBadge"/>. The badge is purely presentational:
/// it renders the tier label (Strongest / Strong / Basic) the server computed, never deriving it.
/// </summary>
public sealed class AssuranceBadgeTests : BunitContext
{
    public AssuranceBadgeTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Theory]
    [InlineData(AuthAssuranceTier.Strongest, "Strongest")]
    [InlineData(AuthAssuranceTier.Strong, "Strong")]
    [InlineData(AuthAssuranceTier.Basic, "Basic")]
    public void RendersLabel_PerTier(AuthAssuranceTier tier, string expected)
    {
        var cut = Render<AssuranceBadge>(parameters => parameters.Add(p => p.Tier, tier));

        cut.Find("[data-testid=assurance-badge]").TextContent.Trim().Should().Be(expected);
    }
}
