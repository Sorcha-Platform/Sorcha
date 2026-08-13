// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Cli.Models;

namespace Sorcha.Cli.Services;

/// <summary>
/// Thrown by <see cref="IAuthenticationService.LoginAsync"/> when the account belongs to more than
/// one organisation and no <c>organizationId</c> was supplied to pre-select one. The caller (the
/// command layer) is expected to either prompt the user to pick from <see cref="Organizations"/> and
/// complete login via <see cref="IAuthenticationService.CompleteOrgSelectionAsync"/>, or — in a
/// non-interactive context — surface <see cref="Organizations"/> as a clear error listing the
/// available organisation IDs (issue #1402).
/// </summary>
public sealed class OrgSelectionRequiredException(string platformLoginToken, IReadOnlyList<OrgSelectionEntry> organizations)
    : Exception("Multiple organizations are available for this account; organization selection is required.")
{
    /// <summary>Short-lived token to complete org selection via <c>POST /api/auth/select-org</c>.</summary>
    public string PlatformLoginToken { get; } = platformLoginToken;

    /// <summary>The organisations available to choose from.</summary>
    public IReadOnlyList<OrgSelectionEntry> Organizations { get; } = organizations;
}
