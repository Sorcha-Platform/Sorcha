// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Sorcha.McpServer.Infrastructure;

/// <summary>
/// Handles JWT token validation for the MCP server.
/// </summary>
public interface IJwtValidationHandler
{
    /// <summary>
    /// Validates a JWT token and returns the claims principal.
    /// </summary>
    /// <param name="token">The JWT token to validate.</param>
    /// <returns>Validation result with claims if successful.</returns>
    JwtValidationResult ValidateToken(string token);
}

/// <summary>
/// Result of JWT validation.
/// </summary>
public sealed record JwtValidationResult
{
    /// <summary>
    /// Whether the token is valid.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// The validated claims principal, if valid.
    /// </summary>
    public ClaimsPrincipal? Principal { get; init; }

    /// <summary>
    /// The parsed JWT token, if valid.
    /// </summary>
    public JwtSecurityToken? Token { get; init; }

    /// <summary>
    /// Error message if validation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Error code for categorizing the failure.
    /// </summary>
    public JwtValidationErrorCode ErrorCode { get; init; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static JwtValidationResult Success(ClaimsPrincipal principal, JwtSecurityToken token) =>
        new()
        {
            IsValid = true,
            Principal = principal,
            Token = token,
            ErrorCode = JwtValidationErrorCode.None
        };

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    public static JwtValidationResult Failure(JwtValidationErrorCode code, string message) =>
        new()
        {
            IsValid = false,
            ErrorMessage = message,
            ErrorCode = code
        };
}

/// <summary>
/// Error codes for JWT validation failures.
/// </summary>
public enum JwtValidationErrorCode
{
    None = 0,
    MalformedToken,
    InvalidSignature,
    TokenExpired,
    InvalidIssuer,
    InvalidAudience,
    MissingClaims,
    TokenRevoked,
    UnknownError
}

/// <summary>
/// Configuration options for JWT validation.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>
    /// Expected token issuer. Spec 136: resolved at startup from the installation identity
    /// (<see cref="Sorcha.ServiceDefaults.Auth.SorchaIssuer"/>) — no shared default.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Expected token audiences. Spec 136: derived at startup from the installation's tier
    /// audiences (<see cref="Sorcha.ServiceDefaults.Auth.SorchaAudiences"/>) — not hardcoded.
    /// </summary>
    public string[] Audiences { get; set; } = [];

    /// <summary>
    /// Signing key for HMAC validation (development only).
    /// Production should use asymmetric keys from Tenant Service.
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>
    /// Whether to validate the issuer claim.
    /// </summary>
    public bool ValidateIssuer { get; set; } = true;

    /// <summary>
    /// Whether to validate the audience claim.
    /// </summary>
    public bool ValidateAudience { get; set; } = true;

    /// <summary>
    /// Whether to validate the signing key.
    /// </summary>
    public bool ValidateIssuerSigningKey { get; set; } = true;

    /// <summary>
    /// Whether to validate token lifetime.
    /// </summary>
    public bool ValidateLifetime { get; set; } = true;

    /// <summary>
    /// Clock skew tolerance in minutes.
    /// </summary>
    public int ClockSkewMinutes { get; set; } = 5;

    /// <summary>
    /// Whether to skip validation entirely (for development/testing).
    /// When true, tokens are parsed but not cryptographically validated.
    /// </summary>
    public bool SkipValidation { get; set; } = false;
}

/// <summary>
/// Handles JWT token validation for the MCP server.
/// </summary>
public sealed class JwtValidationHandler : IJwtValidationHandler
{
    private readonly ILogger<JwtValidationHandler> _logger;
    private readonly JwtOptions _options;
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly TokenValidationParameters? _validationParameters;

