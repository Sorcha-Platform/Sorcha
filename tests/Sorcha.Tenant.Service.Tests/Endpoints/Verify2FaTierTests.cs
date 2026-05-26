// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.ServiceDefaults.Auth;
using Sorcha.Tenant.Service.Endpoints;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

public class Verify2FaTierTests
{
    [Theory]
    [InlineData("consumer", Tier.Consumer)]
    [InlineData("CONSUMER", Tier.Consumer)]
    [InlineData("platform", Tier.Platform)]
    [InlineData(null, Tier.Platform)]
    public void ResolveVerify2FaTier_HonoursConsumerHintOnly(string? hint, Tier expected)
        => AuthEndpoints.ResolveVerify2FaTier(hint).Should().Be(expected);
}
