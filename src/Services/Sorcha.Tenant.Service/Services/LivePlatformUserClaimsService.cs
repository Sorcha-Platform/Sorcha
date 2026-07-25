// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Resolves the <b>current</b> value of named identity claims for a platform user, straight from
/// server state.
/// </summary>
/// <remarks>
/// <para>
/// A JWT claim is a snapshot taken when the token was minted. That is fine for authentication, and
/// wrong for any decision that must reflect reality at the moment it is made. Issue #1264 is the
/// concrete cost: a citizen's token was minted at signup carrying <c>email_verified: false</c>, they
/// verified nine minutes later, submitted five minutes after that, and their application was
/// rejected on the stale value. Verifying updates server state but cannot rewrite a token already
/// issued, and nothing re-mints it.
/// </para>
/// <para>
/// This is deliberately <b>one batch query keyed by claim name</b> rather than an endpoint per
/// checkable attribute: a caller resolving several bindings makes a single round trip, and adding a
/// newly-resolvable attribute is a mapping entry here — never a new route.
/// </para>
/// <para>
/// The vocabulary is the <b>JWT claim vocabulary</b> — the same names <see cref="TokenService"/>
/// mints. That symmetry is the point: a consumer asks for "the live value of the claim I would
/// otherwise have read off the token". A name the token never carries does not belong here; add it
/// when it is minted (e.g. <c>phone_verified</c> arrives with the F150 SMS-OTP work), so the two
/// sides cannot drift into separate vocabularies.
/// </para>
/// </remarks>
public interface ILivePlatformUserClaimsService
{
    /// <summary>
    /// Resolves the requested claim names for <paramref name="platformUserId"/>.
    /// </summary>
    /// <param name="platformUserId">The cross-org platform user to read.</param>
    /// <param name="names">
    /// Claim names to resolve. Names outside <see cref="LivePlatformUserClaimsService.SupportedNames"/>
    /// are <b>omitted</b> from the result rather than guessed at, so a consumer fails closed on them
    /// visibly instead of silently receiving a wrong value.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The resolved name → value pairs (claim values are strings, as on a token), or
    /// <see langword="null"/> when no such platform user exists.
    /// </returns>
    Task<IReadOnlyDictionary<string, string>?> ResolveAsync(
        Guid platformUserId,
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class LivePlatformUserClaimsService : ILivePlatformUserClaimsService
{
    /// <summary>Claim name for the account's email-verification state.</summary>
    public const string EmailVerified = "email_verified";

    /// <summary>Claim name for the account's email address.</summary>
    public const string Email = "email";

    /// <summary>Claim name for the account's display name.</summary>
    public const string Name = "name";

    /// <summary>
    /// Every claim name this service can resolve. Consumers use it to validate a request up front,
    /// and the test suite derives its expectations from it so a name declared here but not actually
    /// resolved below fails the build rather than silently omitting at runtime.
    /// </summary>
    public static readonly IReadOnlySet<string> SupportedNames =
        new HashSet<string>(StringComparer.Ordinal) { EmailVerified, Email, Name };

    private readonly TenantDbContext _db;

    /// <summary>Initialises a new instance.</summary>
    public LivePlatformUserClaimsService(TenantDbContext db)
        => _db = db ?? throw new ArgumentNullException(nameof(db));

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>?> ResolveAsync(
        Guid platformUserId,
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);

        // Project only the columns any supported name could need — one row, no entity tracking.
        var row = await _db.PlatformUsers
            .AsNoTracking()
            .Where(u => u.Id == platformUserId)
            .Select(u => new { u.EmailVerified, u.Email, u.DisplayName })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            // Indistinguishable from "exists but nothing resolvable" is NOT wanted here: the caller
            // must be able to tell "no such user" (a bug or a stale id) from "user with no verified
            // email", because the two demand different handling.
            return null;
        }

        var resolved = new Dictionary<string, string>(names.Count, StringComparer.Ordinal);
        foreach (var name in names)
        {
            switch (name)
            {
                case EmailVerified:
                    // Same wire shape TokenService mints, so a consumer parses one format only.
                    resolved[EmailVerified] = row.EmailVerified ? "true" : "false";
                    break;
                case Email:
                    resolved[Email] = row.Email;
                    break;
                case Name:
                    resolved[Name] = row.DisplayName;
                    break;
                default:
                    // Unsupported — omitted deliberately. See the interface docs.
                    break;
            }
        }

        return resolved;
    }
}
