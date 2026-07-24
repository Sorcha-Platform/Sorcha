// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Models.Auth;

/// <summary>
/// The RFC 6749 token-endpoint response issued by the Sorcha Tenant Service — the single wire
/// contract shared by the issuer and every first-party consumer (Blazor UI, CLI, service-principal
/// client, demo host).
/// </summary>
/// <remarks>
/// <para>
/// This lives in <c>Sorcha.Tenant.Models</c> — a zero-dependency leaf — so the issuer and its
/// clients name one type instead of five. It previously existed as five separate declarations
/// which had already diverged in what they admitted: the Blazor UI's copy omitted
/// <see cref="Scope"/> entirely, and the service-principal client's private copy omitted
/// <see cref="RefreshToken"/>. Each copy quietly dropped whatever field its author did not happen
/// to need, so a field added at the issuer reached no consumer until someone noticed.
/// </para>
/// <para>
/// <b><see cref="RefreshToken"/> is optional by design, not by omission.</b> RFC 6749 §4.4.3 states
/// that a client-credentials grant SHOULD NOT include a refresh token, and Sorcha's
/// service-principal flow does not issue one. Modelling it as required would be wrong for that
/// grant and would throw on deserialisation — which is precisely why the service-principal client
/// had to keep its own copy.
/// </para>
/// <para>
/// This is deliberately <b>not</b> shared with HAIP's credential-issuance token response. That is a
/// different protocol surface (OpenID4VCI): it carries <c>c_nonce</c> / <c>c_nonce_expires_in</c>
/// for credential-request proof binding and never carries a refresh token. Sharing a name across
/// two protocols was the hazard; sharing the type would have been worse.
/// </para>
/// </remarks>
public sealed record TokenResponse
{
    /// <summary>The issued access token (JWT).</summary>
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    /// <summary>Token type. Always <c>Bearer</c> for Sorcha.</summary>
    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";

    /// <summary>Access-token lifetime in seconds.</summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    /// <summary>
    /// The refresh token, when the grant issues one. <see langword="null"/> for the
    /// client-credentials (service-principal) grant — see the remarks on this type.
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    /// <summary>Space-delimited granted scopes, when the grant returns them.</summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    /// <summary>
    /// Sanity-checks a deserialised token response before it is cached or used.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the response carries a non-blank access token and a positive
    /// lifetime.
    /// </returns>
    /// <remarks>
    /// Lifted from the Blazor UI's former private copy, where it guarded every token cache write.
    /// It belongs on the shared type: every consumer deserialises this from a remote response and
    /// wants the same guard, and the UI having it while the CLI and service-principal client did
    /// not was itself a symptom of the copies drifting.
    /// </remarks>
    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(AccessToken) && ExpiresIn > 0;
}
