// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models.LocalRelationship;

/// <summary>
/// Flag set of roles the local node holds on a register, derived from the latest
/// <c>RegisterControlRecord</c> + local identity (wallet addresses + validator key).
/// Multiple flags may be set simultaneously — Owner + Validator is the common case
/// for a node that created a register and self-rosters as its validator.
/// </summary>
/// <remarks>
/// <c>Subscriber</c> is intentionally not a flag: a node is considered a subscriber
/// whenever <c>Roles == None</c> (i.e. no attestation matches its local identity).
/// </remarks>
[Flags]
public enum RegisterRoleSet
{
    /// <summary>No role matched — the node is a plain subscriber.</summary>
    None = 0,

    /// <summary>The node's wallet signed the Owner attestation in the control record.</summary>
    Owner = 1 << 0,

    /// <summary>The node's wallet signed an Admin attestation in the control record.</summary>
    Admin = 1 << 1,

    /// <summary>The node's wallet signed an Auditor attestation in the control record.</summary>
    Auditor = 1 << 2,

    /// <summary>The node's wallet signed a Designer attestation in the control record.</summary>
    Designer = 1 << 3,

    /// <summary>The node's validator public key appears in the control record's validator roster.</summary>
    Validator = 1 << 4
}
