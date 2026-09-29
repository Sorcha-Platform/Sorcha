// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Contracts.Models;

/// <summary>
/// Immutable wire contract asking the Wallet Service to sign an IETF Token Status List with an
/// organisation's VC-issuance key (<c>POST /api/internal/status-lists/ietf/sign</c>).
/// </summary>
/// <remarks>
/// Deliberately typed: the caller states WHAT the list says, and the Wallet Service builds the
/// whole token — header, <c>iss</c>, <c>kid</c>, <c>typ</c> — itself. A request carrying raw bytes
/// to sign would turn the org's credential key into a forgery oracle.
/// </remarks>
public sealed record SignStatusListTokenRequest
{
    /// <summary>The issuing organisation whose VC-issuance key signs the list.</summary>
    public required Guid OrganizationId { get; init; }

    /// <summary>The list's URI; becomes <c>sub</c> and MUST equal the credentials' <c>status_list.uri</c>.</summary>
    public required string Subject { get; init; }

    /// <summary>Bits per entry: 1, 2, 4 or 8.</summary>
    public required int Bits { get; init; }

    /// <summary>The packed, uncompressed entry bytes (IETF LSB-first layout), Base64-encoded.</summary>
    public required string EntriesBase64 { get; init; }

    /// <summary>Cache lifetime in seconds; <c>exp = iat + ttl</c>.</summary>
    public required int TtlSeconds { get; init; }
}
