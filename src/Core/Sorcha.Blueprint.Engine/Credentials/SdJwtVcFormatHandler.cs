// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text.Json;

using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Cryptography.SdJwt;

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// SD-JWT VC credential-format handler (feature 135). Owns the SD-JWT cryptography — resolves the
/// issuer key via <see cref="IIssuerKeyResolver"/>, verifies the issuer signature (and KB-JWT when
/// holder-binding is requested) via <see cref="ISdJwtService"/> — then delegates the trust decision
/// to the shared <see cref="ITrustEvaluator"/>. This is the seam that closes the historical
/// <c>SignatureValid=false</c> shortcut: <see cref="IssuerContext.SignatureVerified"/> is set
/// truthfully from the real verification result, and the evaluator fails closed on an unverified
/// signature.
/// </summary>
public class SdJwtVcFormatHandler : ICredentialFormatHandler
{
    private readonly ISdJwtService _sdJwtService;
    private readonly IIssuerKeyResolver _keyResolver;
    private readonly ITrustEvaluator _trustEvaluator;

    public SdJwtVcFormatHandler(
        ISdJwtService sdJwtService,
        IIssuerKeyResolver keyResolver,
        ITrustEvaluator trustEvaluator)
    {
        _sdJwtService = sdJwtService ?? throw new ArgumentNullException(nameof(sdJwtService));
        _keyResolver = keyResolver ?? throw new ArgumentNullException(nameof(keyResolver));
        _trustEvaluator = trustEvaluator ?? throw new ArgumentNullException(nameof(trustEvaluator));
    }

    /// <inheritdoc />
    public CredentialFormat Format => CredentialFormat.SdJwtVc;

    /// <inheritdoc />
    public async Task<FormatVerifyResult> VerifyAsync(
        PresentedCredential presentation,
        CredentialRequirement requirement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(requirement);

        var result = new FormatVerifyResult();

        // The handler only speaks SD-JWT VC. A non-matching format is a fail-closed rejection
        // (FR-009) — the requirement accepts exactly one format.
        if (presentation.Format != CredentialFormat.SdJwtVc || requirement.Format != CredentialFormat.SdJwtVc)
        {
            result.Errors.Add($"SD-JWT VC handler cannot verify a '{presentation.Format}' presentation against a '{requirement.Format}' requirement.");
            result.Trust = TrustDecision.Reject(TrustFailureReason.FormatUnsupported, "Credential format mismatch.");
            return result;
        }

        // Parse the issuer JWT payload once for the issuer id and any status reference. These are
        // read independently of signature verification so an unresolved/invalid signature still
        // produces a well-formed (fail-closed) decision.
        var (issuerId, statusRef) = ParseIssuerPayload(presentation.Raw);

        // Resolve the issuer key (x5c → DID → embedded jwk, service-layer; pinned in-memory, engine).
        var keyResolution = await _keyResolver.ResolveAsync(presentation.Raw, cancellationToken).ConfigureAwait(false);

        bool signatureVerified = false;
        if (keyResolution is { PublicKey.Length: > 0 })
        {
            var verifyResult = await VerifySignatureAsync(presentation, keyResolution, cancellationToken).ConfigureAwait(false);
            signatureVerified = verifyResult.IsValid;
            if (verifyResult.IsValid)
            {
                result.DisclosedClaims = verifyResult.Claims;
                if (!string.IsNullOrEmpty(verifyResult.Issuer))
                    issuerId = verifyResult.Issuer!;
            }
            else
            {
                result.Errors.AddRange(verifyResult.Errors);
            }
        }
        else
        {
            result.Errors.Add("Issuer key could not be resolved for the presented SD-JWT VC.");
        }

        var issuer = new IssuerContext
        {
            IssuerId = issuerId,
            Format = CredentialFormat.SdJwtVc,
            SigningKeyId = keyResolution?.SigningKeyId,
            X5cChain = keyResolution?.X5cChain,
            SignatureVerified = signatureVerified,
            Status = statusRef,
            RevocationPolicy = requirement.RevocationCheckPolicy
        };

        var decision = await _trustEvaluator.EvaluateAsync(issuer, requirement.TrustPolicy, cancellationToken).ConfigureAwait(false);
        TrustMetrics.RecordDecision(decision, CredentialFormat.SdJwtVc);
        result.Trust = decision;
        result.IssuerId = issuerId;
        result.IsValid = decision.IsTrusted;
        if (!decision.IsTrusted && decision.Message is { Length: > 0 })
            result.Errors.Add(decision.Message);

        return result;
    }

    private Task<SdJwtVerificationResult> VerifySignatureAsync(
        PresentedCredential presentation,
        IssuerKeyResolution key,
        CancellationToken ct)
    {
        // Holder-binding verification when the verifier supplied an audience + nonce (HAIP-style);
        // issuer-only verification for the Sorcha-internal engine path (no holder-binding gate).
        if (!string.IsNullOrEmpty(presentation.ExpectedAudience) && !string.IsNullOrEmpty(presentation.ExpectedNonce))
        {
            return _sdJwtService.VerifyPresentationAsync(
                presentation.Raw, key.PublicKey, key.Algorithm,
                presentation.ExpectedAudience!, presentation.ExpectedNonce!, ct,
                issuerRecoveryAddress: key.BlockchainAccountId);
        }

        return _sdJwtService.VerifyPresentationAsync(
            presentation.Raw, key.PublicKey, key.Algorithm, ct,
            issuerRecoveryAddress: key.BlockchainAccountId);
    }

