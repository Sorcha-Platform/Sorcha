// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// PWA-side authentication facade (Feature 114, T109 foundation). Wraps the
/// existing Tenant Service <c>POST /api/auth/login</c> surface so the wallet
/// can obtain a citizen JWT, persist it via <see cref="IAccessTokenStore"/>,
/// and surface a clean signed-in / signed-out signal to the UI.
/// </summary>
public interface IAuthService
{
    /// <summary>True if the wallet currently holds a non-expired access token.</summary>
    Task<bool> IsSignedInAsync(CancellationToken ct = default);

    /// <summary>The signed-in citizen's email if known, else null.</summary>
    Task<string?> GetSignedInEmailAsync(CancellationToken ct = default);

    /// <summary>
    /// Sign in with email + password. Persists the access token on success.
    /// Returns a result describing the outcome. When the account has TOTP 2FA
    /// enabled the result is <see cref="SignInStatus.TwoFactorRequired"/> and
    /// carries a short-lived <see cref="SignInResult.LoginToken"/> to pass to
    /// <see cref="VerifyTwoFactorAsync"/>.
    /// </summary>
    Task<SignInResult> SignInAsync(string email, string password, CancellationToken ct = default);

    /// <summary>
    /// Completes a two-factor login by submitting the TOTP code (or a backup
    /// code) against the <paramref name="loginToken"/> from a prior
    /// <see cref="SignInAsync"/> that returned <see cref="SignInStatus.TwoFactorRequired"/>.
    /// Persists the access token on success.
    /// </summary>
    /// <param name="loginToken">The short-lived login token from the 2FA-required sign-in result.</param>
    /// <param name="email">The email being signed in (stored alongside the token).</param>
    /// <param name="code">Six-digit TOTP code, or an eight-character backup code.</param>
    /// <param name="isBackupCode">True when <paramref name="code"/> is a backup code.</param>
    Task<SignInResult> VerifyTwoFactorAsync(
        string loginToken, string email, string code, bool isBackupCode = false, CancellationToken ct = default);

    /// <summary>Clears the persisted token.</summary>
    Task SignOutAsync(CancellationToken ct = default);
}

/// <summary>Outcome of <see cref="IAuthService.SignInAsync"/> / <see cref="IAuthService.VerifyTwoFactorAsync"/>.</summary>
public sealed record SignInResult(
    SignInStatus Status, string? ErrorMessage = null, string? LoginToken = null)
{
    /// <summary>Convenience for happy-path checks.</summary>
    public bool IsSuccess => Status == SignInStatus.Success;
}

/// <summary>Possible sign-in outcomes.</summary>
public enum SignInStatus
{
    /// <summary>Sign-in completed; token persisted.</summary>
    Success = 0,
    /// <summary>Server rejected the credentials.</summary>
    InvalidCredentials = 1,
    /// <summary>TOTP 2FA required — caller must complete via <see cref="IAuthService.VerifyTwoFactorAsync"/>.</summary>
    TwoFactorRequired = 2,
    /// <summary>Network or server error.</summary>
    ServerError = 3,
}

/// <summary>
/// Default <see cref="IAuthService"/>. Calls <c>POST /api/auth/login</c> via
/// the Tenant Service through the API Gateway. Uses a dedicated unauthenticated
/// HttpClient (no <see cref="BearerTokenHandler"/>) to avoid sending stale
/// tokens with sign-in requests.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly HttpClient _http;
    private readonly IAccessTokenStore _store;

    /// <summary>Initialises a new instance.</summary>
    public AuthService(HttpClient http, IAccessTokenStore store)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<bool> IsSignedInAsync(CancellationToken ct = default)
        => await _store.GetAsync(ct) is not null;

    /// <inheritdoc />
    public async Task<string?> GetSignedInEmailAsync(CancellationToken ct = default)
        => (await _store.GetAsync(ct))?.Email;

    /// <inheritdoc />
    public async Task<SignInResult> SignInAsync(string email, string password, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        try
        {
            var response = await _http.PostAsJsonAsync(
                "api/auth/login",
                new LoginRequest(email.Trim(), password),
                ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new SignInResult(SignInStatus.InvalidCredentials, "Invalid email or password.");
            }
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<LoginResponse>(ct);
            if (body is null) return new SignInResult(SignInStatus.ServerError, "Empty response from auth server.");

            if (body.RequiresTwoFactor)
            {
                if (string.IsNullOrEmpty(body.LoginToken))
                {
                    return new SignInResult(SignInStatus.ServerError,
                        "Auth server signalled 2FA but returned no login token.");
                }
                return new SignInResult(SignInStatus.TwoFactorRequired,
                    "Enter the code from your authenticator app.", body.LoginToken);
            }

            if (string.IsNullOrEmpty(body.AccessToken))
            {
                return new SignInResult(SignInStatus.ServerError, "Auth server did not return an access token.");
            }

            await PersistAsync(body.AccessToken, body.ExpiresIn, email.Trim(), ct);
            return new SignInResult(SignInStatus.Success);
        }
        catch (HttpRequestException ex)
        {
            return new SignInResult(SignInStatus.ServerError, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<SignInResult> VerifyTwoFactorAsync(
        string loginToken, string email, string code, bool isBackupCode = false, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loginToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        try
        {
            var response = await _http.PostAsJsonAsync(
                "api/auth/verify-2fa",
                new Verify2FaRequest(loginToken, code.Trim(), isBackupCode),
                ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new SignInResult(SignInStatus.TwoFactorRequired,
                    "That code wasn't right. Try again.", loginToken);
            }
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<LoginResponse>(ct);
            if (body is null || string.IsNullOrEmpty(body.AccessToken))
            {
                return new SignInResult(SignInStatus.ServerError, "Verification succeeded but no token was returned.");
            }

            await PersistAsync(body.AccessToken, body.ExpiresIn, email.Trim(), ct);
            return new SignInResult(SignInStatus.Success);
        }
        catch (HttpRequestException ex)
        {
            return new SignInResult(SignInStatus.ServerError, ex.Message);
        }
    }

    /// <inheritdoc />
    public Task SignOutAsync(CancellationToken ct = default) => _store.ClearAsync(ct);

    private async Task PersistAsync(string accessToken, int expiresIn, string email, CancellationToken ct)
    {
        var record = new AccessTokenRecord(
            accessToken,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn)),
            email);
        await _store.SetAsync(record, ct);
    }

    private sealed record LoginRequest(string Email, string Password);

    private sealed record Verify2FaRequest(
        [property: JsonPropertyName("login_token")] string LoginToken,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("is_backup_code")] bool IsBackupCode);

    private sealed record LoginResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("requires_two_factor")] bool RequiresTwoFactor,
        [property: JsonPropertyName("login_token")] string? LoginToken);
}
