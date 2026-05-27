// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Tests.Models;

/// <summary>
/// Feature 126 — return-to allowlist validator. Covers research §R-004's
/// matcher rules: HTTPS-only (with localhost http exception), exact-host or
/// <c>*.host</c> suffix match, fail-closed on every malformed input.
/// </summary>
public sealed class ReturnToAllowlistOptionsTests
{
    private static ReturnToAllowlistOptions WithHosts(params string[] hosts) =>
        new() { Hosts = hosts };

    [Fact]
    public void IsAllowed_ExactHttpsHost_Matches() =>
        WithHosts("strathcarron.gov").IsAllowed("https://strathcarron.gov/services/driving-licence")
            .Should().BeTrue();

    [Fact]
    public void IsAllowed_WildcardSubdomain_Matches() =>
        WithHosts("*.strathcarron.gov").IsAllowed("https://api.strathcarron.gov/page")
            .Should().BeTrue();

    [Fact]
    public void IsAllowed_WildcardSubdomain_DoesNotMatch_BareApex() =>
        WithHosts("*.strathcarron.gov").IsAllowed("https://strathcarron.gov/page")
            .Should().BeFalse();

    [Fact]
    public void IsAllowed_HttpLocalhost_Allowed_For_Dev() =>
        WithHosts("localhost").IsAllowed("http://localhost/wallet").Should().BeTrue();

    [Fact]
    public void IsAllowed_HttpNonLocalhost_Rejected() =>
        WithHosts("strathcarron.gov").IsAllowed("http://strathcarron.gov/page").Should().BeFalse();

    [Fact]
    public void IsAllowed_MalformedUrl_Rejected() =>
        WithHosts("strathcarron.gov").IsAllowed("not-a-url").Should().BeFalse();

    [Fact]
    public void IsAllowed_NullOrEmpty_Rejected()
    {
        var opts = WithHosts("strathcarron.gov");
        opts.IsAllowed(null).Should().BeFalse();
        opts.IsAllowed("").Should().BeFalse();
        opts.IsAllowed("   ").Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_UnknownHost_Rejected() =>
        WithHosts("strathcarron.gov").IsAllowed("https://attacker.example/redirect")
            .Should().BeFalse();

    [Fact]
    public void IsAllowed_HostMatch_CaseInsensitive() =>
        WithHosts("Strathcarron.gov").IsAllowed("https://strathcarron.gov/page")
            .Should().BeTrue();

    [Fact]
    public void IsAllowed_EmptyAllowlist_Rejects_All() =>
        WithHosts().IsAllowed("https://strathcarron.gov/page").Should().BeFalse();

    [Fact]
    public void IsAllowed_RelativeUrl_Rejected() =>
        WithHosts("strathcarron.gov").IsAllowed("/page").Should().BeFalse();

    [Fact]
    public void IsAllowed_FileScheme_Rejected() =>
        WithHosts("strathcarron.gov").IsAllowed("file:///etc/passwd").Should().BeFalse();
}
