// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Reason a per-org DID document was last (re)generated. Recorded on
/// <see cref="OrgDidDocument.LastRegenerationReason"/> for governance audit.
/// </summary>
public enum KeyEventReason
{
    /// <summary>First-time creation of the document on initial issuance.</summary>
    Bootstrap = 0,

    /// <summary>An issuance key was newly derived (covers first key + later additive slots).</summary>
    IssuanceKeyDerived = 1,

    /// <summary>An issuance key was rotated; new key replaces the previous Active key.</summary>
    IssuanceKeyRotated = 2,

    /// <summary>An issuance key was revoked via governance op.</summary>
    IssuanceKeyRevoked = 3,

    /// <summary>The status-list signing key was newly derived (additive; never invalidates VMs).</summary>
    StatusSigningKeyDerived = 4
}
