// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;

using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Cryptography.Mdoc;

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// mso_mdoc credential-format handler (feature 135, US2). Owns the ISO 18013-5 format crypto via
/// <see cref="IMdocService"/> — issuer COSE_Sign1 over the MSO, value-digest integrity, and
/// OpenID4VP holder binding — then delegates the trust decision (does the issuer x5chain reach a
/// trusted anchor, and is the credential revoked) to the shared <see cref="ITrustEvaluator"/>, so
/// trust semantics are identical to the SD-JWT VC path. Format-level failures (bad signature,
/// tampered digests, bad holder binding) fail closed before trust is consulted.
/// </summary>
public sealed class MdocFormatHandler : ICredentialFormatHandler
{
    private readonly IMdocService _mdocService;
    private readonly ITrustEvaluator _trustEvaluator;

    public MdocFormatHandler(IMdocService mdocService, ITrustEvaluator trustEvaluator)
    {
        _mdocService = mdocService ?? throw new ArgumentNullException(nameof(mdocService));
        _trustEvaluator = trustEvaluator ?? throw new ArgumentNullException(nameof(trustEvaluator));
    }

    /// <inheritdoc />
    public CredentialFormat Format => CredentialFormat.MsoMdoc;

    /// <inheritdoc />
    public async Task<FormatVerifyResult> VerifyAsync(
        PresentedCredential presentation,
        CredentialRequirement requirement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(requirement);

        var result = new FormatVerifyResult();

        if (presentation.Format != CredentialFormat.MsoMdoc || requirement.Format != CredentialFormat.MsoMdoc)
        {
            result.Errors.Add($"mso_mdoc handler cannot verify a '{presentation.Format}' presentation against a '{requirement.Format}' requirement.");
            result.Trust = TrustDecision.Reject(TrustFailureReason.FormatUnsupported, "Credential format mismatch.");
            return result;
        }

        if (string.IsNullOrEmpty(presentation.ExpectedAudience)
            || string.IsNullOrEmpty(presentation.ExpectedNonce)
            || string.IsNullOrEmpty(presentation.ExpectedResponseUri))
        {
            result.Errors.Add("mso_mdoc verification requires the OpenID4VP client_id, nonce, and response_uri.");
            result.Trust = TrustDecision.Reject(TrustFailureReason.HolderBindingInvalid, "Missing OpenID4VP session parameters.");
            return result;
        }

        byte[] deviceResponse;
        try
        {
            deviceResponse = Base64Url.DecodeFromChars(presentation.Raw);
        }
        catch (FormatException)
        {
            result.Errors.Add("vp_token is not valid base64url.");
            result.Trust = TrustDecision.Reject(TrustFailureReason.IntegrityFailure, "Malformed vp_token.");
            return result;
        }

        var transcript = new MdocSessionTranscript
        {
            ClientId = presentation.ExpectedAudience!,
            Nonce = presentation.ExpectedNonce!,
            ResponseUri = presentation.ExpectedResponseUri!,
            JwkThumbprint = presentation.ExpectedJwkThumbprint
        };

        var mdoc = _mdocService.Verify(deviceResponse, transcript);
        result.DisclosedClaims = mdoc.Claims;
        result.IssuerId = mdoc.IssuerId ?? string.Empty;
        result.Errors.AddRange(mdoc.Errors);

        // Format-level gates fail closed BEFORE trust is consulted.
        if (!mdoc.IssuerSignatureValid)
            return FailFormat(result, TrustFailureReason.SignatureInvalid, "mdoc issuer signature did not verify.", mdoc);
        if (!mdoc.DigestsValid)
            return FailFormat(result, TrustFailureReason.IntegrityFailure, "mdoc value digests did not match the disclosed items.", mdoc);
        if (!mdoc.DeviceBindingValid)
            return FailFormat(result, TrustFailureReason.HolderBindingInvalid, "mdoc device authentication did not verify.", mdoc);
        if (!mdoc.ValidityOk)
            return FailFormat(result, TrustFailureReason.IntegrityFailure, "mdoc is outside its MSO validity window.", mdoc);

        // Trust + revocation via the shared evaluator (x509-tenant / trustlist sources over the x5chain).
        var issuer = new IssuerContext
        {
            IssuerId = mdoc.IssuerId ?? string.Empty,
            Format = CredentialFormat.MsoMdoc,
            SignatureVerified = true,
            X5cChain = mdoc.X5cChain,
            Status = mdoc.Status is null ? null : new StatusReference { Uri = mdoc.Status.Uri, Index = (int)mdoc.Status.Idx },
            RevocationPolicy = requirement.RevocationCheckPolicy
        };

        var decision = await _trustEvaluator.EvaluateAsync(issuer, requirement.TrustPolicy, cancellationToken).ConfigureAwait(false);
        TrustMetrics.RecordDecision(decision, CredentialFormat.MsoMdoc);
        result.Trust = decision;
        result.IsValid = decision.IsTrusted;
        if (!decision.IsTrusted && decision.Message is { Length: > 0 })
            result.Errors.Add(decision.Message);

        return result;
    }

    /// <summary>Records a fail-closed format-gate rejection on both the trust decision and the error list.</summary>
    private static FormatVerifyResult FailFormat(
        FormatVerifyResult result, TrustFailureReason reason, string message, MdocVerificationResult mdoc)
    {
        result.IsValid = false;
        result.Trust = new TrustDecision
        {
            IsTrusted = false,
            SignatureValid = mdoc.IssuerSignatureValid,
            FailureReason = reason,
            Message = message,
            Evidence = new TrustEvidence { IssuerId = mdoc.IssuerId ?? string.Empty }
        };
        if (!result.Errors.Contains(message))
            result.Errors.Add(message);
        return result;
    }
}
