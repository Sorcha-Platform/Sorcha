// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Components.Forms.Controls;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms;

/// <summary>
/// Tests for <see cref="PostcodeLookupRenderer.GetParentScope"/> — the helper
/// that computes the sibling-field prefix for autofill writes. Feature 103 US3.
/// </summary>
public class PostcodeLookupRendererTests
{
    [Fact]
    public void GetParentScope_OneDeepScope_ReturnsParentWithTrailingSlash()
    {
        PostcodeLookupRenderer.GetParentScope("/address/postcode")
            .Should().Be("/address/");
    }

    [Fact]
    public void GetParentScope_RootLevelScope_ReturnsRootSlash()
    {
        PostcodeLookupRenderer.GetParentScope("/postcode")
            .Should().Be("/");
    }

    [Fact]
    public void GetParentScope_EmptyScope_ReturnsRootSlash()
    {
        PostcodeLookupRenderer.GetParentScope(string.Empty)
            .Should().Be("/");
    }

    [Fact]
    public void GetParentScope_DeeplyNestedScope_ReturnsImmediateParent()
    {
        PostcodeLookupRenderer.GetParentScope("/applicant/address/postcode")
            .Should().Be("/applicant/address/");
    }
}
