// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Tenant.Service.Pages.Auth;

namespace Sorcha.Tenant.Service.Tests.Pages;

/// <summary>
/// Unit tests for <see cref="SocialCallbackModel"/> page model.
/// SocialCallbackModel now only takes ILogger (IPublicUserService was removed).
/// </summary>
public class SocialCallbackModelTests
{
    private SocialCallbackModel CreateModel()
    {
        var model = new SocialCallbackModel(
            NullLogger<SocialCallbackModel>.Instance);

        var httpContext = new DefaultHttpContext();
        model.PageContext = new PageContext(new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor()));

        return model;
    }

    [Theory]
    [InlineData(null, "code", "state", "missing provider")]
    [InlineData("Google", null, "state", "missing authorization code")]
    [InlineData("Google", "code", null, "missing state")]
    public async Task OnGetAsync_MissingParams_ShowsError(
        string? provider, string? code, string? state, string expectedFragment)
    {
        // Arrange
        var model = CreateModel();

        // Act
        var result = await model.OnGetAsync(provider, code, state, null, CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain(expectedFragment);
    }
}
