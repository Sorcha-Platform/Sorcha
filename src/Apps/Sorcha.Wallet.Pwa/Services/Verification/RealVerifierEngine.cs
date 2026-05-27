// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Components.User.Services.Verification;
using Sorcha.Verifier.Engine;
using Sorcha.Verifier.Engine.Models;
using LibVerifyOutcome = Sorcha.UI.Components.User.Models.Verification.VerifyOutcome;
using LibVerificationResult = Sorcha.UI.Components.User.Models.Verification.VerificationResult;

namespace Sorcha.Wallet.Pwa.Services.Verification;

/// <summary>
/// Production <see cref="IVerifierEngine"/> for the citizen-as-verifier
/// wallet flow (Feature 126, follow-up to Feature 125 PR-C). Wraps the
/// extracted <see cref="IVerifiablePresentationValidator"/> so the wallet
/// runs the same OID4VP-aligned pipeline the desk verifier
/// (<c>Sorcha.Verifier</c>) uses — issuer signature → holder→device
/// delegation → status list → nonce/audience binding.
/// </summary>
/// <remarks>
/// Offer payload (JSON, pasted into <c>VerifyFlow</c> or surfaced from a
/// future QR/NFC scanner):
/// <code>
/// {
///   "vpToken": "&lt;SD-JWT VC w/ KB-JWT&gt;",
///   "delegationCredential": "&lt;holder→device delegation JWT&gt;",
///   "requiredVct": "&lt;expected credential type URI&gt;",
///   "requiredClaims": ["givenName","familyName"],
///   "purpose": "Doorstep verification",
///   "holderDisplayName": "&lt;optional, used when validator can't extract it from the credential&gt;",
///   "issuerOrgName":  "&lt;optional&gt;"
/// }
/// </code>
/// Unrecognised JSON returns a Fail outcome with a plain-English recovery
/// message. The <c>StubVerifierEngine</c> remains available for tests /
/// pre-real-engine staging.
/// </remarks>
public sealed class RealVerifierEngine : IVerifierEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IVerifiablePresentationValidator _validator;
    private readonly ILogger<RealVerifierEngine> _logger;

    /// <summary>Initialise a new engine wired to the shared validator.</summary>
    public RealVerifierEngine(IVerifiablePresentationValidator validator, ILogger<RealVerifierEngine> logger)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<LibVerificationResult> VerifyAsync(
        VerifierEngineRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        OfferEnvelope? offer;
        try
        {
            offer = JsonSerializer.Deserialize<OfferEnvelope>(request.OfferPayload, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogInformation(ex, "Verifier offer was not valid JSON; returning Fail.");
            return Fail("Couldn't read the credential. Ask the person to try again, or scan a different code.");
        }

        if (offer is null || string.IsNullOrWhiteSpace(offer.VpToken))
        {
            return Fail("This doesn't look like a credential offer. Ask the person to show their credential again.");
        }

        var session = new VerifierSession
        {
            SessionId = $"wallet-verify/{Guid.NewGuid():N}",
            ClientId = request.VerifierClientId,
            Nonce = request.Nonce,
            RequiredVct = offer.RequiredVct ?? string.Empty,
            RequiredClaims = (IReadOnlyList<string>?)offer.RequiredClaims ?? Array.Empty<string>(),
            Purpose = offer.Purpose ?? "Doorstep verification",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };

        VerificationOutcome outcome;
        try
        {
            outcome = await _validator.ValidateAsync(
                session, offer.VpToken, offer.DelegationCredential, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Verifier pipeline threw unexpectedly; returning Fail.");
            return Fail("We couldn't complete the verification. Try again in a moment.");
        }

        return Map(outcome, offer, request);
    }

    private static LibVerificationResult Fail(string message) =>
        new(
            Outcome: LibVerifyOutcome.Fail,
            HolderDisplayName: "Unknown holder",
            IssuerOrgName: "Unknown issuer",
            CredentialType: "Unknown credential",
            DisclosedClaims: new Dictionary<string, object?>(),
            Messages: new[] { message },
            VerifiedAt: DateTimeOffset.UtcNow,
            TrustPanelJson: "{}");

    private static LibVerificationResult Map(
        VerificationOutcome outcome, OfferEnvelope offer, VerifierEngineRequest request)
    {
        var liveOutcome = outcome.Accepted
            ? LibVerifyOutcome.Pass
            : LibVerifyOutcome.Fail;

        var holderDisplay = offer.HolderDisplayName
            ?? (outcome.DisclosedClaims.TryGetValue("givenName", out var gn) ? gn?.ToString() : null)
            ?? "Verified holder";
        var issuer = offer.IssuerOrgName ?? "Unknown issuer";
        var credentialType = offer.RequiredVct ?? "Unknown credential";

        var messages = outcome.Errors;
        if (outcome.Accepted && messages.Count == 0)
            messages = Array.Empty<string>();

        var trustPanelJson = JsonSerializer.Serialize(new
        {
            outcome = liveOutcome.ToString(),
            holderDisplayName = holderDisplay,
            issuerOrgName = issuer,
            credentialType,
            claims = outcome.DisclosedClaims,
            verifiedAt = outcome.CompletedAt,
            sessionId = $"wallet-verify/{request.Nonce}"
        }, JsonOptions);

        return new LibVerificationResult(
            Outcome: liveOutcome,
            HolderDisplayName: holderDisplay,
            IssuerOrgName: issuer,
            CredentialType: credentialType,
            DisclosedClaims: outcome.DisclosedClaims,
            Messages: messages,
            VerifiedAt: outcome.CompletedAt,
            TrustPanelJson: trustPanelJson);
    }

    private sealed class OfferEnvelope
    {
        [JsonPropertyName("vpToken")] public string? VpToken { get; set; }
        [JsonPropertyName("delegationCredential")] public string? DelegationCredential { get; set; }
        [JsonPropertyName("requiredVct")] public string? RequiredVct { get; set; }
        [JsonPropertyName("requiredClaims")] public List<string>? RequiredClaims { get; set; }
        [JsonPropertyName("purpose")] public string? Purpose { get; set; }
        [JsonPropertyName("holderDisplayName")] public string? HolderDisplayName { get; set; }
        [JsonPropertyName("issuerOrgName")] public string? IssuerOrgName { get; set; }
    }
}
