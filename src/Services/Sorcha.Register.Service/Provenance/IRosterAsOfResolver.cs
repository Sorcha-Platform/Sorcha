// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Provenance.Engine.Evidence;

namespace Sorcha.Register.Service.Provenance;

/// <summary>
/// Resolves the validator set that applied when a given docket was sealed.
/// </summary>
/// <remarks>
/// <b>This is the only component permitted to see a register's full roster history</b> (plan D5).
/// Everything downstream receives a single <see cref="RosterAsOf"/> and has no way to reach for
/// "the current roster" — which is how the naive implementation is made unreachable rather than
/// merely discouraged.
/// </remarks>
public interface IRosterAsOfResolver
{
    /// <summary>
    /// Returns the roster version applying at <paramref name="docketNumber"/>, or null when it
    /// cannot be established.
    /// </summary>
    /// <remarks>
    /// Null is a normal outcome, not an error: the engine renders it as
    /// <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Unverified"/> with a reason.
    /// Implementations must not throw for missing or unreadable evidence.
    /// </remarks>
    Task<RosterAsOf?> ResolveAsync(
        string registerId,
        ulong docketNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The dockets at which the validator set changed, so the spine can render network growth as
    /// history rather than leaving it to be inferred from a config file (FR-007).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A docket appears here when it seals a control transaction whose validator roster differs from
    /// the one before it. The <b>first</b> recorded roster is excluded: the genesis set is the origin
    /// of the history, not a change within it, and marking it would put a "the validator set changed
    /// here" flag on every register's first docket.
    /// </para>
    /// <para>
    /// This is a whole-register read, so it is resolved once per spine page rather than per docket.
    /// It runs no verification — the spine deliberately runs none (plan D6).
    /// </para>
    /// </remarks>
    Task<IReadOnlySet<ulong>> GetRosterChangeDocketsAsync(
        string registerId,
        CancellationToken cancellationToken = default);
}
