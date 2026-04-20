// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Forms;

/// <summary>
/// Specification for a resize-to-token-image operation. Feature 107 Assured
/// Identity v1. The v1 preset is 240×320 JPEG at ≤20KB raw / ≤27KB base64,
/// matching ISO/IEC 19794-5 token image conventions.
/// </summary>
public sealed record ImageTokenSpec
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int MaxRawBytes { get; init; }
    public required IReadOnlyList<double> QualitySteps { get; init; }

    /// <summary>
    /// The canonical v1 preset — 240×320 JPEG, ≤20KB raw.
    /// </summary>
    public static ImageTokenSpec ImageTokenJpeg240x320 => new()
    {
        Width = 240,
        Height = 320,
        MaxRawBytes = 20 * 1024,
        QualitySteps = [0.85, 0.75, 0.65, 0.55, 0.5]
    };
}

/// <summary>
/// Outcome of a resize-to-token operation.
/// </summary>
public sealed record PhotoTokenResult
{
    /// <summary>Base64-encoded JPEG bytes ready to embed in the action payload.</summary>
    public required string Base64 { get; init; }

    /// <summary>Raw JPEG byte length (pre-base64 expansion).</summary>
    public required int RawBytes { get; init; }

    /// <summary>The final JPEG quality factor that met the size target.</summary>
    public required double QualityUsed { get; init; }
}

/// <summary>
/// Client-side photo-to-token-image resizer. Reduces a user-captured portrait
/// to the ISO 19794-5 token-image dimensions so the <c>portrait</c> claim can
/// be embedded directly in the credential SD-JWT. Feature 107 Assured
/// Identity v1 (T016 / FR-004).
/// </summary>
/// <remarks>
/// Contract:
/// <c>specs/107-assured-identity-v1/contracts/portrait-claim-format.md</c>.
/// The quality-stepping policy lives here (pure C#); the actual canvas
/// operation is delegated to <see cref="IPhotoTokenResizerInterop"/> so unit
/// tests can mock the JS side.
/// </remarks>
public class PhotoTokenResizer
{
    private readonly IPhotoTokenResizerInterop _interop;

    public PhotoTokenResizer(IPhotoTokenResizerInterop interop)
    {
        _interop = interop ?? throw new ArgumentNullException(nameof(interop));
    }

    /// <summary>
    /// Resizes <paramref name="sourceBytes"/> to the token-image spec, stepping
    /// JPEG quality down until the result fits under
    /// <see cref="ImageTokenSpec.MaxRawBytes"/>. Throws
    /// <see cref="PhotoTokenTooDetailedException"/> if even the lowest quality
    /// step exceeds the target.
    /// </summary>
    public virtual async Task<PhotoTokenResult> ResizeAsync(
        byte[] sourceBytes,
        ImageTokenSpec spec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceBytes);
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.QualitySteps.Count == 0)
        {
            throw new ArgumentException(
                "ImageTokenSpec.QualitySteps must contain at least one quality level.",
                nameof(spec));
        }

        byte[]? bestBytes = null;
        double bestQuality = 0;

        foreach (var quality in spec.QualitySteps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var encoded = await _interop.ResizeAndEncodeJpegAsync(
                sourceBytes, spec.Width, spec.Height, quality, cancellationToken)
                .ConfigureAwait(false);

            if (encoded.Length <= spec.MaxRawBytes)
            {
                return new PhotoTokenResult
                {
                    Base64 = Convert.ToBase64String(encoded),
                    RawBytes = encoded.Length,
                    QualityUsed = quality
                };
            }

            // Track the smallest output in case we never meet the target — the
            // exception message cites the closest attempt for a better error.
            if (bestBytes is null || encoded.Length < bestBytes.Length)
            {
                bestBytes = encoded;
                bestQuality = quality;
            }
        }

        throw new PhotoTokenTooDetailedException(
            $"Portrait too detailed for {spec.Width}x{spec.Height} token: best attempt at quality " +
            $"{bestQuality:F2} produced {bestBytes?.Length ?? 0} bytes (target {spec.MaxRawBytes}). " +
            "Ask the citizen to retake with a plainer background or skip the photo.");
    }
}

/// <summary>
/// Thrown by <see cref="PhotoTokenResizer"/> when the source image cannot
/// be reduced to fit <see cref="ImageTokenSpec.MaxRawBytes"/> at any quality
/// step in <see cref="ImageTokenSpec.QualitySteps"/>.
/// </summary>
public sealed class PhotoTokenTooDetailedException : Exception
{
    public PhotoTokenTooDetailedException(string message) : base(message) { }
}
