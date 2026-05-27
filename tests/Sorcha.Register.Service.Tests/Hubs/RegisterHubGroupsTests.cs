// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Service.Hubs;
using Xunit;

namespace Sorcha.Register.Service.Tests.Hubs;

/// <summary>
/// Unit tests for <see cref="RegisterHubGroups"/>. Phase 7 / US5 of Feature 118.
/// </summary>
public class RegisterHubGroupsTests
{
    [Fact]
    public void Register_GuidOverload_UsesNoHyphenFormat()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        RegisterHubGroups.Register(id).Should().Be("register:11111111222233334444555555555555");
    }

    [Fact]
    public void Register_StringOverload_PreservesInputFormat()
    {
        // Existing callers pass already-formatted strings (often Guid:N already).
        RegisterHubGroups.Register("11111111222233334444555555555555").Should().Be("register:11111111222233334444555555555555");
    }
}
