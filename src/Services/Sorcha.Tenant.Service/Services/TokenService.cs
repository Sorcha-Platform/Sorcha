// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceDefaults.Auth;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Extensions;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Service implementation for JWT token operations.
/// </summary>
public class TokenService : ITokenService
{
    private readonly JwtConfiguration _config;
    private readonly ITokenRevocationService _revocationService;
    private readonly IIdentityRepository _identityRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IParticipantRepository _participantRepository;
    private readonly ILogger<TokenService> _logger;
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly SigningCredentials? _signingCredentials;
    private readonly TokenValidationParameters _validationParameters;
    private readonly SorchaAudiences _audiences;
    private readonly IdentityMetrics? _metrics;

    public TokenService(
        IOptions<JwtConfiguration> options,
        ITokenRevocationService revocationService,
        IIdentityRepository identityRepository,
        IOrganizationRepository organizationRepository,
        IParticipantRepository participantRepository,
        ILogger<TokenService> logger,
        IdentityMetrics? metrics = null)
    {
        _config = options?.Value ?? new JwtConfiguration();
        _audiences = new SorchaAudiences(_config.InstallationName);
        _metrics = metrics;
        _revocationService = revocationService ?? throw new ArgumentNullException(nameof(revocationService));
        _identityRepository = identityRepository ?? throw new ArgumentNullException(nameof(identityRepository));
        _organizationRepository = organizationRepository ?? throw new ArgumentNullException(nameof(organizationRepository));
        _participantRepository = participantRepository ?? throw new ArgumentNullException(nameof(participantRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tokenHandler = new JwtSecurityTokenHandler();
        _tokenHandler.MapInboundClaims = false;

        // Set up signing credentials if key is configured
        if (!string.IsNullOrEmpty(_config.SigningKey))
        {
            var keyBytes = Encoding.UTF8.GetBytes(_config.SigningKey);
            if (keyBytes.Length < 32)
            {
                Array.Resize(ref keyBytes, 32);
            }
            var securityKey = new SymmetricSecurityKey(keyBytes);
            _signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
        }

        // Set up validation parameters
        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = _config.ValidateIssuer,
            ValidIssuer = _config.Issuer,
            ValidateAudience = _config.ValidateAudience,
            ValidAudiences = _config.Audiences,
            ValidateIssuerSigningKey = _config.ValidateIssuerSigningKey,
            IssuerSigningKey = _signingCredentials?.Key,
            ValidateLifetime = _config.ValidateLifetime,
            ClockSkew = TimeSpan.FromMinutes(_config.ClockSkewMinutes)
        };
    }

    /// <inheritdoc />
    public async Task<TokenResponse> GenerateUserTokenAsync(
        UserIdentity user,
        Organization organization,
        Guid platformUserId,
        Tier tier = Tier.Platform,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(organization);

        if (tier is not (Tier.Consumer or Tier.Platform))
        {
            throw new ArgumentOutOfRangeException(nameof(tier), tier,
                "Human access tokens are only minted at the Consumer or Platform tier (spec 136).");
        }

        var accessTokenJti = Guid.NewGuid().ToString();
        var refreshTokenJti = Guid.NewGuid().ToString();

        // Common claims for every human token.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, accessTokenJti),
            new("name", user.DisplayName),
            new(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser),
            new("platform_user_id", platformUserId.ToString())
        };

        // Platform-tier tokens carry organisation context, roles, and wallet binding (FR-014).
        // Consumer-tier tokens deliberately OMIT org_id/org_name/roles/wallet_address so they are
        // inert against platform surfaces (FR-013) — the holder identifier (platform_user_id) is enough.
        if (tier == Tier.Platform)
        {
            claims.Add(new Claim(TokenClaimConstants.OrgId, organization.Id.ToString()));
            claims.Add(new Claim("org_name", organization.Name));

            foreach (var role in user.Roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
            }

            await AddWalletAddressClaimAsync(claims, user.Id, organization.Id, cancellationToken);
        }

        var accessTokenExpiry = DateTimeOffset.UtcNow.AddMinutes(_config.AccessTokenLifetimeMinutes);
        var refreshTokenExpiry = DateTimeOffset.UtcNow.AddHours(_config.RefreshTokenLifetimeHours);

        var accessToken = GenerateToken(claims, accessTokenExpiry, _audiences.For(tier));
        _metrics?.TokenMinted(tier);
        // Consumer refresh re-mints consumer; platform refresh keeps org context. The refresh token
        // carries the tier so RefreshTokenAsync re-mints the same tier (FR-012).
        var refreshToken = GenerateRefreshToken(
            refreshTokenJti, user.Id.ToString(),
            tier == Tier.Platform ? organization.Id.ToString() : null,
            refreshTokenExpiry, platformUserId.ToString(), tier);

        // Track tokens for potential bulk revocation
        await _revocationService.TrackTokenAsync(
            accessTokenJti, user.Id.ToString(), organization.Id.ToString(), accessTokenExpiry, cancellationToken);
        await _revocationService.TrackTokenAsync(
            refreshTokenJti, user.Id.ToString(), organization.Id.ToString(), refreshTokenExpiry, cancellationToken);

        _logger.LogInformation(
            "Generated tokens for user {UserId} in organization {OrgId}",
            user.Id, organization.Id);

        return new TokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = _config.AccessTokenLifetimeMinutes * 60
        };
    }

    /// <inheritdoc />
    public Task<TokenResponse> GenerateServiceTokenAsync(
        ServicePrincipal servicePrincipal,
        Guid? delegatedUserId = null,
        Guid? delegatedOrgId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(servicePrincipal);

        var accessTokenJti = Guid.NewGuid().ToString();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, servicePrincipal.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, accessTokenJti),
            new("client_id", servicePrincipal.ClientId),
            new(TokenClaimConstants.ServiceName, servicePrincipal.ServiceName),
            new(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService)
        };

        // Add allowed scopes
        foreach (var scope in servicePrincipal.Scopes)
        {
            claims.Add(new Claim(TokenClaimConstants.Scope, scope));
        }

        // Add delegation claims if present
        if (delegatedUserId.HasValue)
        {
            claims.Add(new Claim(TokenClaimConstants.DelegatedUserId, delegatedUserId.Value.ToString()));
        }
        if (delegatedOrgId.HasValue)
        {
            claims.Add(new Claim(TokenClaimConstants.DelegatedOrgId, delegatedOrgId.Value.ToString()));
        }

        var accessTokenExpiry = DateTimeOffset.UtcNow.AddHours(_config.ServiceTokenLifetimeHours);

        var accessToken = GenerateToken(claims, accessTokenExpiry, _audiences.For(Tier.Service));
        _metrics?.TokenMinted(Tier.Service);

        _logger.LogInformation(
            "Generated service token for {ServiceName} (client: {ClientId})",
            servicePrincipal.ServiceName, servicePrincipal.ClientId);

        // Service tokens don't have refresh tokens
        return Task.FromResult(new TokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = string.Empty,
            ExpiresIn = _config.ServiceTokenLifetimeHours * 3600
        });
    }

    /// <inheritdoc />
    public async Task<TokenResponse?> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        try
        {
            var principal = _tokenHandler.ValidateToken(refreshToken, _validationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwt)
            {
                return null;
            }

            // Check if token is revoked
            var jti = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;
            if (!string.IsNullOrEmpty(jti) && await _revocationService.IsTokenRevokedAsync(jti, cancellationToken))
            {
                _logger.LogWarning("Attempted to use revoked refresh token {Jti}", jti);
                return null;
            }

            // Verify it's a refresh token
            var tokenType = jwt.Claims.FirstOrDefault(c => c.Type == "token_use")?.Value;
            if (tokenType != "refresh")
            {
                return null;
            }

            // Extract user and org IDs from refresh token
            var userId = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            var orgId = principal.FindFirst(TokenClaimConstants.OrgId)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return null;
            }

            // Look up the current user and organization to rebuild full claims
            // (refresh tokens only contain sub + org_id, not roles/name/email)
            if (!Guid.TryParse(userId, out var userGuid))
            {
                _logger.LogWarning("Invalid user ID in refresh token: {UserId}", userId);
                return null;
            }

            var user = await _identityRepository.GetUserByIdAsync(userGuid, cancellationToken);
            if (user is null || user.Status != IdentityStatus.Active)
            {
                _logger.LogWarning("User {UserId} not found or inactive during token refresh", userId);
                return null;
            }

            Organization? organization = null;
            if (!string.IsNullOrEmpty(orgId) && Guid.TryParse(orgId, out var orgGuid))
            {
                organization = await _organizationRepository.GetByIdAsync(orgGuid, cancellationToken);
            }

            // A refresh re-mints an access token of the SAME tier as the refresh token (spec 136,
            // FR-012). The tier was stamped at issuance; absent (legacy tokens) defaults to platform.
            var tier = ParseTierClaim(principal.FindFirst("tier")?.Value);

            // Generate new access token with claims rebuilt from current user state
            var newAccessTokenJti = Guid.NewGuid().ToString();
            var accessTokenExpiry = DateTimeOffset.UtcNow.AddMinutes(_config.AccessTokenLifetimeMinutes);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email),
                new(JwtRegisteredClaimNames.Jti, newAccessTokenJti),
                new("name", user.DisplayName),
                new(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser)
            };

            // platform_user_id is carried on every human tier. For refresh tokens minted before this
            // claim was persisted, fall back to the UserIdentity row (canonical source).
            var platformUserId = principal.FindFirst("platform_user_id")?.Value;
            if (string.IsNullOrEmpty(platformUserId) && user.PlatformUserId != Guid.Empty)
            {
                platformUserId = user.PlatformUserId.ToString();
                _logger.LogDebug(
                    "Refresh token for user {UserId} lacked platform_user_id claim; recovered from UserIdentity.PlatformUserId",
                    user.Id);
            }
            if (!string.IsNullOrEmpty(platformUserId))
            {
                claims.Add(new Claim("platform_user_id", platformUserId));
            }

            // Platform-tier refresh re-mints org context + roles + wallet binding; consumer-tier
            // refresh stays inert (no org_id/roles/wallet) — FR-013/FR-014.
            if (tier == Tier.Platform)
            {
                if (organization is not null)
                {
                    claims.Add(new Claim(TokenClaimConstants.OrgId, organization.Id.ToString()));
                    claims.Add(new Claim("org_name", organization.Name));
                }
                else if (!string.IsNullOrEmpty(orgId))
                {
                    claims.Add(new Claim(TokenClaimConstants.OrgId, orgId));
                }

                foreach (var role in user.Roles)
                {
                    claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
                }

                var orgIdForWallet = organization?.Id ?? (Guid.TryParse(orgId, out var parsedOrgId) ? parsedOrgId : (Guid?)null);
                if (orgIdForWallet.HasValue)
                {
                    await AddWalletAddressClaimAsync(claims, userGuid, orgIdForWallet.Value, cancellationToken);
                }
            }

            var accessToken = GenerateToken(claims, accessTokenExpiry, _audiences.For(tier));
            _metrics?.TokenMinted(tier);

            // Track new access token
            await _revocationService.TrackTokenAsync(
                newAccessTokenJti, userId, orgId, accessTokenExpiry, cancellationToken);

            _logger.LogInformation("Refreshed access token for user {UserId}", userId);

            return new TokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken, // Return same refresh token
                ExpiresIn = _config.AccessTokenLifetimeMinutes * 60
            };
        }
        catch (SecurityTokenMalformedException ex)
        {
            // Token format is invalid (wrong number of segments, etc.)
            _logger.LogWarning(ex, "Malformed refresh token");
            return null;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Invalid refresh token");
            return null;
        }
        catch (ArgumentException ex)
        {
            // Handles other argument-related parsing errors
            _logger.LogWarning(ex, "Argument error parsing refresh token");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RevokeTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var jwt = _tokenHandler.ReadJwtToken(token);
            var jti = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;
            var exp = jwt.ValidTo;

            if (string.IsNullOrEmpty(jti))
            {
                return false;
            }

            await _revocationService.RevokeTokenAsync(jti, new DateTimeOffset(exp), cancellationToken);

            _logger.LogInformation("Revoked token {Jti}", jti);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to revoke token");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<TokenIntrospectionResponse> IntrospectTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new TokenIntrospectionResponse { Active = false };
        }

        try
        {
            var principal = _tokenHandler.ValidateToken(token, _validationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwt)
            {
                return new TokenIntrospectionResponse { Active = false };
            }

            // Check if token is revoked
            var jti = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;
            if (!string.IsNullOrEmpty(jti) && await _revocationService.IsTokenRevokedAsync(jti, cancellationToken))
            {
                return new TokenIntrospectionResponse { Active = false };
            }

            var roles = principal.FindAll("role").Select(c => c.Value).ToArray();

            return new TokenIntrospectionResponse
            {
                Active = true,
                Sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
                ClientId = principal.FindFirst("client_id")?.Value,
                Scope = string.Join(" ", principal.FindAll(TokenClaimConstants.Scope).Select(c => c.Value)),
                Exp = jwt.ValidTo.ToUnixTimeSeconds(),
                Iat = jwt.IssuedAt.ToUnixTimeSeconds(),
                Iss = jwt.Issuer,
                Aud = jwt.Audiences.FirstOrDefault(),
                TokenType = "Bearer",
                Jti = jti,
                OrgId = principal.FindFirst(TokenClaimConstants.OrgId)?.Value,
                Roles = roles.Length > 0 ? roles : null
            };
        }
        catch (SecurityTokenMalformedException)
        {
            // Token format is invalid (wrong number of segments, etc.)
            return new TokenIntrospectionResponse { Active = false };
        }
        catch (SecurityTokenException)
        {
            // Token validation failed (signature, lifetime, issuer, etc.)
            return new TokenIntrospectionResponse { Active = false };
        }
        catch (ArgumentException)
        {
            // Handles other argument-related parsing errors
            return new TokenIntrospectionResponse { Active = false };
        }
    }

    /// <inheritdoc />
    public async Task RevokeAllUserTokensAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await _revocationService.RevokeAllUserTokensAsync(userId.ToString(), cancellationToken);
        _logger.LogInformation("Revoked all tokens for user {UserId}", userId);
    }

    /// <inheritdoc />
    public async Task RevokeAllOrganizationTokensAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        await _revocationService.RevokeAllOrganizationTokensAsync(organizationId.ToString(), cancellationToken);
        _logger.LogInformation("Revoked all tokens for organization {OrgId}", organizationId);
    }

    private string GenerateToken(IEnumerable<Claim> claims, DateTimeOffset expiry, string audience)
    {
        if (_signingCredentials == null)
        {
            throw new InvalidOperationException("JWT signing key is not configured");
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiry.UtcDateTime,
            IssuedAt = DateTime.UtcNow,
            Issuer = _config.Issuer,
            Audience = audience,
            SigningCredentials = _signingCredentials
        };

        var token = _tokenHandler.CreateToken(tokenDescriptor);
        return _tokenHandler.WriteToken(token);
    }

    private string GenerateRefreshToken(string jti, string userId, string? orgId, DateTimeOffset expiry, string? platformUserId = null, Tier tier = Tier.Platform)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Jti, jti),
            new("token_use", "refresh")
        };

        if (!string.IsNullOrEmpty(orgId))
        {
            claims.Add(new Claim(TokenClaimConstants.OrgId, orgId));
        }

        // Persist platform_user_id on the refresh token so RefreshTokenAsync
        // can re-emit it on the rebuilt access token. Without this, every
        // refreshed access token drops the claim and all PlatformUser-scoped
        // endpoints (inbox, /hubs/wallet, persona) start 401-ing within the
        // access-token lifetime.
        if (!string.IsNullOrEmpty(platformUserId))
        {
            claims.Add(new Claim("platform_user_id", platformUserId));
        }

        // Refresh tokens carry their tier so a refresh re-mints the same tier (spec 136, FR-012).
        claims.Add(new Claim("tier", TierClaimValue(tier)));

        return GenerateToken(claims, expiry, _audiences.For(tier));
    }

    /// <summary>The <c>tier</c> refresh-token claim value for a human tier.</summary>
    private static string TierClaimValue(Tier tier) => tier switch
    {
        Tier.Consumer => "consumer",
        Tier.Platform => "platform",
        _ => "platform",
    };

    /// <summary>Parses the <c>tier</c> refresh-token claim back to a human <see cref="Tier"/> (default Platform).</summary>
    private static Tier ParseTierClaim(string? value) => value switch
    {
        "consumer" => Tier.Consumer,
        "platform" => Tier.Platform,
        _ => Tier.Platform,
    };

    /// <summary>
    /// Looks up the user's first active linked wallet address and adds it as a JWT claim.
    /// Fails silently if the user has no participant record or no linked wallets.
    /// </summary>
    private async Task AddWalletAddressClaimAsync(
        List<Claim> claims, Guid userId, Guid organizationId, CancellationToken cancellationToken)
    {
        try
        {
            var participant = await _participantRepository.GetByUserAndOrgAsync(userId, organizationId, cancellationToken);
            if (participant is null)
                return;

            var walletLinks = await _participantRepository.GetWalletLinksAsync(participant.Id, includeRevoked: false, cancellationToken);
            var firstActive = walletLinks.FirstOrDefault();
            if (firstActive is not null)
            {
                claims.Add(new Claim("wallet_address", firstActive.WalletAddress));
            }
        }
        catch (Exception ex)
        {
            // Non-critical: wallet_address is optional — don't fail token generation
            _logger.LogWarning(ex, "Failed to resolve wallet_address for user {UserId} in org {OrgId}", userId, organizationId);
        }
    }
}

/// <summary>
/// Extension methods for DateTimeOffset.
/// </summary>
internal static class DateTimeOffsetExtensions
{
    public static long ToUnixTimeSeconds(this DateTime dateTime)
    {
        return new DateTimeOffset(dateTime).ToUnixTimeSeconds();
    }
}
