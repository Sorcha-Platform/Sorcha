// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tools.Participant;

/// <summary>
/// Reports who the calling session acts as (#1705): organisation, roles, tier, both user ids, and
/// the tools this session may call.
/// </summary>
/// <remarks>
/// <para>
/// Cold-start run #8's agent spent four calls working out which organisation its own token belonged
/// to, and got the answer only because one audit-log entry happened to name it. The MCP server
/// forwards the caller's bearer and holds no identity of its own, so the organisation is already
/// fixed by the token; the agent simply could not see it.
/// </para>
/// <para>
/// Everything but the organisation's display name comes from the caller's own token, read without
/// re-validating it — the transport already validated it, and the platform re-checks it on every
/// backend call. This tool reports; it never authorises.
/// </para>
/// <para>
/// <b>The two user ids are labelled, never merged.</b> <c>platform_user_id</c> is the account-wide
/// id (inbox, wallet ownership); <c>sub</c> is the per-organisation <c>UserIdentity</c> id. They
/// differ for every real user (CLAUDE.md §26), so a missing <c>platform_user_id</c> claim is
/// reported as missing, not filled in from <c>sub</c>.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class WhoAmITool
{
    private const string ToolName = "sorcha_whoami";

    private readonly IMcpAuthorizationService _authService;
    private readonly ICallerContext _caller;
    private readonly ITenantServiceClient _tenantClient;
    private readonly ILogger<WhoAmITool> _logger;

    /// <summary>Creates the tool.</summary>
    public WhoAmITool(
        IMcpAuthorizationService authService,
        ICallerContext caller,
        ITenantServiceClient tenantClient,
        ILogger<WhoAmITool> logger)
    {
        _authService = authService;
        _caller = caller;
        _tenantClient = tenantClient;
        _logger = logger;
    }

    /// <summary>Reports the identity the calling session acts as.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session's organisation, roles, tier, user ids and visible tools.</returns>
    [McpServerTool(Name = ToolName, Destructive = false, ReadOnly = true, Idempotent = true)]
    [Description("Reports who this session acts as: the organisation id and name its token is bound to, its roles, its trust tier, its account-wide platform user id and its per-organisation user identity id (labelled separately — they are different ids), the token's expiry, and the tools this session is allowed to call. Call this first, before any organisation-scoped operation, instead of inferring your organisation from sorcha_tenant_list or from audit entries: every org-scoped action (publishing a participant record, creating a register) acts for the organisation in this token and never for one you name. Use sorcha_my_wallets rather than this tool to list the wallets you can sign with.")]
    public async Task<WhoAmIResult> WhoAmIAsync(CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName) || string.IsNullOrWhiteSpace(_caller.RawToken))
        {
            return new WhoAmIResult
            {
                Status = "Unauthorized",
                Message = "No authenticated session: this MCP connection carries no valid bearer token.",
                CheckedAt = DateTimeOffset.UtcNow,
            };
        }

        JwtSecurityToken token;
        try
        {
            token = new JwtSecurityTokenHandler().ReadJwtToken(_caller.RawToken);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException)
        {
            return new WhoAmIResult
            {
                Status = "Error",
                Message = "The session's bearer token could not be read.",
                CheckedAt = DateTimeOffset.UtcNow,
            };
        }

        string? Claim(string type) => token.Claims.FirstOrDefault(c => c.Type == type)?.Value;

        var organizationId = Claim(TokenClaimConstants.OrgId);
        var platformRoles = token.Claims
            .Where(c => c.Type is "role" or "roles" or System.Security.Claims.ClaimTypes.Role)
            .Select(c => c.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var tier = _caller.Tier;
        var mcpRoles = _caller.Roles.ToList();

        var (organizationName, organizationNote) = await ResolveOrganizationNameAsync(organizationId, cancellationToken);

        var message = organizationId is null
            ? "This session is not bound to any organisation, so organisation-scoped operations will be refused."
            : $"This session acts for organisation {(organizationName is null ? $"'{organizationId}'" : $"'{organizationName}' ({organizationId})")} "
              + $"at the {tier?.ToString().ToLowerInvariant() ?? "unrecognised"} tier"
              + (platformRoles.Count > 0 ? $", with roles {string.Join(", ", platformRoles)}." : ", with no roles.");

        return new WhoAmIResult
        {
            Status = "Success",
            Message = message,
            CheckedAt = DateTimeOffset.UtcNow,
            OrganizationId = organizationId,
            OrganizationName = organizationName,
            OrganizationNameNote = organizationNote,
            Tier = tier?.ToString(),
            Roles = platformRoles,
            McpRoles = mcpRoles,
            PlatformUserId = Claim(TokenClaimConstants.PlatformUserId),
            UserIdentityId = Claim(JwtRegisteredClaimNames.Sub),
            Email = Claim(JwtRegisteredClaimNames.Email) ?? Claim("email"),
            DisplayName = Claim("name"),
            TokenExpiresAt = token.ValidTo == DateTime.MinValue ? null : new DateTimeOffset(token.ValidTo, TimeSpan.Zero),
            VisibleTools = ToolEntitlements.VisibleTools(tier, mcpRoles),
        };
    }

    /// <summary>
    /// Best-effort organisation display name. A refusal or an outage leaves it null with a note —
    /// the id from the token is the authoritative answer either way.
    /// </summary>
    private async Task<(string? Name, string? Note)> ResolveOrganizationNameAsync(
        string? organizationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            return (null, null);
        }

        try
        {
            var body = await _tenantClient.GetOrganizationAsync(organizationId, cancellationToken);
            if (body is null)
            {
                return (null, "The organisation's name could not be read with this token; the id above is authoritative.");
            }

            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                ? (name.GetString(), null)
                : (null, "The organisation record carried no name.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "whoami could not read organisation {OrgId}", organizationId);
            return (null, "The Tenant Service could not be reached to read the organisation's name; the id above is authoritative.");
        }
    }
}

