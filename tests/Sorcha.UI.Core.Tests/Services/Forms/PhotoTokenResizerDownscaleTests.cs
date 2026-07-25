// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Sorcha.UI.Core.Services.Forms;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Forms;

/// <summary>
/// Issue #1277 (UT-014) — the portrait claim was dropped silently once the token image would not
/// fit, and the resizer gave up too early.
/// <para>
/// The quality ladder (0.85 → 0.5) only ever ran at the FIXED 240×320 token size. A detailed
/// photo that still exceeded 20KB at quality 0.5 threw, the citizen's submission went out with no
/// token image, and the server's ≤27,000-char base64 gate then omitted the <c>portrait</c> claim —
/// issuing a portrait-less credential that degrades the F174 verifier, with nothing user-visible.
/// </para>
/// <para>
/// Dimensions are the obvious remaining lever and were never pulled. A slightly smaller token is a
/// far better outcome for the citizen than no portrait at all: it still shows their face, whereas a
/// dropped claim shows nothing.
/// </para>
/// </summary>
public sealed class PhotoTokenResizerDownscaleTests
{
    private static readonly byte[] Source = new byte[1024 * 1024];

    private static byte[] BytesOfSize(int size) => new byte[size];

    private static Mock<IPhotoTokenResizerInterop> Interop() =>
        new(MockBehavior.Strict);

    private static void Encodes(
        Mock<IPhotoTokenResizerInterop> interop, int w, int h, double q, int resultBytes) =>
        interop.Setup(x => x.ResizeAndEncodeJpegAsync(Source, w, h, q, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BytesOfSize(resultBytes));

    [Fact]
    public async Task WhenEveryQualityStepIsTooBig_ItRetriesAtSmallerDimensions()
    {
        var spec = ImageTokenSpec.ImageTokenJpeg240x320;
        var interop = Interop();

        // Nothing fits at the full token size, at any quality.
        foreach (var q in spec.QualitySteps) Encodes(interop, 240, 320, q, 30_000);

        // The first smaller step succeeds at its top quality.
        var (w, h) = spec.DimensionSteps[0];
        Encodes(interop, w, h, spec.QualitySteps[0], 18_000);

        var result = await new PhotoTokenResizer(interop.Object).ResizeAsync(Source, spec);

        result.RawBytes.Should().Be(18_000);
        result.Width.Should().Be(w);
        result.Height.Should().Be(h);
    }

    [Fact]
    public async Task TheFullSizeTokenIsAlwaysPreferred()
    {
        var spec = ImageTokenSpec.ImageTokenJpeg240x320;
        var interop = Interop();
        Encodes(interop, 240, 320, spec.QualitySteps[0], 15_000);

        var result = await new PhotoTokenResizer(interop.Object).ResizeAsync(Source, spec);

        result.Width.Should().Be(240);
        result.Height.Should().Be(320);
        // Strict mock: any call at a reduced dimension would have thrown, so downscaling is
        // genuinely a last resort rather than something that runs speculatively.
        interop.VerifyAll();
    }

    [Fact]
    public async Task DimensionsShrinkMonotonically_AndNeverBelowAUsableFace()
    {
        var spec = ImageTokenSpec.ImageTokenJpeg240x320;

        spec.DimensionSteps.Should().NotBeEmpty();

        var previousWidth = spec.Width;
        foreach (var (w, h) in spec.DimensionSteps)
        {
            w.Should().BeLessThan(previousWidth, "each step must actually reduce the image");
            // A token image is a face. Below roughly 120px wide it stops being usable for the
            // human check the F174 verifier exists to support, so shrinking must stop before
            // "technically fits" becomes "useless to look at".
            w.Should().BeGreaterThanOrEqualTo(120);
            ((double)w / h).Should().BeApproximately((double)spec.Width / spec.Height, 0.02,
                "the ISO 19794-5 3:4 token aspect must survive downscaling");
            previousWidth = w;
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task WhenNoDimensionFits_ItStillThrows_SoTheCitizenIsToldRatherThanSilentlyDropped()
    {
        var spec = ImageTokenSpec.ImageTokenJpeg240x320;
        var interop = Interop();

        foreach (var q in spec.QualitySteps) Encodes(interop, 240, 320, q, 90_000);
        foreach (var (w, h) in spec.DimensionSteps)
        {
            foreach (var q in spec.QualitySteps) Encodes(interop, w, h, q, 90_000);
        }

        var act = () => new PhotoTokenResizer(interop.Object).ResizeAsync(Source, spec);

        await act.Should().ThrowAsync<PhotoTokenTooDetailedException>();
    }

    [Fact]
    public async Task TheFailureNamesTheSmallestAttempt_NotTheFirst()
    {
        // The message is what the citizen is shown. Citing the 240×320 attempt after five smaller
        // ones were also tried would understate how hard the resizer worked and mislead anyone
        // debugging it.
        var spec = ImageTokenSpec.ImageTokenJpeg240x320;
        var interop = Interop();

        foreach (var q in spec.QualitySteps) Encodes(interop, 240, 320, q, 90_000);
        for (var i = 0; i < spec.DimensionSteps.Count; i++)
        {
            var (w, h) = spec.DimensionSteps[i];
            foreach (var q in spec.QualitySteps) Encodes(interop, w, h, q, 80_000 - (i * 1_000));
        }

        var last = spec.DimensionSteps[^1];
        var expectedBest = 80_000 - ((spec.DimensionSteps.Count - 1) * 1_000);

        var act = () => new PhotoTokenResizer(interop.Object).ResizeAsync(Source, spec);

        (await act.Should().ThrowAsync<PhotoTokenTooDetailedException>())
            .Which.Message.Should()
            .Contain($"{last.Width}x{last.Height}")
            .And.Contain(expectedBest.ToString());
    }
}
