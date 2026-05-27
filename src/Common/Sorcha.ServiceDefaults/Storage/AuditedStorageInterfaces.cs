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

        // Feature 118 — SignalR backplane synthetic registration. Production / Staging
        // refuse to start when a hub-hosting service has no Redis backplane: silent
        // multi-replica fan-out misses are a correctness bug, not a degraded mode.
        // Value matches Sorcha.ServiceDefaults.Hubs.SorchaHubConventions.BackplaneRegistrationInterface.
        "Sorcha.ServiceDefaults.Hubs.SignalRBackplane",

        // Feature 118 / T065 — Tenant Service durable user inbox. The bell, unread
        // counts, and SignalR fan-out all read from this store; an in-memory
        // fallback would silently lose every user-facing notification on restart.
        // Verified: matches typeof(Sorcha.Tenant.Service.Storage.IInboxStore).FullName.
        "Sorcha.Tenant.Service.Storage.IInboxStore",

        // Feature 114 / US4 — Wallet Service citizen credential-event log. Backs
        // the citizen wallet sync surface; an in-memory fallback would silently
        // drop every push-on-issuance signal AND every replay through /sync after
        // restart, leaving the wallet permanently out of date with no error path.
        // Verified: matches typeof(Sorcha.Wallet.Service.Services.Interfaces.ICitizenCredentialEventStream).FullName.
        "Sorcha.Wallet.Service.Services.Interfaces.ICitizenCredentialEventStream",
    };

    /// <summary>The literal backend label used for in-memory registrations.</summary>
    public const string InMemoryBackend = "in-memory";
}
