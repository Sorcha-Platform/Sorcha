// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Outcomes of <see cref="Services.IPasskeyService.RenameCredentialAsync"/>.
/// </summary>
public enum PasskeyRenameOutcome
{
    /// <summary>The credential id was not found or is not owned by the caller.</summary>
    NotFound = 0,

    /// <summary>The credential is in <see cref="CredentialStatus.Disabled"/> state and cannot be renamed.</summary>
    BlockedByDisabled = 1,

    /// <summary>The credential is in <see cref="CredentialStatus.Revoked"/> state and cannot be renamed.</summary>
    BlockedByRevoked = 2,

    /// <summary>The display name was updated.</summary>
    Renamed = 3,
}

/// <summary>
/// Outcomes of <see cref="Services.IPasskeyService.RevokeCredentialAsync"/>.
/// The endpoint relies on <see cref="PriorStatus"/> on the result record to
/// distinguish forensic reasons; this enum captures the final disposition.
/// </summary>
public enum PasskeyRevocationOutcome
{
    /// <summary>The credential id was not found or is not owned by the caller.</summary>
    NotFound = 0,

    /// <summary>The credential is already <see cref="CredentialStatus.Revoked"/>; treated as not-found by the API surface.</summary>
    AlreadyRevoked = 1,

    /// <summary>Removing the credential would leave the platform user with zero remaining sign-in methods.</summary>
    BlockedByFloor = 2,

    /// <summary>An <see cref="CredentialStatus.Active"/> credential was soft-revoked (audit reason: <c>"user-removed"</c>).</summary>
    RevokedFromActive = 3,

    /// <summary>A <see cref="CredentialStatus.Disabled"/> credential was soft-revoked (audit reason: <c>"user-removed-after-disable"</c>).</summary>
    RevokedFromDisabled = 4,
}
