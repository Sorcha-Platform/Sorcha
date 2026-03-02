// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sorcha.ServiceClients.Auth;
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
    private readonly ILogger<TokenService> _logger;
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly SigningCredentials? _signingCredentials;
    private readonly TokenValidationParameters _validationParameters;

    public TokenService(
        IOptions<JwtConfiguration> options,
        ITokenRevocationService revocationService,
        ILogger<TokenService> logger)
    {
        _config = options?.Value ?? new JwtConfiguration();
        _revocationService = revocationService ?? throw new ArgumentNullException(nameof(revocationService));
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(organization);

        var accessTokenJti = Guid.NewGuid().ToString();
        var refreshTokenJti = Guid.NewGuid().ToString();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, accessTokenJti),
            new("name", user.DisplayName),
            new(TokenClaimConstants.OrgId, organization.Id.ToString()),
            new("org_name", organization.Name),
            new(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser)
        };

        // Add role claims
        foreach (var role in user.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
        }

        var accessTokenExpiry = DateTimeOffset.UtcNow.AddMinutes(_config.AccessTokenLifetimeMinutes);
        var refreshTokenExpiry = DateTimeOffset.UtcNow.AddHours(_config.RefreshTokenLifetimeHours);

        var accessToken = GenerateToken(claims, accessTokenExpiry);
        var refreshToken = GenerateRefreshToken(refreshTokenJti, user.Id.ToString(), organization.Id.ToString(), refreshTokenExpiry);

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
    public async Task<TokenResponse> GeneratePublicUserTokenAsync(
        PublicIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var accessTokenJti = Guid.NewGuid().ToString();
        var refreshTokenJti = Guid.NewGuid().ToString();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, identity.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, accessTokenJti),
            new(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser),
            new("auth_method", "passkey")
        };

        // Add device type if available
        if (!string.IsNullOrEmpty(identity.DeviceType))
        {
            claims.Add(new Claim("device_type", identity.DeviceType));
        }

        var accessTokenExpiry = DateTimeOffset.UtcNow.AddMinutes(_config.AccessTokenLifetimeMinutes);
        var refreshTokenExpiry = DateTimeOffset.UtcNow.AddHours(_config.RefreshTokenLifetimeHours);

        var accessToken = GenerateToken(claims, accessTokenExpiry);
        var refreshToken = GenerateRefreshToken(refreshTokenJti, identity.Id.ToString(), null, refreshTokenExpiry);

        // Track tokens
        await _revocationService.TrackTokenAsync(
            accessTokenJti, identity.Id.ToString(), null, accessTokenExpiry, cancellationToken);
        await _revocationService.TrackTokenAsync(
            refreshTokenJti, identity.Id.ToString(), null, refreshTokenExpiry, cancellationToken);

        _logger.LogInformation("Generated tokens for public user {UserId}", identity.Id);

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

        var accessToken = GenerateToken(claims, accessTokenExpiry);

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

            // Extract claims for new token
            var userId = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            var orgId = principal.FindFirst(TokenClaimConstants.OrgId)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return null;
            }

            // Generate new access token with same claims but new JTI and expiry
            var newAccessTokenJti = Guid.NewGuid().ToString();
            var accessTokenExpiry = DateTimeOffset.UtcNow.AddMinutes(_config.AccessTokenLifetimeMinutes);

            var claims = principal.Claims
                .Where(c => c.Type != JwtRegisteredClaimNames.Jti &&
                            c.Type != JwtRegisteredClaimNames.Exp &&
                            c.Type != JwtRegisteredClaimNames.Iat &&
                            c.Type != "token_use")
                .ToList();

            claims.Add(new Claim(JwtRegisteredClaimNames.Jti, newAccessTokenJti));

            var accessToken = GenerateToken(claims, accessTokenExpiry);

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

    private string GenerateToken(IEnumerable<Claim> claims, DateTimeOffset expiry)
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
            Audience = _config.Audiences.FirstOrDefault(),
            SigningCredentials = _signingCredentials
        };

        var token = _tokenHandler.CreateToken(tokenDescriptor);
        return _tokenHandler.WriteToken(token);
    }

    private string GenerateRefreshToken(string jti, string userId, string? orgId, DateTimeOffset expiry)
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

        return GenerateToken(claims, expiry);
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
