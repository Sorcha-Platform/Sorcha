// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models.LocalRelationship;

/// <summary>
/// Derived view of the local node's relationship to a register (Feature 108).
/// Not persisted — recomputed from the latest control record on demand and cached
/// in-process until the next control-transaction seal on the register.
/// </summary>
/// <param name="RegisterId">The register this relationship describes.</param>
/// <param name="Roles">Flag set of attestation + roster roles the local node holds.</param>
/// <param name="ControlRecordVersion">
/// Docket number of the control transaction this relationship was derived from.
/// Consumers use this to detect stale cached copies after governance ops.
/// </param>
/// <param name="DerivedAt">When this derivation was computed.</param>
public sealed record RegisterLocalRelationship(
    string RegisterId,
    RegisterRoleSet Roles,
    int ControlRecordVersion,
    DateTimeOffset DerivedAt)
{
    /// <summary>True when the node's wallet signed the Owner attestation.</summary>
    public bool IsOwner => Roles.HasFlag(RegisterRoleSet.Owner);

    /// <summary>True when the node's wallet signed an Admin attestation.</summary>
    public bool IsAdmin => Roles.HasFlag(RegisterRoleSet.Admin);

    /// <summary>True when the node's wallet signed an Auditor attestation.</summary>
    public bool IsAuditor => Roles.HasFlag(RegisterRoleSet.Auditor);

    /// <summary>True when the node's wallet signed a Designer attestation.</summary>
    public bool IsDesigner => Roles.HasFlag(RegisterRoleSet.Designer);

    /// <summary>True when the node's validator public key is on the control record's roster.</summary>
    public bool IsValidator => Roles.HasFlag(RegisterRoleSet.Validator);

    /// <summary>
    /// True when the node has no sealing or governance authority for this register —
    /// i.e. it is not an Owner, Admin, or Validator. Auditor-only and Designer-only nodes
    /// also report <c>IsSubscriber == true</c> because they can neither seal dockets nor
    /// change governance; operationally they behave as read-only subscribers.
    /// </summary>
    public bool IsSubscriber => !IsOwner && !IsAdmin && !IsValidator;
}
