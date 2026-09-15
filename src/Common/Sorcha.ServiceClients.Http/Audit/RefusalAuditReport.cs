// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Audit;

/// <summary>
/// A refusal one service reports to the organisation's audit log (#1648), so the person who was
/// refused (and an MCP agent acting for them) can learn why without reading service logs.
/// </summary>
/// <remarks>
/// <para>
/// The single wire contract for <c>POST /api/internal/audit/refusals</c>: the Tenant Service binds
/// this type directly, so the writer and the endpoint cannot drift apart.
/// </para>
/// <para>
/// Deliberately carries no "service" field. The Tenant Service records the writer from the
/// service TOKEN, so a body cannot claim to be another service. Every entry it produces is a
/// refusal (<c>PermissionDenied</c>, <c>Success = false</c>): a compromised writer can add refusal
/// noise, but can never forge a record that something was allowed.
/// </para>
/// </remarks>
public sealed record RefusalAuditReport
{
    /// <summary>Longest accepted <see cref="Action"/>.</summary>
    public const int MaxActionLength = 100;

    /// <summary>Longest accepted <see cref="ResourceType"/>.</summary>
    public const int MaxResourceTypeLength = 64;

    /// <summary>Longest accepted <see cref="ResourceId"/>.</summary>
    public const int MaxResourceIdLength = 256;

    /// <summary>Longest accepted <see cref="Reason"/>.</summary>
    public const int MaxReasonLength = 1024;

    /// <summary>The organisation of the caller who was refused (from the caller's token).</summary>
    public required Guid OrganizationId { get; init; }

    /// <summary>The platform user who was refused, when the caller was a person.</summary>
    public Guid? PlatformUserId { get; init; }

    /// <summary>What was attempted. Use a <see cref="RefusalAuditActions"/> constant.</summary>
    public required string Action { get; init; }

    /// <summary>The kind of resource acted on, e.g. <c>wallet</c> or <c>register</c>.</summary>
    public string? ResourceType { get; init; }

    /// <summary>The resource acted on, e.g. a wallet address or register id.</summary>
    public string? ResourceId { get; init; }

    /// <summary>Why it was refused, in words the refused person can act on. Never secrets.</summary>
    public required string Reason { get; init; }

    /// <summary>When the refusal happened; the Tenant Service uses its own clock when absent.</summary>
    public DateTimeOffset? OccurredAt { get; init; }
}

/// <summary>
/// The one home for refusal action names, so writers and the agents filtering on them agree.
/// </summary>
public static class RefusalAuditActions
{
    /// <summary>Signing with a wallet (<c>POST /api/v1/wallets/{address}/sign</c>).</summary>
    public const string WalletSign = "wallet.sign";

    /// <summary>Any other wallet-scoped operation refused by the wallet ownership gate.</summary>
    public const string WalletAccess = "wallet.access";

    /// <summary>Publishing a blueprint to a register.</summary>
    public const string BlueprintPublish = "blueprint.publish";

    /// <summary>Amending (cloning) a published blueprint.</summary>
    public const string BlueprintAmend = "blueprint.amend";
}
