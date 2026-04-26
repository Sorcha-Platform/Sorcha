// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Resolves "the wallet that derives the slot-109
/// (<c>sorcha:citizen-status-signing</c>) key for the given organisation"
/// (Feature 114). Lazily provisions a system wallet on first call so the
/// citizen-wallet enrolment endpoint never has to deal with bootstrap.
/// </summary>
/// <remarks>
/// One status-signing wallet per org keeps verifier-side trust manageable —
/// every list issued for a given org carries a stable kid that the verifier
/// can pin. Citizens never derive these; this is org-internal infrastructure.
/// </remarks>
public interface IOrgStatusSigningWalletResolver
{
    /// <summary>
    /// Returns the wallet address whose slot-109 key signs the given org's
    /// citizen-device status lists. Idempotent — first call provisions a fresh
    /// ED25519 system wallet (fast signing, deterministic derivation), every
    /// subsequent call returns the same address.
    /// </summary>
    Task<string> ResolveAsync(Guid organizationId, CancellationToken ct = default);
}
