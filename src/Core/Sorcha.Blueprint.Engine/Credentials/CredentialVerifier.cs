// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// Verifies credential presentations against action credential requirements (feature 135).
/// Dispatches each requirement to the matching <see cref="ICredentialFormatHandler"/>, which owns
/// the format cryptography and routes the trust decision through the shared
/// <see cref="ITrustEvaluator"/>. The verifier itself only orchestrates: it matches credential
/// type and enforces required-claim constraints over the disclosed claims the handler returns.
/// The historical <c>SignatureValid=false</c> "defer to the service layer" shortcut and the flat
/// accepted-issuer match are gone — signature, issuer trust, and revocation are now decided
/// truthfully and fail-closed inside the handler/evaluator (FR-008).
/// </summary>
public class CredentialVerifier : ICredentialVerifier
{
    private readonly IReadOnlyDictionary<CredentialFormat, ICredentialFormatHandler> _handlers;

    public CredentialVerifier(IEnumerable<ICredentialFormatHandler> formatHandlers)
    {
        ArgumentNullException.ThrowIfNull(formatHandlers);
        // Last registration per format wins, matching the resolver-registry convention.
        var map = new Dictionary<CredentialFormat, ICredentialFormatHandler>();
        foreach (var handler in formatHandlers)
            map[handler.Format] = handler;
        _handlers = map;
    }