    /// <summary>
    /// Decodes the issuer JWT payload and extracts the issuer id (<c>iss</c>) and any status
    /// reference (IETF <c>status.status_list</c> preferred, W3C <c>credentialStatus</c> fallback).
    /// Returns an empty issuer id and null status when the token cannot be parsed.
    /// </summary>
    private static (string IssuerId, StatusReference? Status) ParseIssuerPayload(string rawSdJwt)
    {
        try
        {
            var jwtPart = rawSdJwt.TrimEnd('~').Split('~')[0];
            var segments = jwtPart.Split('.');
            if (segments.Length < 2)
                return (string.Empty, null);

            using var doc = JsonDocument.Parse(Base64Url.DecodeFromChars(segments[1]));
            var payload = doc.RootElement;

            var issuerId = payload.TryGetProperty("iss", out var iss) && iss.ValueKind == JsonValueKind.String
                ? iss.GetString() ?? string.Empty
                : string.Empty;

            return (issuerId, ExtractStatusReference(payload));
        }
        catch
        {
            return (string.Empty, null);
        }
    }

    private static StatusReference? ExtractStatusReference(JsonElement payload)
    {
        // IETF Token Status List — status.status_list { uri, idx }.
        if (payload.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Object
            && status.TryGetProperty("status_list", out var statusList) && statusList.ValueKind == JsonValueKind.Object)
        {
            var uri = ReadString(statusList, "uri");
            var idx = ReadInt(statusList, "idx");
            if (uri is not null && idx is not null)
                return new StatusReference { Uri = uri, Index = idx.Value };
        }

        // W3C Bitstring Status List — credentialStatus { statusListCredential, statusListIndex, statusPurpose }.
        // The value is an OBJECT for a single purpose and an ARRAY when the credential carries one
        // entry per purpose (spec example A.3), which is how revocation and suspension are
        // expressed: W3C makes revocation not reversible and suspension reversible, so they are
        // different statuses and cannot share a bit.
        if (payload.TryGetProperty("credentialStatus", out var w3c))
        {
            if (w3c.ValueKind == JsonValueKind.Object)
                return ReadW3cEntry(w3c);

            if (w3c.ValueKind == JsonValueKind.Array)
            {
                // Prefer revocation: it is the terminal status, so if it is set no other purpose
                // changes the outcome. ExtractStatusReferences() returns them all for callers that
                // must evaluate every purpose.
                foreach (var e in w3c.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    if (!string.Equals(ReadString(e, "statusPurpose"), "revocation", StringComparison.Ordinal)) continue;
                    var preferred = ReadW3cEntry(e);
                    if (preferred is not null) return preferred;
                }

                foreach (var e in w3c.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var any = ReadW3cEntry(e);
                    if (any is not null) return any;
                }
            }
        }

        return null;
    }

    /// <summary>Reads one W3C <c>BitstringStatusListEntry</c> object into a status reference.</summary>
    private static StatusReference? ReadW3cEntry(JsonElement entry)
    {
        var uri = ReadString(entry, "statusListCredential");
        var idx = ReadInt(entry, "statusListIndex");
        if (uri is null || idx is null) return null;

        return new StatusReference
        {
            Uri = uri,
            Index = idx.Value,
            Purpose = ReadString(entry, "statusPurpose")
        };
    }

    /// <summary>
    /// Every status reference the credential declares — one per purpose.
    /// </summary>
    /// <remarks>
    /// A credential is unusable if ANY of its purposes is set: a revoked credential and a
    /// suspended one must both be refused. Checking only the first entry would let a suspended
    /// credential through whenever suspension happened to be listed second.
    /// </remarks>
    internal static IReadOnlyList<StatusReference> ExtractStatusReferences(JsonElement payload)
    {
        var all = new List<StatusReference>();

        if (payload.TryGetProperty("status", out var ietf) && ietf.ValueKind == JsonValueKind.Object
            && ietf.TryGetProperty("status_list", out var sl) && sl.ValueKind == JsonValueKind.Object)
        {
            var uri = ReadString(sl, "uri");
            var idx = ReadInt(sl, "idx");
            if (uri is not null && idx is not null)
                all.Add(new StatusReference { Uri = uri, Index = idx.Value });
        }

        if (payload.TryGetProperty("credentialStatus", out var w3c))
        {
            if (w3c.ValueKind == JsonValueKind.Object)
            {
                var one = ReadW3cEntry(w3c);
                if (one is not null) all.Add(one);
            }
            else if (w3c.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in w3c.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var one = ReadW3cEntry(e);
                    if (one is not null) all.Add(one);
                }
            }
        }

        return all;
    }

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el)
            ? el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString()
            : null;

    private static int? ReadInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(el.GetString(), out var n) => n,
            _ => null
        };
    }
}
