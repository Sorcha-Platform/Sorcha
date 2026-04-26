// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.ObjectModel;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Sorcha.CitizenWallet.Abstractions.Models;
using Microsoft.Extensions.Hosting;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Default <see cref="ICitizenSyncService"/>. Sync cursors are HMAC-SHA256 signed
/// JWTs (research §R-006) carrying <c>{ sub, lastEventSeq, iat }</c>; tokens
/// older than <see cref="MaxCursorAge"/> are rejected (caller returns 410).
/// </summary>
public sealed class CitizenSyncService : ICitizenSyncService
{
    /// <summary>Cursor JWTs older than this are stale and force a full re-sync.</summary>
    public static readonly TimeSpan MaxCursorAge = TimeSpan.FromDays(30);

    /// <summary>Threshold below which a sync response will piggy-back a delegation renewal hint.</summary>
    public static readonly TimeSpan DelegationRenewalWindow = TimeSpan.FromDays(30);

    private const string CursorIssuer = "sorcha:wallet:citizen-sync";
    private const string CursorAudience = "sorcha:wallet:citizen-sync";
    private const string CursorClaimSeq = "seq";

    private readonly ICitizenCredentialEventStream _events;
    private readonly TimeProvider _clock;
    private readonly SigningCredentials _signing;
    private readonly TokenValidationParameters _validation;
    private readonly JwtSecurityTokenHandler _handler;

    /// <summary>Initialises a new instance.</summary>
    public CitizenSyncService(
        ICitizenCredentialEventStream events,
        JwtSettings jwtSettings,
        TimeProvider clock)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentNullException.ThrowIfNull(jwtSettings);

