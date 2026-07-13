// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sorcha.AtomicCache;
using Sorcha.ServiceDefaults.Auth;
using Sorcha.Tenant.Service.Extensions;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Mints and redeems one-time enrolment session tokens.
/// Feature 126 — see <c>specs/126-enrol-inside-wizard/data-model.md</c>
/// and <c>research.md §R-002/§R-003</c>.
/// </summary>
/// <remarks>
/// Implementation notes:
/// <list type="bullet">
///   <item><description>The session JWT is signed with the same key as
///   <see cref="TokenService"/> (HMAC-SHA256 over the configured signing
///   key). It is identified by <c>scope: "enrol"</c> — the redeem endpoint
///   rejects any token whose scope is not exactly that.</description></item>
///   <item><description>Single-use is enforced by writing a sentinel value
///   to <see cref="IAtomicDistributedCache"/> at mint time and consuming it
///   atomically at redeem time via <see cref="IAtomicDistributedCache.GetAndRemoveAsync"/>.
///   Absent-entry at redeem with a still-valid JWT means
///   <see cref="RedeemEnrolSessionErrorCode.AlreadyUsed"/>.</description></item>
///   <item><description>The redeem response's <c>displayName</c> + <c>email</c>
///   are sourced fresh from <see cref="IPlatformUserService.GetByIdAsync"/>
///   at redeem time, so they reflect the user's current profile rather than
///   a stale cache from mint.</description></item>
/// </list>
/// </remarks>
public sealed class EnrolSessionService : IEnrolSessionService
{
    /// <summary>Token scope claim value identifying these tokens.</summary>
    public const string EnrolScope = "enrol";

    /// <summary>
    /// JWT claim carrying the <see cref="EnrolSessionMode"/> discriminator
    /// (F128). Persisted in-token so single-use enforcement is unchanged
    /// (only the JTI is tracked server-side).
    /// </summary>
    public const string PairModeClaim = "pair_mode";

    /// <summary>Cache key prefix for the single-use JTI registry.</summary>
    public const string JtiRegistryKeyPrefix = "sorcha:enrol-session:";

    /// <summary>Token TTL — 10 minutes. Matches data-model.md §"One-time JWT (session token)".</summary>
    public static readonly TimeSpan SessionTokenLifetime = TimeSpan.FromMinutes(10);

