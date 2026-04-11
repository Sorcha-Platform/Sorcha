// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Text.Json;
using Sorcha.Haip.Service.Models;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Creates and manages credential offers. Called by the Blueprint Service
/// when a credential issuance action targets an external HAIP wallet.
/// </summary>
public class CredentialOfferService
{
    private readonly PreAuthCodeStore _codeStore;
    private readonly ILogger<CredentialOfferService> _logger;
    private readonly string _issuerUrl;

    // In-memory offer store (production would use Redis or a database)
    private readonly ConcurrentDictionary<Guid, CredentialOffer> _offers = new();

    public CredentialOfferService(
        PreAuthCodeStore codeStore,
        ILogger<CredentialOfferService> logger,
        IConfiguration configuration)
    {
        _codeStore = codeStore;
        _logger = logger;
        _issuerUrl = configuration.GetValue<string>("Haip:IssuerUrl")
            ?? "https://sorcha.example/haip";
    }

    /// <summary>
    /// Creates a new credential offer with a pre-authorized code.
    /// Returns the offer and a URI for QR code rendering.
    /// </summary>
    public async Task<(CredentialOffer Offer, string OfferUri)> CreateOfferAsync(
        string issuerWalletAddress,
        string tenantId,
        string credentialType,
        Dictionary<string, object> claims,
        List<string>? disclosablePaths = null,
        CancellationToken ct = default)
    {
        var offer = new CredentialOffer
        {
            IssuerWalletAddress = issuerWalletAddress,
            TenantId = tenantId,
            CredentialType = credentialType,
            Claims = claims,
            DisclosablePaths = disclosablePaths ?? new(),
            PreAuthorizedCode = string.Empty, // Set below
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        };

        // Generate and store the pre-authorized code
        var code = await _codeStore.CreateAsync(offer.Id, ct);
        offer.PreAuthorizedCode = code;

        _offers[offer.Id] = offer;

        // Build the credential offer URI per OpenID4VCI
        // Build offer payload with correct IETF grant-type key (colons, not underscores)
        var grants = new Dictionary<string, object>
        {
            ["urn:ietf:params:oauth:grant-type:pre-authorized_code"] = new Dictionary<string, object>
            {
                ["pre-authorized_code"] = code
            }
        };
        var offerPayload = new Dictionary<string, object>
        {
            ["credential_issuer"] = _issuerUrl,
            ["credentials"] = new[] { credentialType },
            ["grants"] = grants
        };

        var offerJson = JsonSerializer.Serialize(offerPayload);
        var offerUri = $"openid-credential-offer://?credential_offer={Uri.EscapeDataString(offerJson)}";

        _logger.LogInformation(
            "Created credential offer {OfferId} for type {CredentialType} from issuer {IssuerWallet}",
            offer.Id, credentialType, issuerWalletAddress);

        return (offer, offerUri);
    }

    /// <summary>
    /// Gets an offer by ID.
    /// </summary>
    public CredentialOffer? GetOffer(Guid offerId)
    {
        _offers.TryGetValue(offerId, out var offer);
        return offer;
    }

    /// <summary>
    /// Marks an offer as exchanged.
    /// </summary>
    public Task MarkExchangedAsync(Guid offerId, CancellationToken ct = default)
    {
        if (_offers.TryGetValue(offerId, out var offer))
            offer.Status = OfferStatus.Exchanged;

        return Task.CompletedTask;
    }
}
