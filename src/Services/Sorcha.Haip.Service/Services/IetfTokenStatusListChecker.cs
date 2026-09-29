// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Verifier.Engine;

using CredentialStatusValue = Sorcha.Blueprint.Engine.Credentials.CredentialStatusValue;
using EngineCredentials = Sorcha.Blueprint.Engine.Credentials;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Feature 095 US4 — HAIP's reader for IETF Token Status Lists: fetches the list and reads the
/// credential's entry, after the engine's shared <see cref="StatusListTokenVerifier"/> has decided
/// the list may be believed.
/// </summary>
/// <remarks>
/// #1768 — this used to verify the list against the JWK embedded in the list's OWN header, and never
/// pinned it to the credential's issuer: a forger could sign an all-VALID list with any key and embed
/// it. The key now comes from the credential issuer's DID by <c>kid</c>, and <c>iss</c>/<c>sub</c> are
/// pinned — the same rule the engine's status cache applies, because it is the same code.
/// </remarks>
public sealed class IetfTokenStatusListChecker : EngineCredentials.IStatusListChecker
{
    private readonly HttpClient _httpClient;
    private readonly StatusListTokenVerifier _verifier;
    private readonly ILogger<IetfTokenStatusListChecker> _logger;

    /// <summary>Initialises a new instance of the <see cref="IetfTokenStatusListChecker"/> class.</summary>
    public IetfTokenStatusListChecker(
        HttpClient httpClient, StatusListTokenVerifier verifier, ILogger<IetfTokenStatusListChecker> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Reads the status of the referenced entry, or <see cref="CredentialStatusValue.Unresolved"/> when
    /// the list cannot be fetched or believed. The caller's policy decides what Unresolved means; HAIP
    /// runs FailClosed.
    /// </summary>
    public async Task<CredentialStatusValue> CheckAsync(
        EngineCredentials.StatusReference statusRef, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statusRef);
        if (string.IsNullOrWhiteSpace(statusRef.Uri) || statusRef.Index < 0)
            return CredentialStatusValue.Unresolved;
        if (string.IsNullOrWhiteSpace(statusRef.ExpectedIssuer))
        {
            _logger.LogWarning(
                "IETF status list {Uri}: no expected issuer to pin it to, so it cannot be authenticated", statusRef.Uri);
            return CredentialStatusValue.Unresolved;
        }

        string jwt;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, statusRef.Uri);
            request.Headers.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/statuslist+jwt"));
            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "IETF status list fetch failed: {StatusCode} {ReasonPhrase} for {Uri}",
                    (int)response.StatusCode, response.ReasonPhrase, statusRef.Uri);
                return CredentialStatusValue.Unresolved;
            }
            jwt = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "IETF status list fetch errored for {Uri}", statusRef.Uri);
            return CredentialStatusValue.Unresolved;
        }

        var verified = await _verifier.VerifyAsync(jwt, statusRef.Uri, statusRef.ExpectedIssuer, cancellationToken);
        return verified.IsVerified
            ? ReadBit(verified.Entries, statusRef.Index, verified.Bits)
            : CredentialStatusValue.Unresolved;
    }

    /// <summary>
    /// Reads the STATUS VALUE of entry <paramref name="idx"/> from a bitstring where each entry
    /// takes <paramref name="bitsPerEntry"/> bits, in the IETF Token Status List layout: entries fill
    /// each byte from its least significant bit (RFC 9972 §4.1; #1761).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Feature 192. This method used to return "set" if ANY bit in the entry was set, which made
    /// <c>0x02</c> SUSPENDED and <c>0x01</c> INVALID indistinguishable BY CONSTRUCTION — the read
    /// side threw away the distinction #1492 had just gone to the trouble of encoding correctly on
    /// the write side. It now accumulates the entry's bits into its value and reports that.
    /// </para>
    /// <para>
    /// The accumulation order mirrors <c>IetfStatusListPacker.PackTwoBit</c>, which writes the
    /// high bit at <c>2i</c> and the low bit at <c>2i+1</c>, so the two round-trip.
    /// </para>
    /// <para>
    /// A value the spec reserves for application-specific use (<c>0x03</c> and above) is reported
    /// as <see cref="CredentialStatusValue.Unresolved"/>, NOT as a status. We genuinely cannot
    /// interpret it, and "I could not tell" is the honest answer — the caller's fail-closed policy
    /// then decides, which for the default FailClosed still refuses.
    /// </para>
    /// </remarks>
    internal static CredentialStatusValue ReadBit(byte[] raw, int idx, int bitsPerEntry)
    {
        // One home for the IETF bit layout (least-significant bit first, RFC 9972 §4.1, #1761): the
        // engine's shared reader. Null covers both a width the spec does not permit and an index past
        // the end — neither may be guessed into a status.
        if (StatusListTokenVerifier.ReadEntry(raw, bitsPerEntry, idx) is not { } value)
            return CredentialStatusValue.Unresolved;

        return value switch
        {
            IetfStatusValue.Valid => CredentialStatusValue.Valid,
            IetfStatusValue.Invalid => CredentialStatusValue.Invalid,
            IetfStatusValue.Suspended => CredentialStatusValue.Suspended,
            _ => CredentialStatusValue.Unresolved
        };
    }

    /// <summary>
    /// The IETF Token Status List status values this verifier understands. Mirrors the write-side
    /// constants on <c>IetfStatusListPacker</c>, which lives in the Blueprint Service — the two
    /// services do not share an assembly, so the values are pinned by
    /// <c>IetfStatusValueReadTests</c> on each side rather than by a shared type.
    /// </summary>
    private static class IetfStatusValue
    {
        public const int Valid = 0x00;
        public const int Invalid = 0x01;
        public const int Suspended = 0x02;
    }
}
