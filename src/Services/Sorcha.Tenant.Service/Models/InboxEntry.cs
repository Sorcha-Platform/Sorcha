// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Durable per-user notification entry. Phase 5 (US3) of Feature 118.
/// </summary>
/// <remarks>
/// <para>
/// The user-facing canonical notification surface. Other services (Blueprint,
/// Wallet, Tenant itself) write entries via the internal endpoint
/// <c>POST /api/internal/inbox</c>; the user reads via <c>GET /api/me/inbox</c>
/// and acts via the read/dismiss endpoints.
/// </para>
/// <para>
/// Idempotency on writes is enforced by the unique index on
/// <see cref="PlatformUserId"/> + <see cref="SourceEventId"/>: a duplicate POST
/// with the same source-event id is a no-op that returns the original entry.
/// </para>
/// </remarks>
public class InboxEntry
{
    /// <summary>Server-generated GUID primary key.</summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>Owner — the user who can read and act on this entry.</summary>
    public Guid PlatformUserId { get; set; }

    /// <summary>Domain category. Drives icon and grouping in the UI.</summary>
    public InboxCategory Category { get; set; }

    /// <summary>Severity level. Drives visual prominence and toast behaviour.</summary>
    public InboxSeverity Severity { get; set; }

    /// <summary>
    /// Hierarchical correlation key, e.g. <c>tx:{walletAddress}:{txId}</c> or
    /// <c>membership:{orgId}</c>. Entries with the same key arriving within
    /// 30s are grouped visually in the inbox UI.
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string CorrelationKey { get; set; } = "";

    /// <summary>
    /// Authenticated REST endpoint to fetch the full render-ready content of
    /// this entry. MUST be a relative path starting with <c>/api/</c>.
    /// </summary>
    [Required]
    [MaxLength(1024)]
    public string DetailHref { get; set; } = "";

    /// <summary>
    /// Writer-supplied source event id. Forms the <c>(PlatformUserId, SourceEventId)</c>
    /// unique key used for idempotent writes — a writer that retries with the same
    /// id collapses to a no-op.
    /// </summary>
    public Guid SourceEventId { get; set; }

    /// <summary>UTC timestamp at which the underlying event occurred.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>UTC timestamp at which the user marked the entry read. <c>null</c> = unread.</summary>
    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>UTC timestamp at which the user dismissed the entry. <c>null</c> = active.</summary>
    public DateTimeOffset? DismissedAt { get; set; }

    /// <summary>Card title — short, surfaces in the inbox list.</summary>
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = "";

    /// <summary>Optional one- or two-sentence body. <c>null</c> = title-only entry.</summary>
    [MaxLength(1000)]
    public string? Summary { get; set; }

    /// <summary>Optional icon key (e.g., <c>credential.received</c>) for UI rendering.</summary>
    [MaxLength(64)]
    public string? IconKey { get; set; }

    /// <summary>
    /// Bit-flag set of channels the writer or default policy hints should apply
    /// to this entry. Phase 5 only delivers <see cref="ChannelHints.Inbox"/>;
    /// push / email / digest dispatchers ship in a follow-up phase.
    /// </summary>
    public ChannelHints ChannelHints { get; set; } = ChannelHints.Inbox;

    /// <summary>Optional audit field — id of the writing service principal, if any.</summary>
    public Guid? WriterServiceId { get; set; }
}

/// <summary>Domain category of an inbox entry. Drives the rendered icon + grouping.</summary>
public enum InboxCategory
{
    /// <summary>Workflow action requiring user response.</summary>
    Action = 0,

    /// <summary>Verifiable credential issued to or about the user.</summary>
    Credential = 1,

    /// <summary>Org membership change — added, removed, role changed.</summary>
    Membership = 2,

    /// <summary>Security alert — new device login, password reset, etc.</summary>
    Security = 3,

    /// <summary>Platform-wide system announcement (admin-only emit).</summary>
    System = 4,

    /// <summary>Workflow lifecycle event not requiring user response (informational).</summary>
    Workflow = 5,

    /// <summary>Open-ended bucket for future categories.</summary>
    Custom = 99
}

/// <summary>Severity level of an inbox entry. Drives visual prominence.</summary>
public enum InboxSeverity
{
    /// <summary>Informational — low prominence.</summary>
    Info = 0,

    /// <summary>Warning — yellow accent.</summary>
    Warning = 1,

    /// <summary>Action required — orange accent, toast on arrival.</summary>
    ActionRequired = 2,

    /// <summary>Critical — red accent, persistent toast until dismissed.</summary>
    Critical = 3
}

/// <summary>
/// Bit-flag set of delivery channels the writer hints (or default policy) should apply
/// to this entry. Phase 5 only delivers <see cref="Inbox"/> in process; the other channels
/// are recorded so the phase-5 follow-up dispatcher work can wire them up without a data-model migration.
/// </summary>
[Flags]
public enum ChannelHints
{
    /// <summary>No channels — entry is silent (rare; mainly tests).</summary>
    None = 0,

    /// <summary>In-app notification bell + activity panel + inbox list.</summary>
    Inbox = 1,

    /// <summary>Web Push notification via VAPID — wired in a follow-up phase.</summary>
    Push = 2,

    /// <summary>Transactional email — wired in a follow-up phase.</summary>
    Email = 4,

    /// <summary>Hourly digest grouping — wired in a follow-up phase.</summary>
    Digest = 8
}
