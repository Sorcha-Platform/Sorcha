// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;

using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Service implementation for public user (non-organizational) account management.
/// Creates and looks up <see cref="PublicIdentity"/> users who authenticate via passkeys
/// or social login providers without belonging to an organization.
/// </summary>
public class PublicUserService : IPublicUserService
{
    private readonly IIdentityRepository _identityRepository;
    private readonly ILogger<PublicUserService> _logger;

    /// <summary>
    /// Creates a new <see cref="PublicUserService"/> instance.
    /// </summary>
    /// <param name="identityRepository">Repository for identity persistence operations.</param>
    /// <param name="logger">Logger instance.</param>
    public PublicUserService(
        IIdentityRepository identityRepository,
        ILogger<PublicUserService> logger)
    {
        ArgumentNullException.ThrowIfNull(identityRepository);
        ArgumentNullException.ThrowIfNull(logger);

        _identityRepository = identityRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PublicUserResult> CreatePublicUserAsync(
        string displayName,
        string? email,
        PasskeyCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        // Check for duplicate email
        if (!string.IsNullOrWhiteSpace(email))
        {
            var existing = await _identityRepository.GetPublicIdentityByEmailAsync(email, cancellationToken);
            if (existing is not null)
            {
                _logger.LogWarning("Public user signup rejected: email {Email} already in use", email);
                return new PublicUserResult(null, false, "A user with this email address already exists.");
            }
        }

        var identity = new PublicIdentity
        {
            DisplayName = displayName,
            Email = email,
            Status = "Active",
            RegisteredAt = DateTimeOffset.UtcNow
        };

        // Link the passkey credential to this identity
        credential.OwnerType = "PublicIdentity";
        credential.OwnerId = identity.Id;
        identity.PasskeyCredentials.Add(credential);

        var created = await _identityRepository.CreatePublicIdentityAsync(identity, cancellationToken);

        _logger.LogInformation("Public user created: {UserId} with display name {DisplayName}",
            created.Id, created.DisplayName);

        return new PublicUserResult(created, true);
    }

    /// <inheritdoc />
    public async Task<PublicUserResult> CreatePublicUserFromSocialAsync(
        string displayName,
        string? email,
        SocialLoginLink socialLoginLink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(socialLoginLink);

        // Check for existing user with same email — link the social account if found
        if (!string.IsNullOrWhiteSpace(email))
        {
            var existing = await _identityRepository.GetPublicIdentityByEmailAsync(email, cancellationToken);
            if (existing is not null)
            {
                // Check if this exact social link already exists (prevent duplicates)
                var alreadyLinked = existing.SocialLoginLinks.Any(
                    s => s.ProviderType == socialLoginLink.ProviderType
                      && s.ExternalSubjectId == socialLoginLink.ExternalSubjectId);

                if (alreadyLinked)
                {
                    // Returning user — just update last used
                    existing.LastUsedAt = DateTimeOffset.UtcNow;
                    await _identityRepository.UpdatePublicIdentityAsync(existing, cancellationToken);
                    return new PublicUserResult(existing, IsNewUser: false);
                }

                // Link the social account to the existing identity
                socialLoginLink.PublicIdentityId = existing.Id;
                existing.SocialLoginLinks.Add(socialLoginLink);
                existing.LastUsedAt = DateTimeOffset.UtcNow;
                await _identityRepository.UpdatePublicIdentityAsync(existing, cancellationToken);

                _logger.LogInformation(
                    "Social login linked to existing public user: {UserId} with provider {Provider}",
                    existing.Id, socialLoginLink.ProviderType);

                return new PublicUserResult(existing, IsNewUser: false);
            }
        }

        var identity = new PublicIdentity
        {
            DisplayName = displayName,
            Email = email,
            Status = "Active",
            EmailVerified = true, // Social provider verified the email
            EmailVerifiedAt = DateTimeOffset.UtcNow,
            RegisteredAt = DateTimeOffset.UtcNow
        };

        // Link the social login to this identity
        socialLoginLink.PublicIdentityId = identity.Id;
        identity.SocialLoginLinks.Add(socialLoginLink);

        var created = await _identityRepository.CreatePublicIdentityAsync(identity, cancellationToken);

        _logger.LogInformation("Public user created from social login: {UserId} with provider {Provider}",
            created.Id, socialLoginLink.ProviderType);

        return new PublicUserResult(created, true);
    }

    /// <inheritdoc />
    public async Task<PublicIdentity?> GetPublicUserByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _identityRepository.GetPublicIdentityByIdAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PublicIdentity?> GetPublicUserByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        return await _identityRepository.GetPublicIdentityByEmailAsync(email, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PublicIdentity?> GetPublicUserByCredentialIdAsync(
        byte[] credentialId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentialId);
        return await _identityRepository.GetPublicIdentityByCredentialIdAsync(credentialId, cancellationToken);
    }
}
