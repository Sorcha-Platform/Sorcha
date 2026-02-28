// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Tenant.Models;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Service for managing wallet address linking with cryptographic verification.
/// </summary>
public class WalletVerificationService : IWalletVerificationService
{
    private readonly IParticipantRepository _repository;
    private readonly IWalletServiceClient _walletClient;
    private readonly TenantDbContext _dbContext;
    private readonly ILogger<WalletVerificationService> _logger;

    // Challenge expiration time (5 minutes as per spec)
    private static readonly TimeSpan ChallengeExpiration = TimeSpan.FromMinutes(5);

    // Maximum number of active wallet links per participant
    private const int MaxActiveLinksPerParticipant = 10;

    public WalletVerificationService(
        IParticipantRepository repository,
        IWalletServiceClient walletClient,
        TenantDbContext dbContext,
        ILogger<WalletVerificationService> logger)
    {
        _repository = repository;
        _walletClient = walletClient;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WalletLinkChallengeResponse> InitiateLinkAsync(
        Guid participantId,
        InitiateWalletLinkRequest request,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        // Verify participant exists
        var participant = await _repository.GetByIdAsync(participantId, cancellationToken);
        if (participant == null)
        {
            throw new KeyNotFoundException($"Participant {participantId} not found");
        }

        // Check max links limit
        var activeCount = await GetActiveLinksCountAsync(participantId, cancellationToken);
        if (activeCount >= MaxActiveLinksPerParticipant)
        {
            throw new InvalidOperationException(
                $"Participant has reached the maximum of {MaxActiveLinksPerParticipant} active wallet links");
        }

        // Check if wallet is already linked to another participant (platform-wide uniqueness)
        if (await IsAddressLinkedAsync(request.WalletAddress, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Wallet address {request.WalletAddress} is already linked to a participant. " +
                "Wallet addresses must be unique platform-wide.");
        }

        // Check for existing pending challenge for this participant/wallet
        var existingChallenge = await _repository.GetPendingChallengeAsync(participantId, request.WalletAddress, cancellationToken);
        if (existingChallenge != null && existingChallenge.ExpiresAt > DateTimeOffset.UtcNow)
        {
            _logger.LogInformation(
                "Returning existing pending challenge {ChallengeId} for participant {ParticipantId}",
                existingChallenge.Id,
                participantId);
            return MapChallengeToResponse(existingChallenge, request.Algorithm);
        }

        // Generate challenge components
        var nonce = GenerateNonce();
        var timestamp = DateTimeOffset.UtcNow;
        var expiresAt = timestamp.Add(ChallengeExpiration);

        // Create challenge message
        var challengeMessage = GenerateChallengeMessage(participantId, request.WalletAddress, nonce, timestamp);

        var challenge = new WalletLinkChallenge
        {
            ParticipantId = participantId,
            WalletAddress = request.WalletAddress,
            Challenge = challengeMessage,
            Status = ChallengeStatus.Pending,
            ExpiresAt = expiresAt
        };

        await _repository.CreateChallengeAsync(challenge, cancellationToken);

        _logger.LogInformation(
            "Created wallet link challenge {ChallengeId} for participant {ParticipantId}, wallet {WalletAddress}",
            challenge.Id,
            participantId,
            request.WalletAddress);

        return MapChallengeToResponse(challenge, request.Algorithm);
    }

    /// <inheritdoc />
    public async Task<LinkedWalletAddressResponse> VerifyLinkAsync(
        Guid participantId,
        Guid challengeId,
        VerifyWalletLinkRequest request,
        string actorId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        // Retrieve the challenge
        var challenge = await _repository.GetChallengeByIdAsync(challengeId, cancellationToken);
        if (challenge == null)
        {
            throw new KeyNotFoundException($"Challenge {challengeId} not found");
        }

        // Validate participant matches
        if (challenge.ParticipantId != participantId)
        {
            throw new InvalidOperationException($"Challenge {challengeId} does not belong to participant {participantId}");
        }

        // Validate challenge status and expiration
        if (challenge.Status != ChallengeStatus.Pending)
        {
            throw new InvalidOperationException($"Challenge {challengeId} is not pending (status: {challenge.Status})");
        }

        if (challenge.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            challenge.Status = ChallengeStatus.Expired;
            await _repository.UpdateChallengeAsync(challenge, cancellationToken);
            throw new InvalidOperationException($"Challenge {challengeId} has expired");
        }

        // Re-check platform-wide uniqueness before verification
        if (await IsAddressLinkedAsync(challenge.WalletAddress, cancellationToken))
        {
            challenge.Status = ChallengeStatus.Failed;
            await _repository.UpdateChallengeAsync(challenge, cancellationToken);
            throw new InvalidOperationException(
                $"Wallet address {challenge.WalletAddress} was linked by another participant during verification");
        }

        // Verify signature using Wallet Service
        // First, get the wallet info to determine the algorithm and public key
        var walletInfo = await _walletClient.GetWalletAsync(challenge.WalletAddress, cancellationToken);
        if (walletInfo == null)
        {
            challenge.Status = ChallengeStatus.Failed;
            await _repository.UpdateChallengeAsync(challenge, cancellationToken);
            throw new InvalidOperationException($"Wallet {challenge.WalletAddress} not found in Wallet Service");
        }

        // Verify the signature
        var isValid = await _walletClient.VerifySignatureAsync(
            walletInfo.PublicKey,
            challenge.Challenge,
            request.Signature,
            walletInfo.Algorithm,
            cancellationToken);

        if (!isValid)
        {
            challenge.Status = ChallengeStatus.Failed;
            await _repository.UpdateChallengeAsync(challenge, cancellationToken);

            _logger.LogWarning(
                "Signature verification failed for challenge {ChallengeId}",
                challengeId);

            throw new InvalidOperationException("Signature verification failed");
        }

        // Signature valid - complete the challenge and create the link
        challenge.Status = ChallengeStatus.Completed;
        challenge.CompletedAt = DateTimeOffset.UtcNow;
        await _repository.UpdateChallengeAsync(challenge, cancellationToken);

        // Get participant to get org ID
        var participant = await _repository.GetByIdAsync(participantId, cancellationToken);

        var linkedWallet = new LinkedWalletAddress
        {
            ParticipantId = participantId,
            OrganizationId = participant?.OrganizationId ?? Guid.Empty,
            WalletAddress = challenge.WalletAddress,
            // Smart decode: legacy Base64 (+, /, =) or Base64url
            PublicKey = walletInfo.PublicKey.Contains('+') || walletInfo.PublicKey.Contains('/') || walletInfo.PublicKey.Contains('=')
                ? Convert.FromBase64String(walletInfo.PublicKey)
                : Base64Url.DecodeFromChars(walletInfo.PublicKey),
            Algorithm = walletInfo.Algorithm,
            Status = WalletLinkStatus.Active
        };

        await _repository.CreateWalletLinkAsync(linkedWallet, cancellationToken);

        // Add audit entry
        var auditEntry = new ParticipantAuditEntry
        {
            ParticipantId = participantId,
            Action = ParticipantAuditAction.WalletLinked,
            ActorId = actorId,
            ActorType = "User",
            NewValues = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                linkedWallet.WalletAddress,
                linkedWallet.Algorithm
            })),
            IpAddress = ipAddress
        };
        await _repository.CreateAuditEntryAsync(auditEntry, cancellationToken);

        _logger.LogInformation(
            "Successfully linked wallet {WalletAddress} to participant {ParticipantId}",
            challenge.WalletAddress,
            participantId);

        return MapLinkedWalletToResponse(linkedWallet);
    }

    /// <inheritdoc />
    public async Task<List<LinkedWalletAddressResponse>> ListLinksAsync(
        Guid participantId,
        bool includeRevoked = false,
        CancellationToken cancellationToken = default)
    {
        var links = await _repository.GetWalletLinksAsync(participantId, includeRevoked, cancellationToken);
        return links.Select(MapLinkedWalletToResponse).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> RevokeLinkAsync(
        Guid participantId,
        Guid linkId,
        string actorId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var link = await _repository.GetWalletLinkByIdAsync(linkId, cancellationToken);
        if (link == null || link.ParticipantId != participantId)
        {
            return false;
        }

        if (link.Status == WalletLinkStatus.Revoked)
        {
            return false;
        }

        link.Status = WalletLinkStatus.Revoked;
        link.RevokedAt = DateTimeOffset.UtcNow;
        await _repository.UpdateWalletLinkAsync(link, cancellationToken);

        // Add audit entry
        var auditEntry = new ParticipantAuditEntry
        {
            ParticipantId = participantId,
            Action = ParticipantAuditAction.WalletRevoked,
            ActorId = actorId,
            ActorType = "User",
            OldValues = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                link.WalletAddress,
                link.Algorithm
            })),
            IpAddress = ipAddress
        };
        await _repository.CreateAuditEntryAsync(auditEntry, cancellationToken);

        _logger.LogInformation(
            "Revoked wallet link {WalletAddress} from participant {ParticipantId}",
            link.WalletAddress,
            participantId);

        return true;
    }

    /// <inheritdoc />
    public async Task<WalletLinkChallengeResponse?> GetChallengeAsync(
        Guid challengeId,
        CancellationToken cancellationToken = default)
    {
        var challenge = await _repository.GetChallengeByIdAsync(challengeId, cancellationToken);
        if (challenge == null || challenge.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        return MapChallengeToResponse(challenge, "ED25519"); // Default algorithm
    }

    /// <inheritdoc />
    public async Task<int> ExpirePendingChallengesAsync(CancellationToken cancellationToken = default)
    {
        var count = await _repository.ExpirePendingChallengesAsync(cancellationToken);
        _logger.LogInformation("Expired {Count} pending wallet link challenges", count);
        return count;
    }

    /// <inheritdoc />
    public async Task<bool> IsAddressLinkedAsync(
        string walletAddress,
        CancellationToken cancellationToken = default)
    {
        var link = await _repository.GetActiveWalletLinkByAddressAsync(walletAddress, cancellationToken);
        return link != null;
    }

    /// <inheritdoc />
    public async Task<int> GetActiveLinksCountAsync(
        Guid participantId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.GetActiveWalletLinkCountAsync(participantId, cancellationToken);
    }


    /// <summary>
    /// Generates a cryptographically secure nonce.
    /// </summary>
    private static string GenerateNonce()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>
    /// Generates the challenge message to be signed.
    /// </summary>
    private static string GenerateChallengeMessage(
        Guid participantId,
        string walletAddress,
        string nonce,
        DateTimeOffset timestamp)
    {
        // Format: "Link wallet {address} to participant {id} at {timestamp}. Nonce: {nonce}"
        return $"Link wallet {walletAddress} to participant {participantId} at {timestamp:O}. Nonce: {nonce}";
    }

    /// <summary>
    /// Maps a WalletLinkChallenge entity to a response DTO.
    /// </summary>
    private static WalletLinkChallengeResponse MapChallengeToResponse(WalletLinkChallenge challenge, string algorithm)
    {
        return new WalletLinkChallengeResponse
        {
            ChallengeId = challenge.Id,
            Challenge = challenge.Challenge,
            WalletAddress = challenge.WalletAddress,
            Algorithm = algorithm,
            ExpiresAt = challenge.ExpiresAt,
            Status = challenge.Status
        };
    }

    /// <summary>
    /// Maps a LinkedWalletAddress entity to a response DTO.
    /// </summary>
    private static LinkedWalletAddressResponse MapLinkedWalletToResponse(LinkedWalletAddress link)
    {
        return new LinkedWalletAddressResponse
        {
            Id = link.Id,
            WalletAddress = link.WalletAddress,
            Algorithm = link.Algorithm,
            Status = link.Status,
            LinkedAt = link.LinkedAt,
            RevokedAt = link.RevokedAt
        };
    }

}
