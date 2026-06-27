// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using Sorcha.ServiceDefaults.Auth;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Pages.Auth;

/// <summary>
/// Server-rendered Social OAuth callback page model.
/// Handles the OAuth provider redirect, exchanges the code for user claims,
/// resolves/creates PlatformUser, and issues a JWT via fragment redirect.
/// </summary>
public class SocialCallbackModel : PageModel
{
    private readonly ISocialLoginService _socialLoginService;
    private readonly IPlatformUserService _platformUserService;
    private readonly IIdentityRepository _identityRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly ITokenService _tokenService;
    private readonly IWelcomeEmailDispatcher _welcomeDispatcher;
    private readonly TenantDbContext _db;
    private readonly ILogger<SocialCallbackModel> _logger;
    private readonly IPlatformUserDeviceService _deviceService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SocialCallbackModel"/> class.
    /// </summary>
    public SocialCallbackModel(
        ISocialLoginService socialLoginService,
        IPlatformUserService platformUserService,
        IIdentityRepository identityRepository,
        IOrganizationRepository organizationRepository,
        ITokenService tokenService,
        IWelcomeEmailDispatcher welcomeDispatcher,
        TenantDbContext db,
        ILogger<SocialCallbackModel> logger,
        IPlatformUserDeviceService deviceService)
    {
        _socialLoginService = socialLoginService;
        _platformUserService = platformUserService;
        _identityRepository = identityRepository;
        _organizationRepository = organizationRepository;
        _tokenService = tokenService;
        _welcomeDispatcher = welcomeDispatcher;
        _db = db;
        _logger = logger;
        _deviceService = deviceService;
    }

    /// <summary>
    /// Error message to display when the social login flow fails.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// True during the initial page render while the OAuth exchange is in progress.
    /// </summary>
    public bool IsProcessing { get; set; } = true;

    /// <summary>
    /// Handles GET requests from the OAuth provider redirect.
    /// Exchanges the authorization code (which internally resolves the
    /// provider from the cached <c>state</c> data — feature 115 FR-021),
    /// resolves/creates user under the strict link policy, and redirects
    /// with JWT on success or renders a refusal message on policy gate.
    /// </summary>
    public async Task<IActionResult> OnGetAsync(
        string? code,
        string? state,
        string? error,
        CancellationToken ct)
    {
        IsProcessing = false;

        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("Social login callback received error: {Error}", error);
            ErrorMessage = "The sign-in was cancelled or failed. Please try again.";
            // Provider name is unknown at this point (we never reached state-cache lookup);
            // tag as "unknown" so the counter still surfaces volume of cancelled flows.
            SocialLoginMetrics.RecordUpstreamFailure("unknown", "provider_error");
            return Page();
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            ErrorMessage = "Invalid callback parameters. Please try signing in again.";
            SocialLoginMetrics.RecordUpstreamFailure("unknown", "missing_params");
            return Page();
        }

        // The provider is recovered from cached state inside ExchangeCodeAsync;
        // it is NOT a query parameter (OAuth providers do not preserve query
        // parameters across the redirect, so the previous behaviour was relying
        // on a value that was never actually present). We pass an empty string
        // here to maintain the existing service signature; ExchangeCodeAsync
        // returns the resolved provider on the result.
        var callbackResult = await _socialLoginService.ExchangeCodeAsync(string.Empty, code, state, ct);
        var provider = callbackResult.Provider;
        var isWallet = IsWalletSurface(callbackResult.Surface);

        if (!callbackResult.Success || string.IsNullOrEmpty(callbackResult.Subject))
        {
            _logger.LogWarning("Social login exchange failed for {Provider}: {Error}", provider, callbackResult.Error);
            ErrorMessage = callbackResult.Error ?? "Authentication failed. Please try again.";
            // Tag the cause based on what ExchangeCodeAsync surfaced — the message is
            // user-facing copy, so we map it back to a stable telemetry tag rather
            // than tagging the message verbatim.
            var reasonTag = callbackResult.Error switch
            {
                { } e when e.Contains("state", StringComparison.OrdinalIgnoreCase) => "state_invalid",
                { } e when e.Contains("token", StringComparison.OrdinalIgnoreCase) => "code_exchange_failed",
                _ => "code_exchange_failed",
            };
            SocialLoginMetrics.RecordUpstreamFailure(provider, reasonTag);
            return Page();
        }

        // Resolve or create PlatformUser under the strict link policy.
        // Wallet surface is login-only — do not create new accounts via social sign-in.
        var resolveResult = await _platformUserService.ResolveOrCreateSocialUserAsync(callbackResult, allowCreate: !isWallet, ct);

        // Refusal handling (feature 115 FR-016, FR-018)
        if (resolveResult.Refusal != SocialLoginRefusal.None)
        {
            ErrorMessage = resolveResult.Refusal switch
            {
                SocialLoginRefusal.ProviderUnverified =>
                    $"Your {provider} account hasn't verified this email address. Please verify it with {provider} and try again.",
                SocialLoginRefusal.ExistingUnverified =>
                    "An account exists for this email but isn't verified. Sign in with your password and verify your email first, or recover access at /auth/login.",
                _ => "Authentication failed. Please try again.",
            };

            // Structured warning + redacted email tag for telemetry (FR-018).
            var emailTag = HashEmailForLog(callbackResult.Email);
            _logger.LogWarning(
                "Social login refused for provider {Provider}: reason={Reason}, emailHash={EmailHash}",
                provider, resolveResult.Refusal, emailTag);

            SocialLoginMetrics.RecordRefusal(provider, resolveResult.Refusal);
            if (isWallet)
            {
                var errorCode = resolveResult.Refusal == SocialLoginRefusal.NoExistingAccount ? "no_account" : "refused";
                return Redirect(BuildWalletSignInError(errorCode));
            }
            return Page();
        }

