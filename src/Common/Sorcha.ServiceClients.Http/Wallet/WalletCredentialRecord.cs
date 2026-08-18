// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.ServiceClients.Wallet;

/// <summary>
/// The shape the Wallet Service actually returns when a stored credential is READ
/// (<c>GET /api/v1/wallets/{walletAddress}/credentials/{credentialId}</c>).
/// </summary>
/// <remarks>
/// <para>
/// This exists because the read shape is NOT the issuance shape, and conflating the two broke
/// credential lifecycle management platform-wide (issue #1475).
/// </para>
/// <para>
/// The read endpoint returns the persisted <c>CredentialEntity</c> directly: <c>id</c> and
/// <c>claimsJson</c> (a serialised JSON string). <see cref="CredentialIssuanceResult"/> — the
/// ISSUANCE response — declares <c>required</c> <c>credentialId</c> and <c>claims</c> (an object).
/// Deserialising a read response into the issuance type therefore threw
/// <c>"missing required properties including: 'credentialId', 'claims'"</c>, the exception was
/// swallowed into <c>null</c>, and the caller reported a bodiless 404 for a credential that
/// plainly existed. Revoke / suspend / reinstate / refresh were all dead as a result.
/// </para>
/// <para>
/// Both sides were individually correct: the issuance path genuinely returns
/// <c>credentialId</c>/<c>claims</c>, so the shared type was right there and nothing verified the
/// read join. Keep the two shapes separate — do NOT relax
/// <see cref="CredentialIssuanceResult"/> to accommodate this one, because that would weaken the
/// contract on the working issuance path.
/// </para>
/// <para>
/// Every property here is optional. A read DTO that hard-requires fields reintroduces exactly the
/// failure mode it exists to fix: a future wallet-side rename would once again surface as
/// "credential not found" rather than as a mapping error.
/// </para>
/// </remarks>
public sealed class WalletCredentialRecord
{
    /// <summary>Credential identifier — the wallet calls this <c>id</c>, not <c>credentialId</c>.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Credential type URI (the <c>vct</c>).</summary>
    public string? Type { get; init; }

    /// <summary>Issuer DID or wallet address.</summary>
    public string? IssuerDid { get; init; }

    /// <summary>Recipient DID or wallet address.</summary>
    public string? SubjectDid { get; init; }

    /// <summary>Claims as a serialised JSON object string — the wallet does not expand these.</summary>
    public string? ClaimsJson { get; init; }

    /// <summary>When the credential was issued.</summary>
    public DateTimeOffset IssuedAt { get; init; }

    /// <summary>When the credential expires; null for no expiry.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>The complete SD-JWT VC token.</summary>
    public string? RawToken { get; init; }

    /// <summary>
    /// Current status. Deliberately typed as <see cref="string"/> rather than the
    /// <c>CredentialStatus</c> enum: the wire values are kebab-case-lower
    /// (<c>active</c>, <c>pending-acceptance</c>) and a value this client does not yet know about
    /// must not fail the whole read.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>Register the credential was issued against.</summary>
    public string? RegisterId { get; init; }

    /// <summary>Bitstring Status List URL carrying this credential's revocation status.</summary>
    public string? StatusListUrl { get; init; }

    /// <summary>Index position in the Bitstring Status List.</summary>
    public int? StatusListIndex { get; init; }

    /// <summary>Serialised issuer-defined display config.</summary>
    public string? DisplayConfigJson { get; init; }

    /// <summary>
    /// Project this read record onto <see cref="CredentialIssuanceResult"/>, the shape the
    /// credential-lifecycle endpoints consume.
    /// </summary>
    /// <returns>
    /// The mapped result, or <c>null</c> when the record carries no identifier — a response that
    /// cannot be identified is not a usable credential.
    /// </returns>
    public CredentialIssuanceResult? ToIssuanceResult()
    {
        if (string.IsNullOrWhiteSpace(Id)) return null;

        return new CredentialIssuanceResult
        {
            CredentialId = Id,
            Type = Type ?? string.Empty,
            IssuerDid = IssuerDid ?? string.Empty,
            SubjectDid = SubjectDid ?? string.Empty,
            Claims = ParseClaims(ClaimsJson),
            IssuedAt = IssuedAt,
            ExpiresAt = ExpiresAt,
            RawToken = RawToken ?? string.Empty,
            Status = NormaliseStatus(Status),
            RegisterId = RegisterId,
            StatusListUrl = StatusListUrl,
            StatusListIndex = StatusListIndex,
            DisplayConfigJson = DisplayConfigJson
        };
    }

    /// <summary>
    /// Translate the wallet's wire spelling of a credential status into the PascalCase form
    /// <see cref="CredentialIssuanceResult.Status"/> documents (<c>Active</c>, <c>Suspended</c>,
    /// <c>Revoked</c>, <c>Expired</c>, <c>Consumed</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The wallet serialises <c>CredentialStatus</c> through <c>SorchaJson.Options</c>, whose enum
    /// converter is <c>JsonNamingPolicy.KebabCaseLower</c> — so the wire carries <c>"active"</c> and
    /// <c>"pending-acceptance"</c>, not <c>"Active"</c> and <c>"PendingAcceptance"</c>.
    /// </para>
    /// <para>
    /// Passing that through unchanged fails the lifecycle gate
    /// <c>Status is not ("Active" or "Suspended")</c>, which is an exact, case-sensitive match: an
    /// Active credential is refused with <i>"Credential must be in Active or Suspended state to
    /// revoke"</i> while every surface shows it as active. That was the SECOND defect behind #1475
    /// and stayed hidden because the first one (the read shape) meant the gate was never reached.
    /// </para>
    /// <para>
    /// Normalising here rather than relaxing the gate is deliberate. The gate is a real
    /// authorisation decision — <c>pending-acceptance</c> and <c>revoked</c> must keep failing it —
    /// so it should compare canonical values, and translating a wire representation into the
    /// target type's documented contract is exactly what a mapping layer is for.
    /// </para>
    /// </remarks>
    private static string NormaliseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return "Active";

        // Already PascalCase (no separator, leading capital) — leave it alone.
        if (!status.Contains('-') && char.IsUpper(status[0])) return status;

        var segments = status.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(segments.Select(s =>
            char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant()));
    }

    /// <summary>
    /// Expand the wallet's serialised <c>claimsJson</c> string into the claims dictionary the
    /// issuance shape exposes. Returns an empty dictionary rather than throwing: the lifecycle
    /// endpoints authorise on <c>IssuerDid</c>, so unreadable claims must not block a revocation.
    /// </summary>
    private static Dictionary<string, object> ParseClaims(string? claimsJson)
    {
        if (string.IsNullOrWhiteSpace(claimsJson)) return new Dictionary<string, object>();

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(claimsJson);
            return parsed ?? new Dictionary<string, object>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, object>();
        }
    }
}
