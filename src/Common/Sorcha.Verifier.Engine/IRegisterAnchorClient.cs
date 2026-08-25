// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Verification.Abstractions;
using Sorcha.Verifier.Engine.Models;

namespace Sorcha.Verifier.Engine;

/// <summary>
/// Layer-4 register-anchor cross-check (Feature 163). Calls the PUBLIC anchor-read endpoint on the
/// Register Service to locate a credential's issuance transaction by the credential's own id, then
/// re-verifies the returned Merkle inclusion proof against the public verify endpoint. No auth, no
/// operator configuration — the "open" path.
/// </summary>
public interface IRegisterAnchorClient
{
    /// <summary>Resolve and verify a credential's anchor on the public register.</summary>
    Task<RegisterAnchorResult> CheckAsync(string registerId, string credentialId, CancellationToken ct = default);
}

/// <summary>Outcome of the register-anchor cross-check, surfaced as the RegisterAnchor trail layer.</summary>
public sealed record RegisterAnchorResult
{
    /// <summary>True when the issuance tx was found AND its inclusion proof verified.</summary>
    public required bool Anchored { get; init; }

    /// <summary>Verified (anchored), Failed (proof invalid), or Unverified (not found / unreachable).</summary>
    public required VerificationStatus Status { get; init; }

    /// <summary>Issuance transaction id, when found.</summary>
    public string? TxId { get; init; }

    /// <summary>Sealing docket number, when found.</summary>
    public ulong? DocketNumber { get; init; }

    /// <summary>UTC time the docket sealed, when found.</summary>
    public DateTimeOffset? SealedAt { get; init; }

    /// <summary>Transaction lifecycle status (Active/Revoked/Superseded), when found.</summary>
    public string? LifecycleStatus { get; init; }

    /// <summary>The exportable verification bundle JSON, when available.</summary>
    public string? BundleJson { get; init; }

    /// <summary>Human-readable note for the trail detail.</summary>
    public string? Note { get; init; }
}

/// <summary>
/// HTTP implementation. The Register Service base address comes from configuration
/// (<c>RegisterService:PublicBaseUrl</c>, e.g. the API gateway origin); the verifier reaches the
/// public register endpoints over the network.
/// </summary>
public sealed class RegisterAnchorClient(
    HttpClient http,
    IConfiguration configuration,
    ILogger<RegisterAnchorClient> logger) : IRegisterAnchorClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<RegisterAnchorResult> CheckAsync(string registerId, string credentialId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(registerId) || string.IsNullOrWhiteSpace(credentialId))
        {
            return Unverified("Credential carries no register anchor reference.");
        }

        var baseUrl = configuration["RegisterService:PublicBaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            logger.LogWarning("RegisterService:PublicBaseUrl not configured; cannot perform register-anchor check.");
            return Unverified("Register endpoint not configured.");
        }

        try
        {
            var anchorUrl = $"{baseUrl}/api/registers/{Uri.EscapeDataString(registerId)}/credentials/{Uri.EscapeDataString(credentialId)}/anchor";
            using var resp = await http.GetAsync(anchorUrl, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                return Unverified("No issuance transaction found on the register for this credential.");
            }
            resp.EnsureSuccessStatusCode();

            var anchor = await resp.Content.ReadFromJsonAsync<CredentialAnchorDto>(JsonOpts, ct);
            if (anchor?.InclusionProof is null)
            {
                return Unverified("Anchor response missing inclusion proof.");
            }

            var verifyUrl = $"{baseUrl}/api/registers/{Uri.EscapeDataString(registerId)}/inclusion-proofs/verify";
            using var verifyResp = await http.PostAsJsonAsync(verifyUrl, new
            {
                transactionHash = anchor.InclusionProof.TransactionHash,
                merkleRoot = anchor.InclusionProof.MerkleRoot,
                proofPath = anchor.InclusionProof.ProofPath,
                // Issue #1372 — ask the LEDGER question too. Without a docket number this endpoint
                // only folds the proof path, and a path always folds to SOME root: `isValid` alone
                // says the arithmetic is sound, never that the root is one this register sealed.
                docketNumber = anchor.DocketNumber,
            }, JsonOpts, ct);

            var proofValid = false;
            string? ledgerAnchored = null;
            if (verifyResp.IsSuccessStatusCode)
            {
                var verdict = await verifyResp.Content.ReadFromJsonAsync<ProofVerifyDto>(JsonOpts, ct);
                proofValid = verdict?.IsValid ?? false;
                ledgerAnchored = verdict?.LedgerAnchored;
            }

            if (!proofValid)
            {
                return new RegisterAnchorResult
                {
                    Anchored = false,
                    Status = VerificationStatus.Failed,
                    TxId = anchor.TxId,
                    DocketNumber = anchor.DocketNumber,
                    SealedAt = anchor.SealedAt,
                    LifecycleStatus = anchor.Status,
                    Note = "Inclusion proof did not verify.",
                };
            }

            // A contradicted anchor is a FAILURE — the proof is sound and describes a different
            // ledger than this one. Anything short of an affirmative "verified" is UNVERIFIED, never
            // Verified: reporting a check that could not run as a pass is the one output this layer
            // must never produce (Feature 188's tri-state discipline).
            if (string.Equals(ledgerAnchored, "failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(anchor.SealStatus, "failed", StringComparison.OrdinalIgnoreCase))
            {
                return new RegisterAnchorResult
                {
                    Anchored = false,
                    Status = VerificationStatus.Failed,
                    TxId = anchor.TxId,
                    DocketNumber = anchor.DocketNumber,
                    SealedAt = anchor.SealedAt,
                    LifecycleStatus = anchor.Status,
                    Note = $"Docket #{anchor.DocketNumber} does not match the Merkle root its validator sealed.",
                };
            }

            var sealConfirmed = string.Equals(ledgerAnchored, "verified", StringComparison.OrdinalIgnoreCase);

            return new RegisterAnchorResult
            {
                Anchored = sealConfirmed,
                Status = sealConfirmed ? VerificationStatus.Verified : VerificationStatus.Unverified,
                TxId = anchor.TxId,
                DocketNumber = anchor.DocketNumber,
                SealedAt = anchor.SealedAt,
                LifecycleStatus = anchor.Status,
                Note = sealConfirmed
                    ? $"Anchored in docket #{anchor.DocketNumber}, matching the root its validator sealed."
                    : $"Inclusion proof verifies for docket #{anchor.DocketNumber}, but the register did not "
                      + "confirm it against the root the validator sealed.",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Register-anchor check failed for {RegisterId}/{CredentialId}", registerId, credentialId);
            return Unverified("Register unreachable.");
        }
    }

    private static RegisterAnchorResult Unverified(string note) => new()
    {
        Anchored = false,
        Status = VerificationStatus.Unverified,
        Note = note,
    };

    private sealed record CredentialAnchorDto(
        string RegisterId,
        string CredentialId,
        string TxId,
        ulong DocketNumber,
        DateTimeOffset SealedAt,
        string Status,
        MerkleInclusionProofDto? InclusionProof,
        // Issue #1372. Absent on an older node, which lands as null => Unverified, not Verified.
        string? SealStatus = null);

    private sealed record MerkleInclusionProofDto(
        string TransactionHash,
        string MerkleRoot,
        [property: JsonPropertyName("proofPath")] JsonElement ProofPath);

    private sealed record ProofVerifyDto(
        [property: JsonPropertyName("isValid")] bool IsValid,
        [property: JsonPropertyName("ledgerAnchored")] string? LedgerAnchored = null);
}
