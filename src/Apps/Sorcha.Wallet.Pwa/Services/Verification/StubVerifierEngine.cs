// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Components.User.Services.Verification;
using LibVerifyOutcome = Sorcha.UI.Components.User.Models.Verification.VerifyOutcome;
using VerificationResult = Sorcha.UI.Components.User.Models.Verification.VerificationResult;

namespace Sorcha.Wallet.Pwa.Services.Verification;

/// <summary>
/// v1 placeholder implementation of <see cref="IVerifierEngine"/>
/// (Feature 125, PR-C). Parses a minimal demo-offer JSON envelope and
/// returns a <see cref="VerificationResult"/> shaped from the envelope
/// fields — useful for UI testing the doorstep flow before the real
/// validator extraction lands in a follow-up PR.
/// </summary>
/// <remarks>
/// Expected offer payload shape (JSON, all fields optional, sensible defaults):
/// <code>
/// {
///   "outcome": "pass" | "warn" | "fail",
///   "holderDisplayName": "Liam Buchanan",
///   "issuerOrgName": "Caledonian Water",
///   "credentialType": "WaterEngineerCredential/v1",
///   "claims": { "givenName": "Liam", "expiresAt": "2027-11-04" },
///   "messages": ["optional warning text"]
/// }
/// </code>
/// Anything that isn't valid JSON returns a Fail outcome with a plain-English
/// recovery message — the manual-entry box in <c>VerifyFlow</c> wires this
/// through the trust panel directly.
/// </remarks>
public sealed class StubVerifierEngine : IVerifierEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<StubVerifierEngine> _logger;

    /// <summary>Initialise a new stub.</summary>
    public StubVerifierEngine(ILogger<StubVerifierEngine> logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public Task<VerificationResult> VerifyAsync(VerifierEngineRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        VerificationResult result;
        try
        {
            var offer = JsonSerializer.Deserialize<StubOffer>(request.OfferPayload, JsonOptions)
                ?? new StubOffer();
            var outcome = ParseOutcome(offer.Outcome);
            var messages = offer.Messages ?? new List<string>();
            if (outcome != LibVerifyOutcome.Pass && messages.Count == 0)
                messages.Add(DefaultMessageForOutcome(outcome));

            var claims = (IReadOnlyDictionary<string, object?>?)offer.Claims
                ?? new Dictionary<string, object?>();
            var verifiedAt = DateTimeOffset.UtcNow;

            result = new VerificationResult(
                Outcome: outcome,
                HolderDisplayName: offer.HolderDisplayName ?? "Unknown holder",
                IssuerOrgName: offer.IssuerOrgName ?? "Unknown issuer",
                CredentialType: offer.CredentialType ?? "Unknown credential",
                DisclosedClaims: claims,
                Messages: messages,
                VerifiedAt: verifiedAt,
                TrustPanelJson: BuildTrustPanelJson(outcome, offer, claims, verifiedAt));
        }
        catch (JsonException ex)
        {
            _logger.LogInformation(ex, "Stub verifier couldn't parse offer payload as JSON; returning Fail.");
            result = new VerificationResult(
                Outcome: LibVerifyOutcome.Fail,
                HolderDisplayName: "Unknown holder",
                IssuerOrgName: "Unknown issuer",
                CredentialType: "Unknown credential",
                DisclosedClaims: new Dictionary<string, object?>(),
                Messages: new[] { "Couldn't read the credential. Ask the person to try again." },
                VerifiedAt: DateTimeOffset.UtcNow,
                TrustPanelJson: "{}");
        }
        return Task.FromResult(result);
    }

    private static LibVerifyOutcome ParseOutcome(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "pass" or "success" or "ok" => LibVerifyOutcome.Pass,
        "warn" or "warning" => LibVerifyOutcome.Warn,
        "fail" or "rejected" or "revoked" => LibVerifyOutcome.Fail,
        _ => LibVerifyOutcome.Pass
    };

    private static string DefaultMessageForOutcome(LibVerifyOutcome outcome) => outcome switch
    {
        LibVerifyOutcome.Warn => "Some checks couldn't complete. The credential may still be valid — try again in a moment, or ask the person to wait while you check on another network.",
        LibVerifyOutcome.Fail => "This credential could not be verified. Do not let this person in until they can prove their identity another way.",
        _ => string.Empty
    };

    private static string BuildTrustPanelJson(
        LibVerifyOutcome outcome,
        StubOffer offer,
        IReadOnlyDictionary<string, object?> claims,
        DateTimeOffset verifiedAt)
        => JsonSerializer.Serialize(new
        {
            outcome = outcome.ToString(),
            holderDisplayName = offer.HolderDisplayName,
            issuerOrgName = offer.IssuerOrgName,
            credentialType = offer.CredentialType,
            claims,
            verifiedAt
        }, JsonOptions);

    private sealed class StubOffer
    {
        public string? Outcome { get; set; }
        public string? HolderDisplayName { get; set; }
        public string? IssuerOrgName { get; set; }
        public string? CredentialType { get; set; }
        public Dictionary<string, object?>? Claims { get; set; }
        public List<string>? Messages { get; set; }
    }
}
