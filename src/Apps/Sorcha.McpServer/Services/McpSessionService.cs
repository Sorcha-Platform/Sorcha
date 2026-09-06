// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.ServiceDefaults.Auth;

namespace Sorcha.McpServer.Services;

/// <summary>
/// Manages the current MCP session context derived from JWT authentication. Also serves as
/// the stdio-transport <see cref="ICallerContext"/> — one caller per process (spec 139).
/// </summary>
public sealed class McpSessionService : IMcpSessionService, ICallerContext
{
    private readonly IJwtValidationHandler _jwtHandler;
    private readonly ILogger<McpSessionService> _logger;
    private McpSession? _currentSession;
    private string? _rawToken;

    public McpSessionService(
        IJwtValidationHandler jwtHandler,
        ILogger<McpSessionService> logger)
    {
        _jwtHandler = jwtHandler;
        _logger = logger;
    }

    /// <inheritdoc />
    public McpSession? CurrentSession => _currentSession;

    /// <inheritdoc />
    public void InitializeFromToken(string jwtToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jwtToken);

        _rawToken = jwtToken;

        var validationResult = _jwtHandler.ValidateToken(jwtToken);

        if (!validationResult.IsValid)
        {
            _logger.LogError("JWT validation failed: {ErrorCode} - {ErrorMessage}",
                validationResult.ErrorCode, validationResult.ErrorMessage);
            throw new InvalidOperationException(
                $"Invalid JWT token: {validationResult.ErrorMessage}");
        }

        var jwt = validationResult.Token!;
        var principal = validationResult.Principal!;

        // Extract user identifier (sub claim)
        var userId = GetClaimValue(principal, JwtRegisteredClaimNames.Sub)
            ?? GetClaimValue(principal, ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("JWT missing user identifier claim (sub)");

        // Extract organization/tenant ID
        var tenantId = GetClaimValue(principal, "org_id")
            ?? GetClaimValue(principal, "tenant_id")
            ?? GetClaimValue(principal, "tid")
            ?? "default";

        // Extract organization name
        var organizationName = GetClaimValue(principal, "org_name");

        // Extract roles - Sorcha uses both ClaimTypes.Role and custom role claims
        var roles = principal.Claims
            .Where(c => c.Type == ClaimTypes.Role ||
                        c.Type == "role" ||
                        c.Type == "roles" ||
                        c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
            .Select(c => c.Value)
            .Distinct()
            .ToList();

        // Map standard roles to Sorcha MCP roles if needed
        roles = MapToMcpRoles(roles);

        // Extract optional claims
        var walletAddress = GetClaimValue(principal, "wallet_address");
        var email = GetClaimValue(principal, JwtRegisteredClaimNames.Email)
            ?? GetClaimValue(principal, ClaimTypes.Email);
        var displayName = GetClaimValue(principal, "name")
            ?? GetClaimValue(principal, ClaimTypes.Name);

        // Token type (user or service)
        var tokenType = GetClaimValue(principal, "token_type") ?? "user";

        // Service-specific claims
        var clientId = GetClaimValue(principal, "client_id");
        var serviceName = GetClaimValue(principal, "service_name");

        // Extract scopes for service tokens
        var scopes = principal.Claims
            .Where(c => c.Type == "scope")
            .Select(c => c.Value)
            .ToList();

        // Spec 139: derive the F136 trust tier from the token audience(s).
        var tier = TierResolution.Resolve(jwt.Audiences);

        _currentSession = new McpSession
        {
            UserId = userId,
            TenantId = tenantId,
            OrganizationName = organizationName,
            Roles = roles,
            Tier = tier,
            WalletAddress = walletAddress,
            Email = email,
            DisplayName = displayName,
            TokenType = tokenType,
            ClientId = clientId,
            ServiceName = serviceName,
            Scopes = scopes,
            ExpiresAt = jwt.ValidTo,
            IssuedAt = jwt.IssuedAt,
            TokenId = GetClaimValue(principal, JwtRegisteredClaimNames.Jti)
        };

        _logger.LogInformation(
            "Session initialized for {TokenType} {UserId} in tenant {TenantId} with roles [{Roles}], expires at {ExpiresAt}",
            tokenType,
            userId,
            tenantId,
            string.Join(", ", roles),
            jwt.ValidTo);
    }

    /// <inheritdoc />
    public bool IsTokenExpired()
    {
        if (_currentSession is null)
        {
            return true;
        }

        var isExpired = DateTimeOffset.UtcNow >= _currentSession.ExpiresAt;

        if (isExpired)
        {
            _logger.LogWarning("Session token has expired for user {UserId}", _currentSession.UserId);
        }

        return isExpired;
    }

    /// <summary>
    /// Gets the raw JWT token for forwarding to backend services.
    /// </summary>
    public string? GetRawToken() => _rawToken;

    // --- ICallerContext (stdio transport: one caller per process) ---

    /// <inheritdoc />
    public string? RawToken => _rawToken;

    /// <inheritdoc />
    public Tier? Tier => _currentSession?.Tier;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Roles => _currentSession?.Roles ?? [];

    /// <inheritdoc />
    public string? OrganizationId => _currentSession?.TenantId;

    /// <inheritdoc />
    public string? Subject => _currentSession?.UserId;

    /// <inheritdoc />
    public bool IsAuthenticated => _currentSession is not null && !IsTokenExpired();

    private static string? GetClaimValue(ClaimsPrincipal principal, string claimType)
    {
        return principal.FindFirst(claimType)?.Value;
    }

    /// <summary>
    /// Maps standard role names to Sorcha MCP role format. Delegates to the single-home
    /// <see cref="McpRoleNormalizer"/> (mirrors the HTTP path in <see cref="Infrastructure.HttpCallerContext"/>).
    /// </summary>
    private static List<string> MapToMcpRoles(List<string> roles) =>
        McpRoleNormalizer.NormalizeAll(roles);
}
