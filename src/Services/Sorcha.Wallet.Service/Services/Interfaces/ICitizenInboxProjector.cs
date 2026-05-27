// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Core.Domain.Entities;

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Projects credential-lifecycle changes onto the citizen sync surface
/// (Feature 114, US4).
/// </summary>
/// <remarks>
/// Single composition point hooked into the existing Feature 106 credential
/// pipeline:
/// <list type="bullet">
///   <item>
///     <c>InboundCredentialDetector</c> calls
///     <see cref="OnCredentialAddedAsync"/> after a successful
///     <c>CredentialStore.StoreAsync</c> for a <c>PendingAcceptance</c> row.
///   </item>
///   <item>
///     <c>CredentialStore.PatchStatusAsync</c> and <c>UpdateStatusAsync</c>
///     call <see cref="OnCredentialStatusChangedAsync"/> after a successful
///     status transition.
///   </item>
/// </list>
/// The projector resolves the recipient via <see cref="IHolderAddressLookup"/>;
/// if the wallet address is not a citizen holder the call is a no-op (the org
/// credential pipeline is unaffected). When the recipient is a citizen, the
/// projector appends a <c>CitizenCredentialEventLog</c> row and emits
/// <c>WalletHub.CredentialAvailable</c> on the citizen's PlatformUser group.
/// </remarks>
public interface ICitizenInboxProjector
{
    /// <summary>
    /// Project a newly-stored credential onto the citizen sync surface.
    /// No-op for non-citizen recipients.
    /// </summary>
    Task OnCredentialAddedAsync(CredentialEntity credential, CancellationToken ct = default);

    /// <summary>
    /// Project a credential-status transition onto the citizen sync surface.
    /// Emits a <c>Revoked</c>-kind event when the new status is
    /// <see cref="CredentialStatus.Revoked"/> or <see cref="CredentialStatus.Declined"/>;
    /// no-op for transitions the wallet PWA does not surface (e.g.
    /// <c>Active → Suspended</c>).
    /// </summary>
    Task OnCredentialStatusChangedAsync(
        CredentialEntity credential,
        CredentialStatus previousStatus,
        CancellationToken ct = default);
}