        var keyBytes = Encoding.UTF8.GetBytes(jwtSettings.SigningKey ?? string.Empty);
        if (keyBytes.Length < 32) Array.Resize(ref keyBytes, 32);
        var key = new SymmetricSecurityKey(keyBytes);
        _signing = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        _validation = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = CursorIssuer,
            ValidateAudience = true,
            ValidAudience = CursorAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateLifetime = false, // we apply MaxCursorAge ourselves
            ClockSkew = TimeSpan.Zero,
        };
        _handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
    }

    /// <inheritdoc />
    public async Task<SyncResponse?> ComposeDeltaAsync(
        Guid platformUserId,
        string holderKeyId,
        string? sinceToken,
        CancellationToken ct = default)
    {
        long sinceSeq = 0;
        if (!string.IsNullOrEmpty(sinceToken))
        {
            var parsed = TryParseCursor(sinceToken, holderKeyId);
            if (parsed is null) return null; // 410
            sinceSeq = parsed.Value;
        }

        var newEvents = await _events.ReadAsync(platformUserId, sinceSeq, ct);
        var newest = newEvents.Count > 0
            ? newEvents[^1].Seq
            : await _events.GetHighestSeqAsync(platformUserId, ct);

        var changes = ProjectChanges(newEvents);
        var statusListsToRefresh = CollectStatusListUris(changes);
        var nextCursor = MintCursor(holderKeyId, newest);

        return new SyncResponse
        {
            SyncToken = nextCursor,
            Credentials = changes,
            Delegation = new SyncDelegationUpdate(),
            StatusListsToRefresh = new ReadOnlyCollection<string>(statusListsToRefresh.ToList()),
        };
    }

    /// <inheritdoc />
    public async Task<CredentialListResponse> ListAllCredentialsAsync(
        Guid platformUserId,
        string holderKeyId,
        CancellationToken ct = default)
    {
        var allEvents = await _events.ReadAsync(platformUserId, 0, ct);
        var snapshot = ReplayToSnapshot(allEvents);
        return new CredentialListResponse
        {
            Credentials = new ReadOnlyCollection<CachedCredentialPayload>(snapshot.ToList()),
        };
    }

    internal string MintCursor(string holderKeyId, long lastEventSeq)
    {
        var now = _clock.GetUtcNow();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = CursorIssuer,
            Audience = CursorAudience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.Add(MaxCursorAge).UtcDateTime,
            SigningCredentials = _signing,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, holderKeyId),
                new Claim(CursorClaimSeq, lastEventSeq.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            }),
        };
        return _handler.CreateEncodedJwt(descriptor);
    }

    internal long? TryParseCursor(string token, string expectedHolderKeyId)
    {
        try
        {
            var principal = _handler.ValidateToken(token, _validation, out var jwt);
            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (sub is null || !string.Equals(sub, expectedHolderKeyId, StringComparison.Ordinal))
            {
                return null;
            }

            var iat = jwt.ValidFrom;
            if (_clock.GetUtcNow().UtcDateTime - iat > MaxCursorAge) return null;

            var seqStr = principal.FindFirst(CursorClaimSeq)?.Value;
            return long.TryParse(seqStr, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var seq) ? seq : null;
        }
        catch (SecurityTokenException) { return null; }
        catch (ArgumentException) { return null; }
    }

    private static SyncCredentialChanges ProjectChanges(IReadOnlyList<CitizenCredentialEvent> events)
    {
        var added = new List<CachedCredentialPayload>();
        var revoked = new List<RevokedCredentialEntry>();
        var replaced = new List<ReplacedCredentialEntry>();
        foreach (var evt in events)
        {
            switch (evt.Kind)
            {
                case CitizenCredentialEventKind.Added when evt.Payload is CachedCredentialPayload a:
                    added.Add(a); break;
                case CitizenCredentialEventKind.Revoked when evt.Payload is RevokedCredentialEntry r:
                    revoked.Add(r); break;
                case CitizenCredentialEventKind.Replaced when evt.Payload is ReplacedCredentialEntry rp:
                    replaced.Add(rp); break;
            }
        }
        return new SyncCredentialChanges
        {
            Added = new ReadOnlyCollection<CachedCredentialPayload>(added),
            Revoked = new ReadOnlyCollection<RevokedCredentialEntry>(revoked),
            Replaced = new ReadOnlyCollection<ReplacedCredentialEntry>(replaced),
        };
    }

    private static IReadOnlyCollection<string> CollectStatusListUris(SyncCredentialChanges changes)
    {
        var uris = new HashSet<string>(StringComparer.Ordinal);
        foreach (var added in changes.Added)
        {
            if (!string.IsNullOrEmpty(added.StatusListUri)) uris.Add(added.StatusListUri);
        }
        return uris;
    }

    private static IEnumerable<CachedCredentialPayload> ReplayToSnapshot(IReadOnlyList<CitizenCredentialEvent> events)
    {
        var byId = new Dictionary<Guid, CachedCredentialPayload>();
        foreach (var evt in events)
        {
            switch (evt.Kind)
            {
                case CitizenCredentialEventKind.Added when evt.Payload is CachedCredentialPayload a:
                    byId[a.Id] = a; break;
                case CitizenCredentialEventKind.Revoked when evt.Payload is RevokedCredentialEntry r:
                    byId.Remove(r.Id); break;
                case CitizenCredentialEventKind.Replaced when evt.Payload is ReplacedCredentialEntry rp:
                    byId.Remove(rp.OldId);
                    // Replacement payload only carries the JWT; full payload arrives via subsequent Added events.
                    break;
            }
        }
        return byId.Values;
    }
}

/// <summary>
/// Empty credential event source used until the citizen-credential issuance pipeline
/// lands (US4 / Feature 114 Phase 6). Returns no events so a freshly enrolled wallet
/// gets an empty snapshot and the sync surface is still exercisable end-to-end.
/// </summary>
public sealed class EmptyCitizenCredentialEventStream : ICitizenCredentialEventStream
{
    /// <inheritdoc />
    public Task<IReadOnlyList<CitizenCredentialEvent>> ReadAsync(Guid platformUserId, long afterSeq, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CitizenCredentialEvent>>(Array.Empty<CitizenCredentialEvent>());

    /// <inheritdoc />
    public Task<long> GetHighestSeqAsync(Guid platformUserId, CancellationToken ct = default)
        => Task.FromResult(0L);
}
