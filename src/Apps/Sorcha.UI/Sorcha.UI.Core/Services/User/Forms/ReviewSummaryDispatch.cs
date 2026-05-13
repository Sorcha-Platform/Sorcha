// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Models.Forms;

namespace Sorcha.UI.Core.Services.Forms;

/// <summary>
/// Static dispatch helpers used by <c>ReviewSummaryRenderer.razor</c>. Kept in
/// a plain static class so the rules are unit-testable without spinning up a
/// Blazor renderer. Feature 107 Assured Identity v1 (T023).
/// </summary>
public static class ReviewSummaryDispatch
{
    /// <summary>
    /// Maps the current action lifecycle state to the watermark treatment the
    /// card should render.
    /// </summary>
    public static IdCardWatermark ResolveWatermark(ActionRuntimeState state) => state switch
    {
        ActionRuntimeState.CitizenDraft => IdCardWatermark.Draft,
        ActionRuntimeState.AssessorPending => IdCardWatermark.Pending,
        ActionRuntimeState.IssuedCredential => IdCardWatermark.Issued,
        ActionRuntimeState.CredentialPreview => IdCardWatermark.None,
        _ => IdCardWatermark.None
    };

    /// <summary>
    /// True when <paramref name="variant"/> has a dedicated layout component
    /// in v1. Unknown-but-declared variants fall back to the tabular minimal
    /// render in <c>ReviewSummaryRenderer.razor</c>.
    /// </summary>
    public static bool IsImplemented(XReviewLayoutVariant variant) =>
        variant is XReviewLayoutVariant.IdCard;
}
