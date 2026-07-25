// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Moq;
using Sorcha.UI.Core.Services.Forms;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Forms;

/// <summary>
/// Tests for <see cref="PhotoTokenResizer"/> — the client-side resizer that
/// reduces a citizen-captured portrait to the ISO 19794-5 token-image size
/// (240×320, ≤20KB JPEG). Feature 107 Assured Identity v1 (T016).
/// </summary>
/// <remarks>
/// The browser-side canvas operation is mocked via
/// <see cref="IPhotoTokenResizerInterop"/>; the tests exercise the
/// quality-stepping policy in pure C#.
/// </remarks>
public class PhotoTokenResizerTests
{
    private static readonly byte[] SourceBytes = new byte[1024 * 1024]; // 1 MB stub

    private static byte[] BytesOfSize(int size) => new byte[size];

    [Fact]
    public async Task ResizeAsync_FirstQualityMeetsTarget_ReturnsImmediately()
    {
        var spec = ImageTokenSpec.ImageTokenJpeg240x320;
        var interop = new Mock<IPhotoTokenResizerInterop>(MockBehavior.Strict);
        interop
            .Setup(x => x.ResizeAndEncodeJpegAsync(SourceBytes, 240, 320, 0.85, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BytesOfSize(15_000)); // well under 20KB

        var resizer = new PhotoTokenResizer(interop.Object);

        var result = await resizer.ResizeAsync(SourceBytes, spec);

        result.RawBytes.Should().Be(15_000);
        result.QualityUsed.Should().Be(0.85);
        result.Base64.Should().NotBeNullOrEmpty();
        interop.Verify(x => x.ResizeAndEncodeJpegAsync(
            It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResizeAsync_StepsDownQualityUntilUnderTarget()
    {
        var spec = ImageTokenSpec.ImageTokenJpeg240x320;
        var interop = new Mock<IPhotoTokenResizerInterop>(MockBehavior.Strict);
        interop.Setup(x => x.ResizeAndEncodeJpegAsync(SourceBytes, 240, 320, 0.85, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BytesOfSize(30_000));
        interop.Setup(x => x.ResizeAndEncodeJpegAsync(SourceBytes, 240, 320, 0.75, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BytesOfSize(25_000));
        interop.Setup(x => x.ResizeAndEncodeJpegAsync(SourceBytes, 240, 320, 0.65, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BytesOfSize(18_000));

        var resizer = new PhotoTokenResizer(interop.Object);

        var result = await resizer.ResizeAsync(SourceBytes, spec);

        result.RawBytes.Should().Be(18_000);
        result.QualityUsed.Should().Be(0.65);
        interop.Verify(x => x.ResizeAndEncodeJpegAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), 0.85, It.IsAny<CancellationToken>()), Times.Once);
        interop.Verify(x => x.ResizeAndEncodeJpegAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), 0.75, It.IsAny<CancellationToken>()), Times.Once);
        interop.Verify(x => x.ResizeAndEncodeJpegAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), 0.65, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResizeAsync_NeverMeetsTarget_ThrowsPhotoTokenTooDetailedException()
    {
        var spec = ImageTokenSpec.ImageTokenJpeg240x320;
        var interop = new Mock<IPhotoTokenResizerInterop>();
        // Every quality step at EVERY size produces oversize output. Issue #1277 added a
        // downscale ladder below 240×320, so pinning the setup to 240×320 no longer expresses
        // "nothing fits" — the loose mock answers other sizes with an empty array, which fits
        // trivially and makes this test pass for the wrong reason.
        interop.Setup(x => x.ResizeAndEncodeJpegAsync(
                It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BytesOfSize(40_000));

        var resizer = new PhotoTokenResizer(interop.Object);

        var act = () => resizer.ResizeAsync(SourceBytes, spec);

        await act.Should().ThrowAsync<PhotoTokenTooDetailedException>();
    }

    [Fact]
    public async Task ResizeAsync_Base64OutputMatchesRawJpegBytes()
    {
        var spec = ImageTokenSpec.ImageTokenJpeg240x320;
        var interop = new Mock<IPhotoTokenResizerInterop>();
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 }; // JPEG magic
        interop.Setup(x => x.ResizeAndEncodeJpegAsync(It.IsAny<byte[]>(), 240, 320, 0.85, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jpegBytes);

        var resizer = new PhotoTokenResizer(interop.Object);

        var result = await resizer.ResizeAsync(SourceBytes, spec);

        result.Base64.Should().Be(Convert.ToBase64String(jpegBytes));
    }

    [Fact]
    public async Task ResizeAsync_PassesSpecDimensionsToInterop()
    {
        var spec = new ImageTokenSpec
        {
            Width = 300,
            Height = 400,
            MaxRawBytes = 15_000,
            QualitySteps = [0.8]
        };
        var interop = new Mock<IPhotoTokenResizerInterop>();
        interop.Setup(x => x.ResizeAndEncodeJpegAsync(SourceBytes, 300, 400, 0.8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BytesOfSize(10_000));

        var resizer = new PhotoTokenResizer(interop.Object);
        await resizer.ResizeAsync(SourceBytes, spec);

        interop.Verify(x => x.ResizeAndEncodeJpegAsync(SourceBytes, 300, 400, 0.8, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void V1Preset_HasCanonicalDimensions()
    {
        var spec = ImageTokenSpec.ImageTokenJpeg240x320;
        spec.Width.Should().Be(240);
        spec.Height.Should().Be(320);
        spec.MaxRawBytes.Should().Be(20 * 1024);
        spec.QualitySteps.Should().StartWith([0.85, 0.75, 0.65]);
        spec.QualitySteps.Last().Should().BeApproximately(0.5, 0.001);
    }
}
