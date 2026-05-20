// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Domain.Entities;

/// <summary>
/// Durable per-citizen record of one presentation the citizen reported making
/// (Feature 114, US5 PR3). Backs the cross-device presentation history surfaced
/// on the wallet PWA Activity page.
/// </summary>
/// <remarks>
/// This is <i>citizen-owned convenience data</i>, not a Feature 111 register
/// lifecycle event: a free-standing offline presentation has no originating
/// register, so the record carries <b>no register correlation</b> (FR-010) and
/// <b>only disclosed claim names — never values</b> (FR-002), mirroring the
/// privacy contract of the PWA-local presentation log.
/// <para>
/// Written by <c>CitizenPresentationStoreForwarder</c> off the
/// <c>POST /api/v1/wallet/presentations/log</c> request path; read by
/// <c>GET /api/v1/wallet/presentations</c>; removed by
/// <c>DELETE /api/v1/wallet/presentations/{id}</c>. The composite primary key
/// <c>(PlatformUserId, EntryId)</c> makes the forward upsert idempotent and
/// scopes every read/delete to the owning citizen.
/// </para>
/// </remarks>
public class CitizenPresentationRecord
{
    /// <summary>
    /// Owning citizen account (Tenant Service's PlatformUser). Part of the
    /// composite primary key; scopes every query.
    /// </summary>
    public Guid PlatformUserId { get; set; }

    /// <summary>
    /// Wallet-generated entry id from the report. Part of the composite primary
    /// key — the unit of identity and dedupe (FR-004).
    /// </summary>
    public Guid EntryId { get; set; }

    /// <summary>Local cache id of the credential presented (opaque correlation token).</summary>
    public Guid CredentialId { get; set; }

    /// <summary>Verifier-supplied display label (untrusted, display-only). Nullable.</summary>
    public string? VerifierLabel { get; set; }

    /// <summary>Verifier DID if the request carried one. Typically null on the offline path.</summary>
    public string? VerifierDid { get; set; }

    /// <summary>Names of the disclosed claims. Never values.</summary>
    public string[] DisclosedClaims { get; set; } = [];

    /// <summary>UTC time the wallet completed the presentation. Sort key (newest-first).</summary>
    public DateTimeOffset PresentedAt { get; set; }

    /// <summary>
    /// Outcome the wallet observed. Persisted as <c>integer</c>; values map to
    /// <c>Sorcha.CitizenWallet.Abstractions.Models.PresentationLogOutcome</c>
    /// (<c>Presented=0</c>, <c>DeclinedByCitizen=1</c>, <c>VerifierRejected=2</c>,
    /// <c>Acknowledged=3</c>).
    /// </summary>
    public int Outcome { get; set; }

    /// <summary>
    /// Server-side time the record was first stored (audit). Set on first upsert
    /// and preserved on idempotent re-report.
    /// </summary>
    public DateTimeOffset ReportedAt { get; set; }
}
