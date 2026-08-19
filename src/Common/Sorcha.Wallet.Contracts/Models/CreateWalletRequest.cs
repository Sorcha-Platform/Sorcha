// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using Sorcha.Wallet.Contracts.Validation;

namespace Sorcha.Wallet.Contracts.Models;

/// <summary>
/// Wire contract for creating a new wallet — the single canonical shape shared by the Wallet Service, UI,
/// PWA, CLI, and service clients.
/// </summary>
/// <remarks>
/// Unlike the read/response contracts in this assembly, this type is a <b>mutable form model</b>: it is
/// two-way bound by Blazor <c>EditForm</c> inputs (<c>@bind-Value</c> needs a setter), so its properties are
/// <c>get; set;</c> rather than <c>init</c>. Mandatory fields are enforced with DataAnnotations
/// <c>[Required]</c>, which fires identically under ASP.NET minimal-API model binding and Blazor's
/// <c>DataAnnotationsValidator</c> — the correct guarantee for a user-filled form (validate on submit)
/// rather than a compile-time <c>required</c> that would force placeholder values.
/// </remarks>
public sealed record CreateWalletRequest
{
    /// <summary>Friendly name for the wallet.</summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Cryptographic algorithm (ED25519, NISTP256, RSA4096).</summary>
    [Required]
    public string Algorithm { get; set; } = string.Empty;

    /// <summary>Number of words in mnemonic (12, 15, 18, 21, or 24). Defaults to 24.</summary>
    [Bip39WordCount]
    public int WordCount { get; set; } = 24;

    /// <summary>
    /// Create this wallet as an ORGANISATION's wallet, owned by the organisation rather than by the
    /// caller. Only an Administrator of that organisation may do so (#1525).
    /// </summary>
    /// <remarks>
    /// The organisation's canonical signing wallet is what its issuer DID anchors on and what its
    /// governance roster identity is matched against, so it belongs to the organisation and outlives
    /// whoever set it up. The admin who creates it receives the BIP39 recovery phrase — shown once,
    /// never stored — and is responsible for recording it; that custody is the whole reason this is
    /// a deliberate human step rather than something the platform does on the org's behalf.
    /// </remarks>
    public Guid? OrganizationId { get; set; }

    /// <summary>Optional passphrase for additional security.</summary>
    public string? Passphrase { get; set; }

    /// <summary>Optional PQC algorithm for hybrid wallets (e.g., ML-DSA-65, SLH-DSA-128s).</summary>
    public string? PqcAlgorithm { get; set; }

    /// <summary>Enable hybrid mode: creates both classical and PQC key pairs for the wallet.</summary>
    public bool EnableHybrid { get; set; }

    /// <summary>
    /// Optional signing mode override. When provided and AllowSigningModeOverride is true, overrides the
    /// policy-determined signing mode. Values: "Local" (default — keys stored encrypted locally) or
    /// "KmsResident" (keys held in cloud KMS).
    /// </summary>
    public string? SigningMode { get; set; }

    /// <summary>Optional metadata tags.</summary>
    public Dictionary<string, string>? Tags { get; set; }
}