/// <summary>Result of <c>sorcha_whoami</c>.</summary>
public sealed record WhoAmIResult
{
    /// <summary>Status: Success, Unauthorized, or Error.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable summary.</summary>
    public required string Message { get; init; }

    /// <summary>When the call was answered.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>The organisation this session acts for (the token's <c>org_id</c>).</summary>
    public string? OrganizationId { get; init; }

    /// <summary>The organisation's display name, when it could be read.</summary>
    public string? OrganizationName { get; init; }

    /// <summary>Why <see cref="OrganizationName"/> is missing, when it is.</summary>
    public string? OrganizationNameNote { get; init; }

    /// <summary>The session's trust tier: Consumer, Platform, Service, or EnrolSession.</summary>
    public string? Tier { get; init; }

    /// <summary>The roles the token carries, as the platform names them (e.g. Administrator).</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>The same roles in MCP form (e.g. <c>sorcha:admin</c>), which decide which tools are listed.</summary>
    public IReadOnlyList<string> McpRoles { get; init; } = [];

    /// <summary>Account-wide id (<c>platform_user_id</c>) — what wallets and the inbox are keyed on. Null when the token omits it.</summary>
    public string? PlatformUserId { get; init; }

    /// <summary>Per-organisation id (<c>sub</c>, a <c>UserIdentity</c> id). NOT interchangeable with <see cref="PlatformUserId"/>.</summary>
    public string? UserIdentityId { get; init; }

    /// <summary>The account's email, when the token carries it.</summary>
    public string? Email { get; init; }

    /// <summary>The account's display name, when the token carries it.</summary>
    public string? DisplayName { get; init; }

    /// <summary>When the session's token expires (UTC). After this, every call is refused.</summary>
    public DateTimeOffset? TokenExpiresAt { get; init; }

    /// <summary>The tools this session is allowed to call, sorted by name.</summary>
    public IReadOnlyList<string> VisibleTools { get; init; } = [];
}