    private readonly JwtConfiguration _jwtConfig;
    private readonly IAtomicDistributedCache _cache;
    private readonly IPlatformUserService _platformUserService;
    private readonly EnrolSessionMetrics _metrics;
    private readonly ILogger<EnrolSessionService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };
    private readonly SymmetricSecurityKey _signingKey;
    private readonly SigningCredentials _signingCreds;
    private readonly SorchaAudiences _audiences;

    /// <summary>
    /// Configuration key overriding the origin the enrolment QR points at. Rarely needed: the QR opens
    /// the <b>wallet PWA</b>, which this installation serves at <c>/wallet/enrol</c>, so the platform's
    /// own public origin (<see cref="PublicOriginFallbackConfigKey"/>) is the right answer almost
    /// everywhere — including when the citizen scans it from a third-party council page.
    /// </summary>
    public const string CouncilOriginConfigKey = "EnrolSession:CouncilOrigin";

    /// <summary>
    /// The installation's public origin, already configured on every real deployment (it is what
    /// verification and invitation links are built from). Used as the QR origin when
    /// <see cref="CouncilOriginConfigKey"/> is not set.
    /// </summary>
    public const string PublicOriginFallbackConfigKey = "Email:BaseUrl";

    /// <summary>
    /// Last-resort origin for a dev box that has configured neither key. It is a <b>fictional</b>
    /// council domain, so a QR built from it goes nowhere — hence the loud warning at startup.
    /// </summary>
    private const string DemoOriginOfLastResort = "https://strathcarron.gov";

    private readonly string _councilOrigin;

    public EnrolSessionService(
        IOptions<JwtConfiguration> jwtOptions,
        IAtomicDistributedCache cache,
        IPlatformUserService platformUserService,
        EnrolSessionMetrics metrics,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<EnrolSessionService> logger)
    {
        ArgumentNullException.ThrowIfNull(jwtOptions);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(platformUserService);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _jwtConfig = jwtOptions.Value;
        _audiences = new SorchaAudiences(_jwtConfig.InstallationName);
        _cache = cache;
        _platformUserService = platformUserService;
        _metrics = metrics;
        _timeProvider = timeProvider;
        _logger = logger;

        if (string.IsNullOrEmpty(_jwtConfig.SigningKey))
        {
            throw new InvalidOperationException(
                "JwtSettings:SigningKey must be configured for EnrolSessionService to mint and validate session tokens.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(_jwtConfig.SigningKey);
        if (keyBytes.Length < 32)
        {
            Array.Resize(ref keyBytes, 32);
        }
        _signingKey = new SymmetricSecurityKey(keyBytes);
        _signingCreds = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

        // The QR sends the citizen's phone to THIS installation's wallet PWA, so default to the
        // installation's public origin rather than a council's. Falling through to the demo domain
        // means the deployment configured neither key — the QR would point at a domain the operator
        // does not own, so say so loudly rather than shipping a dead QR silently.
        _councilOrigin = configuration[CouncilOriginConfigKey]
            ?? configuration[PublicOriginFallbackConfigKey]
            ?? DemoOriginOfLastResort;

        if (string.Equals(_councilOrigin, DemoOriginOfLastResort, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Enrolment QR codes will point at {Origin} — a fictional demo domain — because neither "
                + "{OverrideKey} nor {FallbackKey} is configured. Scanning such a QR will go nowhere. "
                + "Set {FallbackKey} to this installation's public origin.",
                DemoOriginOfLastResort, CouncilOriginConfigKey, PublicOriginFallbackConfigKey);
        }
    }

    /// <inheritdoc />
    public async Task<MintEnrolSessionResponse> MintAsync(
        Guid platformUserId,
        EnrolSessionMode mode,
        CancellationToken ct)
    {
        if (platformUserId == Guid.Empty)
        {
            throw new ArgumentException("PlatformUserId must be non-empty.", nameof(platformUserId));
        }

        var jti = Guid.NewGuid().ToString("N");
        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.Add(SessionTokenLifetime);

        var modeWire = ModeToWire(mode);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, platformUserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, jti),
            new("scope", EnrolScope),
            new(PairModeClaim, modeWire),
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Issuer = _jwtConfig.Issuer,
            // Distinct tier audience so the standard auth pipeline never accepts these
            // as a normal access token even by accident (spec 136).
            Audience = _audiences.For(Tier.EnrolSession),
            SigningCredentials = _signingCreds,
        };

        var token = _handler.CreateToken(descriptor);
        var jwt = _handler.WriteToken(token);

        // Pre-write the sentinel so RedeemAsync's GetAndRemoveAsync enforces single-use.
        var cacheKey = JtiRegistryKeyPrefix + jti;
        await _cache.SetAsync(cacheKey, "pending", SessionTokenLifetime, ct).ConfigureAwait(false);

        var qrUrl = $"{_councilOrigin.TrimEnd('/')}/wallet/enrol?session={jwt}";

        _metrics.RecordMint("tier3_first_qr", modeWire);
        _logger.LogInformation(
            "Minted enrol-session token (jti={Jti}, platformUserId={PlatformUserId}, mode={Mode}, exp={Exp:O})",
            jti, platformUserId, modeWire, expiresAt);

        return new MintEnrolSessionResponse(jwt, qrUrl, expiresAt, mode);
    }

    private static string ModeToWire(EnrolSessionMode mode) => mode switch
    {
        EnrolSessionMode.Gated => "gated",
        EnrolSessionMode.Standalone => "standalone",
        _ => "gated",
    };

    private static EnrolSessionMode ModeFromClaim(string? wire) => wire switch
    {
        "standalone" => EnrolSessionMode.Standalone,
        _ => EnrolSessionMode.Gated,
    };

    /// <inheritdoc />
    public async Task<RedeemResult> RedeemAsync(string sessionToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            _metrics.RecordRedeem("malformed", "unknown");
            return RedeemResult.Fail(RedeemEnrolSessionErrorCode.MalformedToken, "Session token is required.");
        }

        // Parse without validation first so we can surface a clean
        // malformed-vs-signature-fail distinction.
        JwtSecurityToken parsed;
        try
        {
            parsed = _handler.ReadJwtToken(sessionToken);
        }
        catch (ArgumentException)
        {
            _metrics.RecordRedeem("malformed", "unknown");
            return RedeemResult.Fail(RedeemEnrolSessionErrorCode.MalformedToken, "Token could not be parsed.");
        }
        catch (SecurityTokenException)
        {
            _metrics.RecordRedeem("malformed", "unknown");
            return RedeemResult.Fail(RedeemEnrolSessionErrorCode.MalformedToken, "Token could not be parsed.");
        }

        // Validate signature, issuer, audience — but NOT lifetime. We re-check
        // expiry below using the injected TimeProvider so tests can drive
        // expired-token paths deterministically.
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _jwtConfig.Issuer,
            ValidateAudience = true,
            ValidAudience = _audiences.For(Tier.EnrolSession),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ValidateLifetime = false,
        };

        ClaimsPrincipal principal;
        try
        {
            principal = _handler.ValidateToken(sessionToken, validationParameters, out _);
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            _metrics.RecordRedeem("signature_fail", "unknown");
            return RedeemResult.Fail(RedeemEnrolSessionErrorCode.InvalidSignature, "Signature did not validate.");
        }
        catch (SecurityTokenException)
        {
            _metrics.RecordRedeem("malformed", "unknown");
            return RedeemResult.Fail(RedeemEnrolSessionErrorCode.MalformedToken, "Token failed validation.");
        }

        // Explicit expiry check via TimeProvider.
        var expClaim = principal.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
        if (expClaim is null || !long.TryParse(expClaim, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expEpoch))
        {
            _metrics.RecordRedeem("malformed", "unknown");
            return RedeemResult.Fail(RedeemEnrolSessionErrorCode.MalformedToken, "Token is missing exp claim.");
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expEpoch);
        if (_timeProvider.GetUtcNow() >= expiresAt)
        {
            _metrics.RecordRedeem("expired", ModeToWire(ModeFromClaim(principal.FindFirst(PairModeClaim)?.Value)));
            return RedeemResult.Fail(RedeemEnrolSessionErrorCode.Expired, "Session token has expired.");
        }

        var scope = principal.FindFirst("scope")?.Value;
        if (!string.Equals(scope, EnrolScope, StringComparison.Ordinal))
        {
            _metrics.RecordRedeem("scope_mismatch", ModeToWire(ModeFromClaim(principal.FindFirst(PairModeClaim)?.Value)));
            return RedeemResult.Fail(RedeemEnrolSessionErrorCode.ScopeMismatch, "Token scope does not permit enrolment.");
        }

        var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (string.IsNullOrEmpty(sub) || !Guid.TryParse(sub, out var platformUserId) || string.IsNullOrEmpty(jti))
        {
            _metrics.RecordRedeem("malformed", "unknown");
            return RedeemResult.Fail(RedeemEnrolSessionErrorCode.MalformedToken, "Token is missing required claims.");
        }

        // Atomic single-use consume. Absent → already used (or expired but our
        // cache TTL is wider than the JWT TTL so this is replay).
        var cacheKey = JtiRegistryKeyPrefix + jti;
        var sentinel = await _cache.GetAndRemoveAsync(cacheKey, ct).ConfigureAwait(false);
        if (sentinel is null)
        {
            _metrics.RecordRedeem("replay", ModeToWire(ModeFromClaim(principal.FindFirst(PairModeClaim)?.Value)));
            _logger.LogWarning(
                "Enrol-session redeem replay detected (jti={Jti}, platformUserId={PlatformUserId})",
                jti, platformUserId);
            return RedeemResult.Fail(RedeemEnrolSessionErrorCode.AlreadyUsed, "Session token has already been used.");
        }

        // Look up fresh user details for the PWA confirmation dialog.
        var user = await _platformUserService.GetByIdAsync(platformUserId, ct).ConfigureAwait(false);
        if (user is null)
        {
            _metrics.RecordRedeem("malformed", "unknown");
            _logger.LogWarning(
                "Enrol-session redeem failed — PlatformUser not found (jti={Jti}, platformUserId={PlatformUserId})",
                jti, platformUserId);
            return RedeemResult.Fail(RedeemEnrolSessionErrorCode.MalformedToken, "Bound user no longer exists.");
        }

        var (accessToken, expiresInSeconds) = IssueCitizenAccessToken(user);

        // Read mode from claim. Tokens minted before F128 lack this claim and
        // fall through to the Gated default — preserving F126 behaviour for
        // any in-flight token at the time of deployment.
        var mode = ModeFromClaim(principal.FindFirst(PairModeClaim)?.Value);

        _metrics.RecordRedeem("success", ModeToWire(mode));
        _logger.LogInformation(
            "Enrol-session redeemed successfully (jti={Jti}, platformUserId={PlatformUserId}, mode={Mode})",
            jti, platformUserId, ModeToWire(mode));

        return RedeemResult.Ok(new RedeemEnrolSessionResponse(
            AccessToken: accessToken,
            ExpiresIn: expiresInSeconds,
            DisplayName: user.DisplayName ?? user.Email,
            Email: user.Email,
            Mode: mode));
    }

    private (string AccessToken, int ExpiresInSeconds) IssueCitizenAccessToken(PlatformUser user)
    {
        var now = _timeProvider.GetUtcNow();
        var lifetimeMinutes = _jwtConfig.AccessTokenLifetimeMinutes;
        var expires = now.AddMinutes(lifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("name", user.DisplayName ?? user.Email),
            new("platform_user_id", user.Id.ToString()),
            new("token_use", "access"),
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Issuer = _jwtConfig.Issuer,
            // The redeemed citizen access token is a consumer-tier token (spec 136).
            Audience = _audiences.For(Tier.Consumer),
            SigningCredentials = _signingCreds,
        };

        var token = _handler.CreateToken(descriptor);
        var jwt = _handler.WriteToken(token);
        var expiresIn = (int)((expires - now).TotalSeconds);
        return (jwt, expiresIn);
    }
}
