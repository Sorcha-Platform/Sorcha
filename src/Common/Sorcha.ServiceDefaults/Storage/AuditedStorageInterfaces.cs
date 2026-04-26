// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceDefaults.Storage;

/// <summary>
/// Storage-interface names that trigger fail-fast in Production/Staging
/// when registered with an in-memory implementation.
/// </summary>
/// <remarks>
/// <para>
/// Membership is intentionally explicit and code-defined. Adding to or
/// removing from this set is a reviewable code change rather than a runtime
/// configuration knob, so the audit boundary cannot drift quietly.
/// </para>
/// <para>
/// Cache-style stores (<c>IBlueprintStore</c>, <c>IPublishedBlueprintStore</c>,
/// <c>BlueprintCache</c>, <c>ValidatorRegistry</c>, in-process routing
/// tables, etc.) reload from authoritative sources on cold start — losing
/// them is a cold start, not data loss — so they are intentionally absent
/// from this list. They still emit the <c>[STORAGE-FALLBACK]</c> warning via
/// <see cref="IStorageRegistrationLog.RegisterInMemory"/>, but do not
/// trigger fail-fast.
/// </para>
/// </remarks>
internal static class AuditedStorageInterfaces
{
    /// <summary>
    /// Fully-qualified names of the storage interfaces audited by feature 113.
    /// </summary>
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.Ordinal)
    {
        // Wallet Service — user wallets, HD-derived keys, signing material.
        // Verified: matches typeof(Sorcha.Wallet.Core.Repositories.Interfaces.IWalletRepository).FullName.
        "Sorcha.Wallet.Core.Repositories.Interfaces.IWalletRepository",

        // Register Service — register documents and transaction stream.
        // Verified: matches typeof(Sorcha.Register.Core.Storage.IRegisterRepository).FullName.
        "Sorcha.Register.Core.Storage.IRegisterRepository",

        // Blueprint Service — workflow instances.
        // Verified: matches typeof(Sorcha.Blueprint.Service.Storage.IInstanceStore).FullName.
        "Sorcha.Blueprint.Service.Storage.IInstanceStore",

        // Blueprint Service — per-action state.
        // Verified: matches typeof(Sorcha.Blueprint.Service.Storage.IActionStore).FullName.
        "Sorcha.Blueprint.Service.Storage.IActionStore",

        // Validator Service — verified-but-not-yet-sealed mempool.
        // Verified: matches typeof(Sorcha.Validator.Service.Services.Interfaces.IVerifiedTransactionQueue).FullName.
        // (Earlier PR-7 placeholder used .Storage. — wrong; caught by claude-review on PR #419.)
        "Sorcha.Validator.Service.Services.Interfaces.IVerifiedTransactionQueue",

        // HAIP and other consumers — atomic distributed cache for replay-protection state.
        // Verified: matches typeof(Sorcha.AtomicCache.IAtomicDistributedCache).FullName.
        "Sorcha.AtomicCache.IAtomicDistributedCache",
    };

    /// <summary>The literal backend label used for in-memory registrations.</summary>
    public const string InMemoryBackend = "in-memory";
}