    public JwtValidationHandler(
        IOptions<JwtOptions> options,
        ILogger<JwtValidationHandler> logger)
    {
        _options = options.Value;
        _logger = logger;
        _tokenHandler = new JwtSecurityTokenHandler();

        // Only set up validation parameters if not skipping validation
        if (!_options.SkipValidation && !string.IsNullOrEmpty(_options.SigningKey))
        {
            var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);
            if (keyBytes.Length < 32)
            {
                Array.Resize(ref keyBytes, 32);
            }
            var securityKey = new SymmetricSecurityKey(keyBytes);

            _validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = _options.ValidateIssuer,
                ValidIssuer = _options.Issuer,
                ValidateAudience = _options.ValidateAudience,
                ValidAudiences = _options.Audiences,
                ValidateIssuerSigningKey = _options.ValidateIssuerSigningKey,
                IssuerSigningKey = securityKey,
                ValidateLifetime = _options.ValidateLifetime,
                ClockSkew = TimeSpan.FromMinutes(_options.ClockSkewMinutes)
            };
        }
    }

    /// <inheritdoc />
    public JwtValidationResult ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return JwtValidationResult.Failure(
                JwtValidationErrorCode.MalformedToken,
                "Token is empty or null");
        }

        try
        {
            // If skipping validation (dev mode), just parse the token
            if (_options.SkipValidation)
            {
                _logger.LogWarning("JWT validation is disabled - parsing token without cryptographic verification");
                return ParseTokenWithoutValidation(token);
            }

            // If no signing key configured, we can't validate but can parse
            if (_validationParameters == null)
            {
                _logger.LogWarning("JWT signing key not configured - parsing token without cryptographic verification");
                return ParseTokenWithoutValidation(token);
            }

            // Full validation
            var principal = _tokenHandler.ValidateToken(token, _validationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwt)
            {
                return JwtValidationResult.Failure(
                    JwtValidationErrorCode.MalformedToken,
                    "Token is not a valid JWT");
            }

            _logger.LogDebug("JWT validated successfully for subject {Sub}",
                jwt.Subject);

            return JwtValidationResult.Success(principal, jwt);
        }
        catch (SecurityTokenMalformedException ex)
        {
            _logger.LogWarning(ex, "Malformed JWT token");
            return JwtValidationResult.Failure(
                JwtValidationErrorCode.MalformedToken,
                "Token format is invalid");
        }
        catch (SecurityTokenExpiredException ex)
        {
            _logger.LogWarning(ex, "Expired JWT token");
            return JwtValidationResult.Failure(
                JwtValidationErrorCode.TokenExpired,
                "Token has expired");
        }
        catch (SecurityTokenInvalidSignatureException ex)
        {
            _logger.LogWarning(ex, "Invalid JWT signature");
            return JwtValidationResult.Failure(
                JwtValidationErrorCode.InvalidSignature,
                "Token signature is invalid");
        }
        catch (SecurityTokenInvalidIssuerException ex)
        {
            _logger.LogWarning(ex, "Invalid JWT issuer");
            return JwtValidationResult.Failure(
                JwtValidationErrorCode.InvalidIssuer,
                "Token issuer is not trusted");
        }
        catch (SecurityTokenInvalidAudienceException ex)
        {
            _logger.LogWarning(ex, "Invalid JWT audience");
            return JwtValidationResult.Failure(
                JwtValidationErrorCode.InvalidAudience,
                "Token audience is not valid");
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "JWT validation failed");
            return JwtValidationResult.Failure(
                JwtValidationErrorCode.UnknownError,
                $"Token validation failed: {ex.Message}");
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid JWT argument");
            return JwtValidationResult.Failure(
                JwtValidationErrorCode.MalformedToken,
                "Token format is invalid");
        }
    }

    private JwtValidationResult ParseTokenWithoutValidation(string token)
    {
        try
        {
            var jwt = _tokenHandler.ReadJwtToken(token);

            // Check expiration manually
            if (jwt.ValidTo < DateTime.UtcNow)
            {
                return JwtValidationResult.Failure(
                    JwtValidationErrorCode.TokenExpired,
                    "Token has expired");
            }

            // Create a claims principal from the token
            var identity = new ClaimsIdentity(jwt.Claims, "jwt");
            var principal = new ClaimsPrincipal(identity);

            return JwtValidationResult.Success(principal, jwt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse JWT token");
            return JwtValidationResult.Failure(
                JwtValidationErrorCode.MalformedToken,
                "Token format is invalid");
        }
    }
}
