// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Services.Forms;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms;

/// <summary>
/// Tests for the static helpers used by <c>FileRenderer.razor</c> to translate
/// a parsed <see cref="FileSchemaExtension"/> into the HTML <c>capture</c>
/// attribute value and the resize-for-embed decision. Feature 107 Assured
/// Identity v1.
/// </summary>
public class FileRendererCaptureTests
{
    // --- GetCaptureAttribute ---

    [Fact]
    public void GetCaptureAttribute_CaptureUser_ReturnsUser()
    {
        var ext = new FileSchemaExtension { Capture = "user" };
        FileFieldCaptureHelper.GetCaptureAttribute(ext).Should().Be("user");
    }

    [Fact]
    public void GetCaptureAttribute_CaptureEnvironment_ReturnsEnvironment()
    {
        var ext = new FileSchemaExtension { Capture = "environment" };
        FileFieldCaptureHelper.GetCaptureAttribute(ext).Should().Be("environment");
    }

    [Fact]
    public void GetCaptureAttribute_CaptureNull_ReturnsNull()
    {
        var ext = new FileSchemaExtension { Capture = null };
        FileFieldCaptureHelper.GetCaptureAttribute(ext).Should().BeNull();
    }

    [Fact]
    public void GetCaptureAttribute_ExtensionNull_ReturnsNull()
    {
        FileFieldCaptureHelper.GetCaptureAttribute(null).Should().BeNull();
    }

    [Fact]
    public void GetCaptureAttribute_CaptureUnknownValue_ReturnsNull()
    {
        // Unknown values are NOT propagated to HTML — renderer emits no
        // attribute rather than trusting an arbitrary string into the DOM.
        var ext = new FileSchemaExtension { Capture = "front-only" };
        FileFieldCaptureHelper.GetCaptureAttribute(ext).Should().BeNull();
    }

    // --- ShouldResizeForEmbed ---

    [Fact]
    public void ShouldResizeForEmbed_EmbedAsImageTokenJpeg_True()
    {
        var ext = new FileSchemaExtension { EmbedAs = "image-token-jpeg-240x320" };
        FileFieldCaptureHelper.ShouldResizeForEmbed(ext).Should().BeTrue();
    }

    [Fact]
    public void ShouldResizeForEmbed_EmbedAsNull_False()
    {
        var ext = new FileSchemaExtension { EmbedAs = null };
        FileFieldCaptureHelper.ShouldResizeForEmbed(ext).Should().BeFalse();
    }

    [Fact]
    public void ShouldResizeForEmbed_ExtensionNull_False()
    {
        FileFieldCaptureHelper.ShouldResizeForEmbed(null).Should().BeFalse();
    }

    [Fact]
    public void ShouldResizeForEmbed_EmbedAsUnknownValue_False()
    {
        var ext = new FileSchemaExtension { EmbedAs = "image-token-png-480x640" };
        FileFieldCaptureHelper.ShouldResizeForEmbed(ext).Should().BeFalse();
    }
}