        var platformUser = resolveResult.User!;
        var isNew = resolveResult.IsNew;

        // Ensure UserIdentity in public org
        var publicOrgId = WellKnownIds.PublicOrgId;
        var userIdentity = await _db.UserIdentities
            .FirstOrDefaultAsync(u => u.PlatformUserId == platformUser.Id && u.OrganizationId == publicOrgId, ct);

        if (userIdentity is null)
        {
            userIdentity = new UserIdentity
            {
                OrganizationId = publicOrgId,
                PlatformUserId = platformUser.Id,
                Email = platformUser.Email,
                DisplayName = platformUser.DisplayName,
                Roles = [UserRole.Consumer],
                Status = IdentityStatus.Active,
                ProvisionedVia = ProvisioningMethod.SocialLogin,
                ProfileCompleted = !string.IsNullOrWhiteSpace(platformUser.Email)
                    && !string.IsNullOrWhiteSpace(platformUser.DisplayName)
            };
            await _identityRepository.CreateUserAsync(userIdentity, ct);
        }

        // Ensure org membership
        var memberships = await _platformUserService.GetOrgMembershipsAsync(platformUser.Id, ct);
        if (!memberships.Any(m => m.OrganizationId == publicOrgId))
        {
            await _platformUserService.AddOrgMembershipAsync(
                platformUser.Id, publicOrgId, UserRole.Consumer.ToString(), ct);
        }

        // Update last login
        userIdentity.LastLoginAt = DateTimeOffset.UtcNow;
        await _identityRepository.UpdateUserAsync(userIdentity, ct);

        // Get public org and issue JWT
        var publicOrg = await _organizationRepository.GetByIdAsync(publicOrgId, ct);
        if (publicOrg is null)
        {
            ErrorMessage = "Platform configuration error. Please contact support.";
            return Page();
        }

        var mintTier = isWallet
            ? Tier.Consumer
            : Tier.Platform;
        var tokens = await _tokenService.GenerateUserTokenAsync(userIdentity, publicOrg, platformUser.Id, mintTier, platformUser.EmailVerified, ct);

        _logger.LogInformation("Social login completed for PlatformUser {PlatformUserId} via {Provider} (isNew={IsNew})",
            platformUser.Id, provider, isNew);

        // Welcome email — social/passkey paths skip email verification (IdP already
        // asserted the address), so first-login is the natural welcome moment.
        // Idempotent + non-throwing by design.
        await _welcomeDispatcher.SendIfPendingAsync(platformUser, ct);

        if (isWallet)
        {
            // Wallet PWA: hand tokens back via the /wallet fragment. No SetupAddDevice
            // returnUrl here — that route lives in the /app web client, not the PWA;
            // the PWA handles "no device yet" with its own PairingTakeover overlay
            // (a /setup/add-device returnUrl would 404 in the wallet).
            return Redirect(BuildWalletRedirect(tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresIn, returnUrl: null));
        }

        // Redirect to app with token in fragment.
        // Feature 128 US2 — same FR-020 routing-gate contract as Login.cshtml.cs.
        // Social flow has no user-provided ReturnUrl, so the gate is the only
        // decision point. The /app/ prefix is load-bearing — see
        // SetupAddDeviceRoutingGate XML doc.
        var fragment = $"token={Uri.EscapeDataString(tokens.AccessToken)}" +
                       $"&refresh={Uri.EscapeDataString(tokens.RefreshToken)}";
        if (await SetupAddDeviceRoutingGate.ShouldRouteAsync(tokens.AccessToken, _deviceService, _logger, ct))
        {
            fragment += $"&returnUrl={Uri.EscapeDataString(SetupAddDeviceRoutingGate.SetupAddDevicePath)}";
        }
        return Redirect($"/app/#{fragment}");
    }

    /// <summary>
    /// Hashes an email to an 8-character hex tag suitable for log correlation
    /// without exposing the address itself. The 4-byte (32-bit) truncation is
    /// deliberate PII minimisation — collisions are tolerable for log
    /// correlation, and shorter tags reduce the chance a leaked log line
    /// contributes to re-identification. Returns "(none)" when input is
    /// null or empty. Feature 115 FR-018.
    /// </summary>
    private static string HashEmailForLog(string? email)
    {
        if (string.IsNullOrEmpty(email))
        {
            return "(none)";
        }

        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(email.ToLowerInvariant()));
        return Convert.ToHexString(bytes.AsSpan(0, 4)).ToLowerInvariant();
    }

    /// <summary>True when the social flow originated from the citizen wallet PWA.</summary>
    internal static bool IsWalletSurface(string? surface)
        => string.Equals(surface, "wallet", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the wallet fragment-return URL. The PWA's auth-fragment.js reads
    /// token/refresh/expires_in from the hash on load. Base-relative /wallet/ —
    /// the PWA is mounted under that prefix at the gateway.
    /// </summary>
    internal static string BuildWalletRedirect(string accessToken, string refreshToken, int expiresIn, string? returnUrl)
    {
        var fragment = $"token={Uri.EscapeDataString(accessToken)}" +
                       $"&refresh={Uri.EscapeDataString(refreshToken)}" +
                       $"&expires_in={expiresIn}";
        if (!string.IsNullOrEmpty(returnUrl))
            fragment += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
        return $"/wallet/#{fragment}";
    }

    /// <summary>Routes a wallet-surface failure/refusal back to the PWA sign-in screen.</summary>
    internal static string BuildWalletSignInError(string code)
        => $"/wallet/signin?authError={Uri.EscapeDataString(code)}";
}
