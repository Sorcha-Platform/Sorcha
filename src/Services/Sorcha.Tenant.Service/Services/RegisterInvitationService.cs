// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Implements private register invitation lifecycle:
/// create (sign + encrypt via Wallet Service), accept (decrypt + verify + nonce check),
/// list, and revoke.
/// </summary>
public partial class RegisterInvitationService : IRegisterInvitationService
{
    private readonly TenantDbContext _dbContext;
    private readonly IWalletServiceClient _walletClient;
    private readonly IRegisterSubscriptionService _subscriptionService;
    private readonly ILogger<RegisterInvitationService> _logger;

    private const int MaxPendingPerOrg = 50;
    private const int MaxPerHour = 10;

    [GeneratedRegex(@"^[a-f0-9]{32}$")]
    private static partial Regex RegisterIdRegex();

    public RegisterInvitationService(
        TenantDbContext dbContext,
        IWalletServiceClient walletClient,
        IRegisterSubscriptionService subscriptionService,
        ILogger<RegisterInvitationService> logger)
    {
        _dbContext = dbContext;
        _walletClient = walletClient;
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<InvitationCreatedResponse> CreateAsync(
        Guid sourceOrgId,
        CreateRegisterInvitationRequest request,
        Guid createdByUserId,
        CancellationToken ct = default)
    {
        // Validate register ID format
        if (!RegisterIdRegex().IsMatch(request.RegisterId))
            throw new ArgumentException("Register ID must be a 32-character lowercase hex string.");

        // Validate expiry range
        if (request.ExpiresInDays is < 1 or > 90)
            throw new ArgumentException("ExpiresInDays must be between 1 and 90.");

        // Validate target org DID
        if (!SorchaDidIdentifier.TryParse(request.TargetOrgDid, out var targetDid)
            || targetDid.Type != SorchaDidType.Organization)
            throw new ArgumentException("Target must be a valid organization DID (did:sorcha:org:{address}).");

        // Verify source org exists and has a wallet
        var sourceOrg = await _dbContext.Organizations.FindAsync([sourceOrgId], ct)
            ?? throw new InvalidOperationException("Source organization not found.");

        if (string.IsNullOrEmpty(sourceOrg.WalletAddress))
            throw new InvalidOperationException("Source organization has no wallet. Wallet provisioning may still be pending.");

        // Verify source org owns the register
        var ownership = await _dbContext.OrganizationRegisterSubscriptions
            .AnyAsync(s => s.OrganizationId == sourceOrgId
                && s.RegisterId == request.RegisterId
                && s.SubscriptionType == SubscriptionType.Owner
                && s.Status == SubscriptionStatus.Active, ct);

        if (!ownership)
            throw new InvalidOperationException("Organization does not own the specified register.");

        // Rate limiting: check pending count
        var pendingCount = await _dbContext.Set<RegisterInvitationRecord>()
            .CountAsync(r => r.SourceOrgId == sourceOrgId
                && r.Status == RegisterInvitationStatus.Pending, ct);

        if (pendingCount >= MaxPendingPerOrg)
            throw new InvalidOperationException($"Rate limit exceeded. Maximum pending invitations ({MaxPendingPerOrg}) reached.");

        // Rate limiting: check hourly count
        var oneHourAgo = DateTimeOffset.UtcNow.AddHours(-1);
        var hourlyCount = await _dbContext.Set<RegisterInvitationRecord>()
            .CountAsync(r => r.SourceOrgId == sourceOrgId
                && r.CreatedAt >= oneHourAgo, ct);

        if (hourlyCount >= MaxPerHour)
            throw new InvalidOperationException($"Rate limit exceeded. Maximum {MaxPerHour} invitations per hour.");

        // Check for duplicate pending invitation to the same register+target
        var duplicatePending = await _dbContext.Set<RegisterInvitationRecord>()
            .AnyAsync(r => r.SourceOrgId == sourceOrgId
                && r.RegisterId == request.RegisterId
                && r.TargetOrgDid == request.TargetOrgDid
                && r.Status == RegisterInvitationStatus.Pending, ct);

        if (duplicatePending)
            throw new InvalidOperationException("A pending invitation already exists for this register and target organization.");

        // Resolve target org wallet address from DID
        var targetWalletAddress = targetDid.Locator;

        // Verify target org wallet exists
        WalletInfo? targetWallet;
        try
        {
            targetWallet = await _walletClient.GetWalletAsync(targetWalletAddress, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve target org wallet {Address}", targetWalletAddress);
            throw new InvalidOperationException("Could not resolve target organization's wallet.");
        }

        if (targetWallet is null)
            throw new InvalidOperationException("Target organization wallet not found. The DID may be invalid.");

        // Build invitation payload
        var invitationId = Guid.NewGuid().ToString();
        var nonce = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddDays(request.ExpiresInDays);

        // Look up register name from subscription
        var subscription = await _dbContext.OrganizationRegisterSubscriptions
            .FirstOrDefaultAsync(s => s.OrganizationId == sourceOrgId
                && s.RegisterId == request.RegisterId, ct);

        var payload = new InvitationPayload
        {
            RegisterId = request.RegisterId,
            SourceOrgDid = SorchaDidIdentifier.FromOrganization(sourceOrg.WalletAddress).ToString(),
            TargetOrgDid = request.TargetOrgDid,
            InvitationId = invitationId,
            ExpiresAt = expiresAt,
            Nonce = nonce,
            RegisterName = subscription?.RegisterName,
            SourceOrgName = sourceOrg.Name
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

        // Encrypt payload to target org's wallet (Wallet Service handles X25519 ECDH)
        var encryptedPayload = await _walletClient.EncryptPayloadAsync(
            targetWalletAddress, payloadBytes, ct);

        // Sign the encrypted payload with source org's wallet (hex-encoded for SignDataAsync)
        var encryptedHex = Convert.ToHexString(encryptedPayload).ToLowerInvariant();
        var signResult = await _walletClient.SignDataAsync(
            sourceOrg.WalletAddress, encryptedHex, ct);

        // Build token envelope
        var envelope = new InvitationTokenEnvelope
        {
            Version = 1,
            Signature = Convert.ToBase64String(signResult.Signature),
            EncryptedPayload = Convert.ToBase64String(encryptedPayload),
            Sender = payload.SourceOrgDid
        };

        var envelopeJson = JsonSerializer.Serialize(envelope);
        var token = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(envelopeJson));

        // Record the invitation
        var record = new RegisterInvitationRecord
        {
            InvitationId = invitationId,
            SourceOrgId = sourceOrgId,
            TargetOrgDid = request.TargetOrgDid,
            TargetWalletAddress = targetWalletAddress,
            RegisterId = request.RegisterId,
            RegisterName = subscription?.RegisterName,
            Nonce = nonce,
            Status = RegisterInvitationStatus.Pending,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            CreatedByUserId = createdByUserId
        };

        _dbContext.Set<RegisterInvitationRecord>().Add(record);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Invitation {InvitationId} created by org {SourceOrgId} for {TargetOrgDid} on register {RegisterId}",
            invitationId, sourceOrgId, request.TargetOrgDid, request.RegisterId);

        return new InvitationCreatedResponse
        {
            InvitationId = invitationId,
            InvitationToken = token,
            RegisterId = request.RegisterId,
            TargetOrgDid = request.TargetOrgDid,
            ExpiresAt = expiresAt,
            CreatedAt = now
        };
    }

    /// <inheritdoc />
    public async Task<InvitationAcceptedResponse> AcceptAsync(
        Guid targetOrgId,
        AcceptInvitationRequest request,
        Guid acceptedByUserId,
        CancellationToken ct = default)
    {
        // Decode token envelope
        InvitationTokenEnvelope envelope;
        try
        {
            var envelopeJson = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(request.InvitationToken));
            envelope = JsonSerializer.Deserialize<InvitationTokenEnvelope>(envelopeJson)
                ?? throw new FormatException("Empty envelope.");
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            throw new ArgumentException("Invalid invitation token format.", ex);
        }

        if (envelope.Version != 1)
            throw new ArgumentException($"Unsupported token version: {envelope.Version}");

        // Verify source org DID format
        if (!SorchaDidIdentifier.TryParse(envelope.Sender, out var senderDid)
            || senderDid.Type != SorchaDidType.Organization)
            throw new ArgumentException("Invalid sender DID in token.");

        // Verify signature using source org's public key (via Wallet Service)
        var encryptedPayload = Convert.FromBase64String(envelope.EncryptedPayload);

        // Get sender's wallet to retrieve public key for verification
        var senderWallet = await _walletClient.GetWalletAsync(senderDid.Locator, ct)
            ?? throw new InvalidOperationException("Source organization wallet not found. Invitation may be from an unknown org.");

        // VerifySignatureAsync takes base64-encoded strings; data was signed as hex
        var encryptedHex = Convert.ToHexString(encryptedPayload).ToLowerInvariant();
        var signatureValid = await _walletClient.VerifySignatureAsync(
            senderWallet.PublicKey, encryptedHex, envelope.Signature, senderWallet.Algorithm, ct);

        if (!signatureValid)
            throw new InvalidOperationException("Invitation signature verification failed. Token may have been tampered with.");

        // Decrypt payload using target org's wallet
        var targetOrg = await _dbContext.Organizations.FindAsync([targetOrgId], ct)
            ?? throw new InvalidOperationException("Target organization not found.");

        if (string.IsNullOrEmpty(targetOrg.WalletAddress))
            throw new InvalidOperationException("Target organization has no wallet.");

        byte[] decryptedBytes;
        try
        {
            decryptedBytes = await _walletClient.DecryptPayloadAsync(
                targetOrg.WalletAddress, encryptedPayload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt invitation for org {OrgId}", targetOrgId);
            throw new InvalidOperationException("Failed to decrypt invitation. Token may not be intended for this organization.");
        }

        var payload = JsonSerializer.Deserialize<InvitationPayload>(
            Encoding.UTF8.GetString(decryptedBytes))
            ?? throw new InvalidOperationException("Decrypted payload is empty.");

        // Validate payload fields
        if (payload.TargetOrgDid != SorchaDidIdentifier.FromOrganization(targetOrg.WalletAddress).ToString())
            throw new InvalidOperationException("Invitation is not addressed to this organization.");

        // Check expiry
        if (payload.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Invitation has expired.");

        // Check nonce for replay protection (unique index will catch races)
        var nonceExists = await _dbContext.Set<InvitationNonce>()
            .AnyAsync(n => n.Nonce == payload.Nonce, ct);

        if (nonceExists)
            throw new InvalidOperationException("Invitation has already been accepted (nonce reuse detected).");

        // Check if already subscribed
        var existingSub = await _dbContext.OrganizationRegisterSubscriptions
            .AnyAsync(s => s.OrganizationId == targetOrgId
                && s.RegisterId == payload.RegisterId
                && s.Status == SubscriptionStatus.Active, ct);

        if (existingSub)
            throw new InvalidOperationException("Organization is already subscribed to this register.");

        // Consume the nonce
        var nonceRecord = new InvitationNonce
        {
            Nonce = payload.Nonce,
            InvitationId = payload.InvitationId,
            SourceOrgId = await ResolveOrgIdFromWalletAddress(senderDid.Locator, ct),
            TargetOrgId = targetOrgId,
            RegisterId = payload.RegisterId,
            ConsumedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Set<InvitationNonce>().Add(nonceRecord);

        // Create the Invited subscription
        var subscription = new OrganizationRegisterSubscription
        {
            OrganizationId = targetOrgId,
            RegisterId = payload.RegisterId,
            RegisterName = payload.RegisterName,
            SubscriptionType = SubscriptionType.Invited,
            Status = SubscriptionStatus.Active,
            InvitationId = payload.InvitationId,
            SubscribedAt = DateTimeOffset.UtcNow,
            SubscribedByUserId = acceptedByUserId
        };

        _dbContext.OrganizationRegisterSubscriptions.Add(subscription);

        // Update invitation record status if it exists locally
        var invitationRecord = await _dbContext.Set<RegisterInvitationRecord>()
            .FirstOrDefaultAsync(r => r.InvitationId == payload.InvitationId, ct);
        if (invitationRecord != null)
        {
            invitationRecord.Status = RegisterInvitationStatus.Accepted;
            invitationRecord.AcceptedAt = DateTimeOffset.UtcNow;
        }

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_InvNonce_Nonce") == true)
        {
            throw new InvalidOperationException("Invitation has already been accepted (nonce reuse detected).");
        }

        _logger.LogInformation(
            "Invitation {InvitationId} accepted by org {TargetOrgId} for register {RegisterId}",
            payload.InvitationId, targetOrgId, payload.RegisterId);

        return new InvitationAcceptedResponse
        {
            SubscriptionId = subscription.Id,
            RegisterId = payload.RegisterId,
            RegisterName = payload.RegisterName,
            SourceOrgDid = payload.SourceOrgDid,
            SourceOrgName = payload.SourceOrgName,
            SubscriptionStatus = "Active",
            AcceptedAt = DateTimeOffset.UtcNow
        };
    }

    /// <inheritdoc />
    public async Task<InvitationListResponse> ListAsync(
        Guid orgId, string direction = "all", CancellationToken ct = default)
    {
        var org = await _dbContext.Organizations.FindAsync([orgId], ct)
            ?? throw new InvalidOperationException("Organization not found.");

        var query = _dbContext.Set<RegisterInvitationRecord>().AsQueryable();

        var orgDid = !string.IsNullOrEmpty(org.WalletAddress)
            ? SorchaDidIdentifier.FromOrganization(org.WalletAddress).ToString()
            : null;

        query = direction.ToLowerInvariant() switch
        {
            "sent" => query.Where(r => r.SourceOrgId == orgId),
            "received" => orgDid != null
                ? query.Where(r => r.TargetOrgDid == orgDid)
                : query.Where(r => false), // Org without wallet can't receive invitations
            _ => orgDid != null
                ? query.Where(r => r.SourceOrgId == orgId || r.TargetOrgDid == orgDid)
                : query.Where(r => r.SourceOrgId == orgId)
        };

        var records = await query.OrderByDescending(r => r.CreatedAt).ToListAsync(ct);

        var summaries = records.Select(r => new InvitationSummary
        {
            InvitationId = r.InvitationId,
            RegisterId = r.RegisterId,
            RegisterName = r.RegisterName,
            SourceOrgDid = GetSourceOrgDid(r),
            SourceOrgName = null, // Could be resolved from SourceOrgId if needed
            TargetOrgDid = r.TargetOrgDid,
            TargetOrgName = null,
            Direction = r.SourceOrgId == orgId ? "sent" : "received",
            Status = r.Status.ToString(),
            ExpiresAt = r.ExpiresAt,
            CreatedAt = r.CreatedAt
        }).ToList();

        return new InvitationListResponse
        {
            Invitations = summaries,
            TotalCount = summaries.Count
        };
    }

    /// <inheritdoc />
    public async Task RevokeAsync(Guid orgId, string invitationId, CancellationToken ct = default)
    {
        var record = await _dbContext.Set<RegisterInvitationRecord>()
            .FirstOrDefaultAsync(r => r.InvitationId == invitationId && r.SourceOrgId == orgId, ct)
            ?? throw new InvalidOperationException("Invitation not found.");

        if (record.Status != RegisterInvitationStatus.Pending)
            throw new InvalidOperationException($"Cannot revoke invitation with status '{record.Status}'.");

        record.Status = RegisterInvitationStatus.Revoked;
        record.RevokedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Invitation {InvitationId} revoked by org {OrgId}", invitationId, orgId);
    }

    private async Task<Guid> ResolveOrgIdFromWalletAddress(string walletAddress, CancellationToken ct)
    {
        var org = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.WalletAddress == walletAddress, ct);
        return org?.Id ?? Guid.Empty;
    }

    private string GetSourceOrgDid(RegisterInvitationRecord record)
    {
        // Try to resolve from the source org's wallet address
        var sourceOrg = _dbContext.Organizations
            .FirstOrDefault(o => o.Id == record.SourceOrgId);
        return sourceOrg?.WalletAddress != null
            ? SorchaDidIdentifier.FromOrganization(sourceOrg.WalletAddress).ToString()
            : $"org:{record.SourceOrgId}";
    }
}
