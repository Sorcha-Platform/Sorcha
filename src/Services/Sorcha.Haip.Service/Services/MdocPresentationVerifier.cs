// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Haip.Service.Models;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Verifies an OpenID4VP <c>mso_mdoc</c> presentation (feature 135, US2). Wraps the
/// <see cref="MdocFormatHandler"/> — which runs the ISO 18013-5 format crypto and routes the trust
/// decision through the unified <see cref="ITrustEvaluator"/> — and maps the result onto the same
/// <see cref="VerificationResult"/> the SD-JWT path produces, so the lifecycle/consumer flow is
/// format-agnostic. The <c>vp_token</c> is the base64url-encoded CBOR DeviceResponse.
/// </summary>
public sealed class MdocPresentationVerifier
{
    private readonly MdocFormatHandler _handler;
    private readonly ILogger<MdocPresentationVerifier> _logger;

    public MdocPresentationVerifier(MdocFormatHandler handler, ILogger<MdocPresentationVerifier> logger)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Verifies an mdoc <c>vp_token</c> against the OpenID4VP session (<paramref name="clientId"/>,
    /// <paramref name="nonce"/>, <paramref name="responseUri"/>) and the supplied trust policy.
    /// </summary>
    public async Task<VerificationResult> VerifyAsync(
        string vpToken,
        string clientId,
        string nonce,
        string responseUri,
        TrustPolicy? trustPolicy = null,
        List<string>? requiredClaims = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vpToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        ArgumentException.ThrowIfNullOrWhiteSpace(responseUri);

        var result = new VerificationResult();

        try
        {
            var presented = new PresentedCredential
            {
                Raw = vpToken,
                Format = CredentialFormat.MsoMdoc,
                ExpectedAudience = clientId,
                ExpectedNonce = nonce,
                ExpectedResponseUri = responseUri
            };
            var requirement = new CredentialRequirement
            {
                Type = string.Empty,
                Format = CredentialFormat.MsoMdoc,
                TrustPolicy = trustPolicy,
                RevocationCheckPolicy = RevocationCheckPolicy.FailClosed
            };

            var verify = await _handler.VerifyAsync(presented, requirement, ct);

            result.IsValid = verify.IsValid;
            result.Issuer = verify.IssuerId;
            result.VerifiedClaims = verify.DisclosedClaims;
            result.Errors.AddRange(verify.Errors);
            result.TrustEvidence = verify.Trust?.Evidence;
            result.HolderKeyVerified = verify.IsValid; // device-auth holder binding gates IsValid for mdoc
            result.X5cChainValid = verify.Trust?.DecidingSources.Any(s =>
                s is TrustSourceKind.X509Tenant or TrustSourceKind.TrustList);
            // Feature 192 — a switch, not the old equality-plus-ternary. That form silently mapped
            // every non-revoked failure to null ("no status problem"), so adding Suspended to the
            // enum would have made a suspended credential report NOTHING while still being refused
            // — strictly worse than the revocation it used to claim, and green in CI.
            result.StatusCheckResult = verify.Trust?.FailureReason switch
            {
                TrustFailureReason.Revoked => "Revoked",
                TrustFailureReason.Suspended => "Suspended",
                TrustFailureReason.RevocationUnavailable => "Unknown",
                _ => null
            };

            if (result.IsValid && requiredClaims is not null)
            {
                foreach (var claim in requiredClaims)
                {
                    if (!verify.DisclosedClaims.ContainsKey(claim))
                    {
                        result.IsValid = false;
                        result.Errors.Add($"Required claim '{claim}' not disclosed in mdoc presentation");
                    }
                }
            }

            if (!result.IsValid)
            {
                _logger.LogWarning(
                    "mdoc presentation rejected: issuer={Issuer} reason={Reason}",
                    verify.IssuerId, verify.Trust?.FailureReason);
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"mdoc verification failed: {ex.Message}");
            _logger.LogError(ex, "mdoc presentation verification threw");
        }

        return result;
    }
}