    /// <inheritdoc />
    public async Task<CredentialValidationResult> VerifyAsync(
        IEnumerable<CredentialRequirement> requirements,
        IEnumerable<CredentialPresentation> presentations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(presentations);

        var result = new CredentialValidationResult();
        var requirementList = requirements.ToList();
        var presentationList = presentations.ToList();

        if (requirementList.Count == 0)
        {
            result.IsValid = true;
            return result;
        }

        foreach (var requirement in requirementList)
        {
            if (!_handlers.TryGetValue(requirement.Format, out var handler))
            {
                result.Errors.Add(new CredentialValidationError
                {
                    RequirementType = requirement.Type,
                    FailureReason = CredentialFailureReason.IssuerNotAccepted,
                    Message = $"No credential-format handler is registered for format '{requirement.Format}'."
                });
                continue;
            }

            if (presentationList.Count == 0)
            {
                result.Errors.Add(MissingError(requirement));
                continue;
            }

            VerifiedCredentialDetail? matched = null;
            CredentialValidationError? firstFailure = null;

            foreach (var presentation in presentationList)
            {
                var outcome = await TryMatchAsync(handler, requirement, presentation, cancellationToken)
                    .ConfigureAwait(false);

                if (outcome.Detail is not null)
                {
                    matched = outcome.Detail;
                    break;
                }

                if (outcome.Error is not null)
                    firstFailure ??= outcome.Error;
            }

            if (matched is not null)
                result.VerifiedCredentials.Add(matched);
            else
                result.Errors.Add(firstFailure ?? MissingError(requirement));
        }

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    private static async Task<MatchOutcome> TryMatchAsync(
        ICredentialFormatHandler handler,
        CredentialRequirement requirement,
        CredentialPresentation presentation,
        CancellationToken cancellationToken)
    {
        var presented = new PresentedCredential
        {
            Raw = presentation.RawPresentation,
            Format = requirement.Format
        };

        var verify = await handler.VerifyAsync(presented, requirement, cancellationToken).ConfigureAwait(false);
        var credentialType = ReadCredentialType(verify.DisclosedClaims);

        // A readable type that doesn't match means the presentation belongs to a different
        // requirement — skip it without surfacing an error.
        if (credentialType is not null && !TypeMatches(credentialType, requirement.Type))
            return MatchOutcome.Skip();

        // Signature / trust / revocation are decided in the handler+evaluator. A failure here is
        // surfaced for this requirement (the type either matched or could not be read because the
        // signature did not verify).
        if (!verify.IsValid)
        {
            return MatchOutcome.Fail(new CredentialValidationError
            {
                RequirementType = requirement.Type,
                FailureReason = MapFailureReason(verify.Trust?.FailureReason),
                Message = FailureMessage(requirement, verify)
            });
        }

        // Valid but no readable type — cannot confirm it satisfies this requirement.
        if (credentialType is null)
            return MatchOutcome.Skip();

        // Required-claim constraints (presence + optional exact value) over the disclosed claims.
        if (requirement.RequiredClaims is not null)
        {
            foreach (var constraint in requirement.RequiredClaims)
            {
                if (!verify.DisclosedClaims.TryGetValue(constraint.ClaimName, out var value))
                {
                    return MatchOutcome.Fail(new CredentialValidationError
                    {
                        RequirementType = requirement.Type,
                        FailureReason = CredentialFailureReason.ClaimMismatch,
                        Message = $"Required claim '{constraint.ClaimName}' not disclosed in credential of type '{requirement.Type}'"
                    });
                }

                if (constraint.ExpectedValue is not null)
                {
                    var expected = constraint.ExpectedValue.ToString();
                    var actual = value?.ToString();
                    if (!string.Equals(expected, actual, StringComparison.Ordinal))
                    {
                        return MatchOutcome.Fail(new CredentialValidationError
                        {
                            RequirementType = requirement.Type,
                            FailureReason = CredentialFailureReason.ClaimMismatch,
                            Message = $"Claim '{constraint.ClaimName}' value '{actual}' does not match expected '{expected}'"
                        });
                    }
                }
            }
        }

        var detail = new VerifiedCredentialDetail
        {
            CredentialId = presentation.CredentialId,
            Type = credentialType,
            IssuerDid = verify.IssuerId,
            VerifiedClaims = new Dictionary<string, object>(verify.DisclosedClaims),
            SignatureValid = verify.Trust?.SignatureValid ?? true,
            RevocationStatus = "Active"
        };

        return MatchOutcome.Match(detail);
    }

    private static CredentialValidationError MissingError(CredentialRequirement requirement) => new()
    {
        RequirementType = requirement.Type,
        FailureReason = CredentialFailureReason.Missing,
        Message = $"No credential of type '{requirement.Type}' found in presentations"
    };

    private static string FailureMessage(CredentialRequirement requirement, FormatVerifyResult verify)
    {
        var detail = verify.Trust?.Message ?? (verify.Errors.Count > 0 ? string.Join("; ", verify.Errors) : null);
        return detail is { Length: > 0 }
            ? $"Credential for requirement '{requirement.Type}' was not trusted: {detail}"
            : $"Credential for requirement '{requirement.Type}' was not trusted.";
    }

    /// <summary>Maps a unified trust failure onto the verifier's coarser credential failure reason.</summary>
    private static CredentialFailureReason MapFailureReason(TrustFailureReason? reason) => reason switch
    {
        TrustFailureReason.SignatureInvalid => CredentialFailureReason.InvalidSignature,
        TrustFailureReason.Revoked => CredentialFailureReason.Revoked,
        TrustFailureReason.Suspended => CredentialFailureReason.Suspended,
        TrustFailureReason.RevocationUnavailable => CredentialFailureReason.RevocationCheckUnavailable,
        // UntrustedIssuer / ChainInvalid / SourceUnavailable / InsufficientAssurance /
        // HolderBindingInvalid / IntegrityFailure / FormatUnsupported — the issuer is not accepted.
        _ => CredentialFailureReason.IssuerNotAccepted
    };

    private static string? ReadCredentialType(Dictionary<string, object> claims)
    {
        if (claims.TryGetValue("vct", out var vct) && vct is not null)
            return vct.ToString();
        if (claims.TryGetValue("type", out var type) && type is not null)
            return type.ToString();
        return null;
    }

    private static bool TypeMatches(string credentialType, string requirementType) =>
        string.Equals(credentialType, requirementType, StringComparison.Ordinal);

    private readonly record struct MatchOutcome(VerifiedCredentialDetail? Detail, CredentialValidationError? Error)
    {
        public static MatchOutcome Match(VerifiedCredentialDetail detail) => new(detail, null);
        public static MatchOutcome Fail(CredentialValidationError error) => new(null, error);
        public static MatchOutcome Skip() => new(null, null);
    }
}
