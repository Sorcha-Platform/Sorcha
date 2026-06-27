// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// HMAC-SHA256 implementation of <see cref="ILinkPendingTokenService"/>.
/// Wire format: <c>base64url(payload)|unixSeconds(expiresAt)|hex(HMAC-SHA256(payload|expiresAt))</c>.
/// The HMAC covers both the payload and the expiry so tampering with either fails verification.
/// </summary>
public sealed class LinkPendingTokenService : ILinkPendingTokenService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly byte[] _key;

    /// <summary>DI constructor — receives the derived signing key from <see cref="LinkPendingTokenKey"/>.</summary>
    public LinkPendingTokenService(LinkPendingTokenKey signingKey)
    {
        ArgumentNullException.ThrowIfNull(signingKey);
        _key = signingKey.Key;
    }

    /// <inheritdoc />
    public string Mint(LinkPendingToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        var payload = Serialize(token);
        var expirySeconds = token.ExpiresAt.ToUnixTimeSeconds().ToString();
        var mac = ComputeMac(payload, expirySeconds);

        return $"{payload}|{expirySeconds}|{mac}";
    }

    /// <inheritdoc />
    public bool TryVerify(string raw, out LinkPendingToken token, out LinkPendingTokenError error)
    {
        token = default!;
        error = LinkPendingTokenError.None;

        if (string.IsNullOrEmpty(raw))
        {
            error = LinkPendingTokenError.Invalid;
            return false;
        }

        var parts = raw.Split('|');
        if (parts.Length != 3)
        {
            error = LinkPendingTokenError.Invalid;
            return false;
        }

        var payload = parts[0];
        var expiryPart = parts[1];
        var macPart = parts[2];

        // Recompute and compare in constant time (CryptographicOperations.FixedTimeEquals).
        var expectedMac = ComputeMac(payload, expiryPart);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(macPart),
                Encoding.UTF8.GetBytes(expectedMac)))
        {
            error = LinkPendingTokenError.Invalid;
            return false;
        }

        // Expiry check.
        if (!long.TryParse(expiryPart, out var expiryUnix))
        {
            error = LinkPendingTokenError.Invalid;
            return false;
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiryUnix);
        if (expiresAt < DateTimeOffset.UtcNow)
        {
            error = LinkPendingTokenError.Expired;
            return false;
        }

        // Deserialise payload.
        LinkPendingToken? decoded;
        try
        {
            decoded = Deserialize(payload);
        }
        catch
        {
            error = LinkPendingTokenError.Invalid;
            return false;
        }

        if (decoded is null)
        {
            error = LinkPendingTokenError.Invalid;
            return false;
        }

        token = decoded;
        return true;
    }

    private static string Serialize(LinkPendingToken t)
    {
        var json = JsonSerializer.Serialize(new TokenPayload(
            t.Provider,
            t.Subject,
            t.SocialEmail,
            t.DisplayName,
            t.TargetAccountId,
            t.ExpiresAt.ToUnixTimeSeconds(),
            t.Surface));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static LinkPendingToken Deserialize(string base64Url)
    {
        var padded = base64Url.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        var p = JsonSerializer.Deserialize<TokenPayload>(json)
                ?? throw new InvalidOperationException("Null payload.");
        return new LinkPendingToken(
            p.Provider,
            p.Subject,
            p.SocialEmail,
            p.DisplayName,
            p.TargetAccountId,
            DateTimeOffset.FromUnixTimeSeconds(p.ExpiresAtUnix),
            p.Surface);
    }

    private string ComputeMac(string payload, string expirySeconds)
    {
        var data = Encoding.UTF8.GetBytes($"{payload}|{expirySeconds}");
        var hash = HMACSHA256.HashData(_key, data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record TokenPayload(
        string Provider,
        string Subject,
        string SocialEmail,
        string? DisplayName,
        Guid TargetAccountId,
        long ExpiresAtUnix,
        string? Surface = null);
}
