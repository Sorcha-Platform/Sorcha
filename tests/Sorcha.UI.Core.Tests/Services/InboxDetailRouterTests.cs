// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Components.User.Services.Shared;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Guards issue #1266: tapping a decision notice left the app, opened an external browser at
/// <c>about:blank</c>, and produced a zero-byte download named after the instance id — because the
/// stored <c>detailHref</c> is a raw API route (<c>/api/instances/{id}</c>) and the client handed it
/// straight to the browser, which sent no bearer token and got an unrenderable 401.
/// </summary>
public sealed class InboxDetailRouterTests
{
    private readonly DefaultInboxDetailRouter _default = new();

    /// <summary>The exact href from the reported notice.</summary>
    private const string InstanceApiHref = "/api/instances/adff1578-08ac-45f4-8fb6-a421e33d45aa";

    [Fact]
    public void Default_RefusesARawApiRoute()
        => _default.Resolve(InstanceApiHref).Should().BeNull(
            "handing an API URL to the browser is what produced about:blank plus a 0 KB download (#1266)");

    [Theory]
    [InlineData("/api/instances/abc")]
    [InlineData("api/instances/abc")]
    [InlineData("/API/Instances/abc")]
    public void Default_RefusesApiRoutes_WhateverTheCasingOrLeadingSlash(string href)
        => _default.Resolve(href).Should().BeNull();

    /// <summary>An absolute URL would leave the app entirely — never a valid in-app route.</summary>
    [Theory]
    [InlineData("https://n1.sorcha.dev/api/instances/abc")]
    [InlineData("http://example.test/somewhere")]
    public void Default_RefusesAbsoluteUrls(string href)
        => _default.Resolve(href).Should().BeNull();

    /// <summary>
    /// F150's security notices already use an app-relative href (<c>/security</c>); those must keep
    /// working, so the fix must not refuse everything.
    /// </summary>
    [Theory]
    [InlineData("/security")]
    [InlineData("/my-actions")]
    [InlineData("applications/abc")]
    public void Default_PassesThroughAppRelativeRoutes(string href)
        => _default.Resolve(href).Should().Be(href);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Default_TreatsAbsentHrefAsNotNavigable(string? href)
        => _default.Resolve(href).Should().BeNull();
}
