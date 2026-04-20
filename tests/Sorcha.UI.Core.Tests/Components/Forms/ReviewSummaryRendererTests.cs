// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Models.Forms;
using Sorcha.UI.Core.Services.Forms;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms;

/// <summary>
/// Tests for <see cref="ReviewSummaryDispatch"/> — the static decision helpers
/// used by <c>ReviewSummaryRenderer.razor</c>. Feature 107 Assured Identity v1 (T023).
/// </summary>
public class ReviewSummaryRendererTests
{
    // --- ResolveWatermark ---

    [Theory]
    [InlineData(ActionRuntimeState.CitizenDraft, IdCardWatermark.Draft)]
    [InlineData(ActionRuntimeState.AssessorPending, IdCardWatermark.Pending)]
    [InlineData(ActionRuntimeState.IssuedCredential, IdCardWatermark.Issued)]
    [InlineData(ActionRuntimeState.CredentialPreview, IdCardWatermark.None)]
    public void ResolveWatermark_MapsStateToWatermark(ActionRuntimeState state, IdCardWatermark expected) =>
        ReviewSummaryDispatch.ResolveWatermark(state).Should().Be(expected);

    // --- IsImplemented ---

    [Fact]
    public void IsImplemented_IdCard_True()
    {
        ReviewSummaryDispatch.IsImplemented(XReviewLayoutVariant.IdCard).Should().BeTrue();
    }

    [Theory]
    [InlineData(XReviewLayoutVariant.PassportPage)]
    [InlineData(XReviewLayoutVariant.Tabular)]
    [InlineData(XReviewLayoutVariant.Receipt)]
    public void IsImplemented_ReservedVariants_False(XReviewLayoutVariant variant)
    {
        ReviewSummaryDispatch.IsImplemented(variant).Should().BeFalse();
    }
}
