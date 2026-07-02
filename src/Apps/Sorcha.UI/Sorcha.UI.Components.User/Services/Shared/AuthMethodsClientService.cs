// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Models;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Typed client for the Tenant Service auth-method endpoints (Feature 116).
/// US4 shipped the aggregate read; US1 adds social link/unlink. US2 (passkey
/// rename + soft-revoke) and US3 (password set/change/remove) wire onto this
/// same client in their PRs.
/// </summary>
public interface IAuthMethodsClientService
{
    /// <summary>
    /// Fetch the signed-in user's sign-in methods in a single round-trip.
    /// Returns null on transport failure or 404 (caller renders an error
    /// state rather than throwing).
    /// </summary>
    Task<AuthMethodsResponse?> GetAuthMethodsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Begin a social-provider link flow — POSTs to <c>/api/auth/social/initiate</c>
    /// with <c>intent=link</c>. The signed-in caller's PlatformUserId is captured
    /// server-side into the cached state. Returns the OAuth authorization URL the
    /// browser should navigate to. Null on transport failure.
    /// </summary>
    /// <param name="provider">Provider name: <c>google</c>, <c>github</c>, <c>microsoft</c>, or <c>apple</c>.</param>
    Task<string?> InitiateSocialLinkAsync(string provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unlink a social provider from the signed-in PlatformUser. The caller MUST
    /// have completed a fresh re-authentication challenge and present the resulting
    /// opaque token in <paramref name="challengeToken"/>; the server-side filter
    /// rejects calls without it.
    /// </summary>
    Task<UnlinkSocialOutcome> UnlinkSocialAsync(
        Guid linkId, string challengeToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Begin a re-authentication challenge for <paramref name="scopedOperation"/>.
    /// The server picks the proof method per its ladder (TOTP → Password →
    /// Passkey → ReOAuth) unless <paramref name="preferredMethod"/> is supplied.
    /// Returns null on transport failure or when no method is enrolled (400).
    /// </summary>
    /// <param name="targetMethodKind">
    /// The sign-in method the operation targets, for the ambiguous
    /// <see cref="ScopedOperation.RemoveAuthMethod"/> (passkey-revoke vs social-unlink). Lets the
    /// server compute the correct floor tier (Feature 150). Omit for unambiguous operations.
    /// </param>
    Task<ChallengeInitiateResult?> InitiateChallengeAsync(
        ScopedOperation scopedOperation,
        ChallengeMethod? preferredMethod = null,
        AuthMethodKind? targetMethodKind = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submit the user's proof and exchange it for a one-shot challenge token.
    /// On success the token is bound to <paramref name="scopedOperation"/> for
    /// five minutes; the caller presents it via the <c>X-Auth-Challenge</c>
    /// header on the subsequent mutation call.
    /// </summary>
    /// <param name="targetMethodKind">
    /// The targeted sign-in method (see <see cref="InitiateChallengeAsync"/>). Re-checked
    /// server-side; a below-floor proof returns <see cref="ChallengeVerifyError.ProofTierInsufficient"/>.
    /// </param>
    Task<ChallengeVerifyResult> VerifyChallengeAsync(
        ChallengeMethod method,
        ScopedOperation scopedOperation,
        JsonElement proof,
        AuthMethodKind? targetMethodKind = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Begin a passkey-add ceremony (Feature 116 US2). Returns the FIDO2
    /// creation options for the caller to pass to the browser WebAuthn API,
    /// along with the transaction ID that ties the subsequent verify call.
    /// Null on transport failure or server rejection.
    /// </summary>
    Task<PasskeyAddBegunResult?> BeginAddPasskeyAsync(
        string displayName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finish a passkey-add ceremony — submits the authenticator's attestation
    /// response and the matching transaction ID to the server, which verifies
    /// and creates the credential.
    /// </summary>
    Task<PasskeyAddOutcome> FinishAddPasskeyAsync(
        string transactionId,
        JsonElement attestationResponse,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rename an Active passkey credential. No re-authentication challenge
    /// required — the change is non-destructive.
    /// </summary>
    Task<PasskeyMutationOutcome> RenamePasskeyAsync(
        Guid id, string displayName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-revoke a passkey credential. <paramref name="challengeToken"/> is
    /// required when removing an Active credential and ignored when removing
    /// a Disabled one (server distinguishes). Pass <c>null</c> for Disabled.
    /// </summary>
    Task<PasskeyMutationOutcome> RemovePasskeyAsync(
        Guid id, string? challengeToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set an initial password (Feature 116 US3). <paramref name="challengeToken"/>
    /// is required unless the user is in bootstrap mode (zero other sign-in
    /// methods), in which case the server bypasses the challenge.
    /// </summary>
    Task<PasswordMutationOutcome> SetPasswordAsync(
        string password, string? challengeToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Change the current password. Always requires a fresh challenge token
    /// issued for <see cref="ScopedOperation.ChangePassword"/>.
    /// </summary>
    Task<PasswordMutationOutcome> ChangePasswordAsync(
        string newPassword, string challengeToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear the password. Always requires a fresh challenge token issued for
    /// <see cref="ScopedOperation.RemovePassword"/>. Server enforces the
    /// last-method floor.
    /// </summary>
    Task<PasswordMutationOutcome> RemovePasswordAsync(
        string challengeToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Begin enabling email one-time codes (Feature 150 US2) — sends a confirmation code to the
    /// account email. Returns false on transport failure or while rate-limited (429).
    /// </summary>
    Task<bool> EnableEmailOtpAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirm the emailed code and enable email 2FA. The outcome distinguishes invalid / expired /
    /// rate-limited so the UI can guide the user.
    /// </summary>
    Task<OtpMutationOutcome> VerifyEmailOtpAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Disable email one-time codes. Returns false on transport failure.</summary>
    Task<bool> DisableEmailOtpAsync(CancellationToken cancellationToken = default);

    /// <summary>Capture a mobile number and trigger a verification SMS (Feature 150 US3). False on
    /// failure / rate-limit / when SMS is unavailable (404).</summary>
    Task<bool> CaptureSmsPhoneAsync(string phoneE164, CancellationToken cancellationToken = default);

    /// <summary>Verify the texted code to confirm the number.</summary>
    Task<OtpMutationOutcome> VerifySmsPhoneAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Enable SMS one-time codes (requires a verified number). False on failure / 404.</summary>
    Task<bool> EnableSmsOtpAsync(CancellationToken cancellationToken = default);

    /// <summary>Disable SMS one-time codes. False on transport failure.</summary>
    Task<bool> DisableSmsOtpAsync(CancellationToken cancellationToken = default);
}

/// <summary>Outcome surfaced for an email/SMS OTP enable-verify call (Feature 150).</summary>
public enum OtpMutationOutcome
{
    /// <summary>Server confirmed (204) — the channel is now enabled.</summary>
    Succeeded = 0,
    /// <summary>Server returned 400 — the code didn't match.</summary>
    Invalid = 1,
    /// <summary>Server returned 410 — the code expired or was already used.</summary>
    Expired = 2,
    /// <summary>Server returned 429 — too many attempts / send cooldown.</summary>
    RateLimited = 3,
    /// <summary>Transport failure or unexpected status.</summary>
    Failed = 4,
}

/// <summary>Outcome surfaced for password set / change / remove calls.</summary>
public enum PasswordMutationOutcome
{
    /// <summary>Server confirmed the mutation (204).</summary>
    Succeeded = 0,
    /// <summary>Server returned 400 — the password failed policy validation.</summary>
    PolicyViolation = 1,
    /// <summary>Server returned 409 — already set / not set / last-method floor.</summary>
    Conflict = 2,
    /// <summary>Server returned 401 — usually a stale or missing challenge token.</summary>
    Forbidden = 3,
    /// <summary>Transport failure or unexpected status.</summary>
    Failed = 4,
}

/// <summary>Result of a successful <c>POST /api/passkey/register/options</c>.</summary>
public sealed record PasskeyAddBegunResult(string TransactionId, JsonElement Options);

/// <summary>Outcome of <c>POST /api/passkey/register/verify</c>.</summary>
public enum PasskeyAddOutcome
{
    /// <summary>Server confirmed credential creation.</summary>
    Added = 0,
    /// <summary>Validation failed (e.g. transaction ID mismatch, attestation rejected).</summary>
    Rejected = 1,
    /// <summary>Transport failure or unexpected status.</summary>
    Failed = 2,
}

/// <summary>Outcome surfaced for passkey rename / remove calls.</summary>
public enum PasskeyMutationOutcome
{
    /// <summary>Server confirmed the mutation (204).</summary>
    Succeeded = 0,
    /// <summary>Server returned 409 — last-method floor or invalid state transition.</summary>
    Conflict = 1,
    /// <summary>Server returned 401 — usually a stale or missing challenge token.</summary>
    Forbidden = 2,
    /// <summary>Server returned 404 — credential not found or not owned by caller.</summary>
    NotFound = 3,
    /// <summary>Transport failure or unexpected status.</summary>
    Failed = 4,
}

/// <summary>Outcome of an unlink call surfaced to the UI.</summary>
public enum UnlinkSocialOutcome
{
    /// <summary>Server confirmed the row was hard-deleted.</summary>
    Unlinked = 0,

    /// <summary>Last-method-floor protection refused the removal.</summary>
    LastSignInMethodProtected = 1,

    /// <summary>Server returned 401 — usually a stale or missing challenge token.</summary>
    Forbidden = 2,

    /// <summary>Server returned 404 — link not found or owned by another user.</summary>
    NotFound = 3,

    /// <summary>Transport failure or unexpected status code.</summary>
    Failed = 4,
}

/// <summary>
/// Default <see cref="IAuthMethodsClientService"/> implementation.
/// </summary>
public sealed class AuthMethodsClientService : IAuthMethodsClientService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AuthMethodsClientService> _logger;

    /// <summary>Creates a new <see cref="AuthMethodsClientService"/>.</summary>
    public AuthMethodsClientService(HttpClient httpClient, ILogger<AuthMethodsClientService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AuthMethodsResponse?> GetAuthMethodsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<AuthMethodsResponse>(
                "/api/me/auth-methods", JsonDefaults.Api, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch /api/me/auth-methods");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> InitiateSocialLinkAsync(string provider, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "/api/auth/social/initiate",
                new { provider, intent = "link" },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Social link initiate returned {StatusCode} for {Provider}",
                    response.StatusCode, provider);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<SocialLinkInitiateResponse>(
                JsonDefaults.Api, cancellationToken);
            return payload?.AuthorizationUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate social link for {Provider}", provider);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<UnlinkSocialOutcome> UnlinkSocialAsync(
        Guid linkId, string challengeToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(challengeToken);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/social/{linkId:D}");
            request.Headers.Add("X-Auth-Challenge", challengeToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.NoContent => UnlinkSocialOutcome.Unlinked,
                System.Net.HttpStatusCode.Conflict => UnlinkSocialOutcome.LastSignInMethodProtected,
                System.Net.HttpStatusCode.Unauthorized => UnlinkSocialOutcome.Forbidden,
                System.Net.HttpStatusCode.NotFound => UnlinkSocialOutcome.NotFound,
                _ => UnlinkSocialOutcome.Failed,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unlink social {LinkId} failed", linkId);
            return UnlinkSocialOutcome.Failed;
        }
    }

    /// <inheritdoc />
    public async Task<ChallengeInitiateResult?> InitiateChallengeAsync(
        ScopedOperation scopedOperation,
        ChallengeMethod? preferredMethod = null,
        AuthMethodKind? targetMethodKind = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "/api/auth/challenge/initiate",
                new ChallengeInitiateBody(scopedOperation, preferredMethod, targetMethodKind),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Challenge initiate returned {StatusCode} for {Operation}",
                    response.StatusCode, scopedOperation);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<ChallengeInitiateResult>(
                JsonDefaults.Api, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate challenge for {Operation}", scopedOperation);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<ChallengeVerifyResult> VerifyChallengeAsync(
        ChallengeMethod method,
        ScopedOperation scopedOperation,
        JsonElement proof,
        AuthMethodKind? targetMethodKind = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "/api/auth/challenge/verify",
                new ChallengeVerifyBody(method, scopedOperation, proof, targetMethodKind),
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<ChallengeVerifyBodyResponse>(
                    JsonDefaults.Api, cancellationToken);
                return body is null || string.IsNullOrEmpty(body.Token)
                    ? new ChallengeVerifyResult(false, null, ChallengeVerifyError.Failed)
                    : new ChallengeVerifyResult(true, body.Token, ChallengeVerifyError.None);
            }

            var error = response.StatusCode switch
            {
                System.Net.HttpStatusCode.Forbidden => ChallengeVerifyError.ProofTierInsufficient,
                System.Net.HttpStatusCode.Unauthorized => ChallengeVerifyError.ProofRejected,
                System.Net.HttpStatusCode.Gone => ChallengeVerifyError.Expired,
                _ => ChallengeVerifyError.Failed,
            };
            return new ChallengeVerifyResult(false, null, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Challenge verify failed for {Method}/{Operation}", method, scopedOperation);
            return new ChallengeVerifyResult(false, null, ChallengeVerifyError.Failed);
        }
    }

    /// <inheritdoc />
    public async Task<PasskeyAddBegunResult?> BeginAddPasskeyAsync(
        string displayName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "/api/passkey/register/options",
                new PasskeyRegisterOptionsBody(displayName),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Passkey register/options returned {StatusCode}", response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<PasskeyRegisterOptionsResponseBody>(
                JsonDefaults.Api, cancellationToken);
            return body is null
                ? null
                : new PasskeyAddBegunResult(body.TransactionId, body.Options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to begin passkey add");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<PasskeyAddOutcome> FinishAddPasskeyAsync(
        string transactionId,
        JsonElement attestationResponse,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(transactionId);
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "/api/passkey/register/verify",
                new PasskeyRegisterVerifyBody(transactionId, attestationResponse),
                cancellationToken);

            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.Created => PasskeyAddOutcome.Added,
                System.Net.HttpStatusCode.OK => PasskeyAddOutcome.Added,
                System.Net.HttpStatusCode.BadRequest => PasskeyAddOutcome.Rejected,
                System.Net.HttpStatusCode.UnprocessableEntity => PasskeyAddOutcome.Rejected,
                _ => PasskeyAddOutcome.Failed,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to finish passkey add");
            return PasskeyAddOutcome.Failed;
        }
    }

    /// <inheritdoc />
    public async Task<PasskeyMutationOutcome> RenamePasskeyAsync(
        Guid id, string displayName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        try
        {
            using var response = await _httpClient.PutAsJsonAsync(
                $"/api/passkey/credentials/{id:D}",
                new PasskeyRenameBody(displayName),
                cancellationToken);

            return MapMutation(response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename passkey {Id}", id);
            return PasskeyMutationOutcome.Failed;
        }
    }

    /// <inheritdoc />
    public async Task<PasskeyMutationOutcome> RemovePasskeyAsync(
        Guid id, string? challengeToken, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete, $"/api/passkey/credentials/{id:D}");
            if (!string.IsNullOrEmpty(challengeToken))
            {
                request.Headers.Add("X-Auth-Challenge", challengeToken);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return MapMutation(response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove passkey {Id}", id);
            return PasskeyMutationOutcome.Failed;
        }
    }

    private static PasskeyMutationOutcome MapMutation(System.Net.HttpStatusCode code) => code switch
    {
        System.Net.HttpStatusCode.NoContent => PasskeyMutationOutcome.Succeeded,
        System.Net.HttpStatusCode.OK => PasskeyMutationOutcome.Succeeded,
        System.Net.HttpStatusCode.Conflict => PasskeyMutationOutcome.Conflict,
        System.Net.HttpStatusCode.Unauthorized => PasskeyMutationOutcome.Forbidden,
        System.Net.HttpStatusCode.NotFound => PasskeyMutationOutcome.NotFound,
        _ => PasskeyMutationOutcome.Failed,
    };

    private sealed record SocialLinkInitiateResponse
    {
        public string AuthorizationUrl { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
    }

    private sealed record ChallengeInitiateBody(
        ScopedOperation ScopedOperation, ChallengeMethod? PreferredMethod, AuthMethodKind? TargetMethodKind);

    private sealed record ChallengeVerifyBody(
        ChallengeMethod Method, ScopedOperation ScopedOperation, JsonElement Proof, AuthMethodKind? TargetMethodKind);

    private sealed record ChallengeVerifyBodyResponse(string Token, int ExpiresIn);

    private sealed record PasskeyRegisterOptionsBody(
        [property: System.Text.Json.Serialization.JsonPropertyName("display_name")] string DisplayName);

    private sealed record PasskeyRegisterOptionsResponseBody(
        [property: System.Text.Json.Serialization.JsonPropertyName("transaction_id")] string TransactionId,
        [property: System.Text.Json.Serialization.JsonPropertyName("options")] JsonElement Options);

    private sealed record PasskeyRegisterVerifyBody(
        [property: System.Text.Json.Serialization.JsonPropertyName("transaction_id")] string TransactionId,
        [property: System.Text.Json.Serialization.JsonPropertyName("attestation_response")] JsonElement AttestationResponse);

    private sealed record PasskeyRenameBody(
        [property: System.Text.Json.Serialization.JsonPropertyName("display_name")] string DisplayName);

    /// <inheritdoc />
    public Task<PasswordMutationOutcome> SetPasswordAsync(
        string password, string? challengeToken, CancellationToken cancellationToken = default)
        => PostPasswordAsync("/api/auth/password/set", password, challengeToken, cancellationToken);

    /// <inheritdoc />
    public Task<PasswordMutationOutcome> ChangePasswordAsync(
        string newPassword, string challengeToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(challengeToken);
        return PostPasswordAsync("/api/auth/password/change", newPassword, challengeToken, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PasswordMutationOutcome> RemovePasswordAsync(
        string challengeToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(challengeToken);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/password/remove");
            request.Headers.Add("X-Auth-Challenge", challengeToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return MapPassword(response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Password remove failed");
            return PasswordMutationOutcome.Failed;
        }
    }

    /// <inheritdoc />
    public async Task<bool> EnableEmailOtpAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsync("/api/me/2fa/email/enable", null, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Enable email OTP failed");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<OtpMutationOutcome> VerifyEmailOtpAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "/api/me/2fa/email/verify", new { code }, cancellationToken);
            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.NoContent => OtpMutationOutcome.Succeeded,
                System.Net.HttpStatusCode.OK => OtpMutationOutcome.Succeeded,
                System.Net.HttpStatusCode.BadRequest => OtpMutationOutcome.Invalid,
                System.Net.HttpStatusCode.Gone => OtpMutationOutcome.Expired,
                System.Net.HttpStatusCode.TooManyRequests => OtpMutationOutcome.RateLimited,
                _ => OtpMutationOutcome.Failed,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Verify email OTP failed");
            return OtpMutationOutcome.Failed;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DisableEmailOtpAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.DeleteAsync("/api/me/2fa/email", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Disable email OTP failed");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> CaptureSmsPhoneAsync(string phoneE164, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneE164);
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "/api/me/2fa/sms/phone", new { phone = phoneE164 }, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogError(ex, "Capture SMS phone failed"); return false; }
    }

    /// <inheritdoc />
    public async Task<OtpMutationOutcome> VerifySmsPhoneAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "/api/me/2fa/sms/phone/verify", new { code }, cancellationToken);
            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.NoContent => OtpMutationOutcome.Succeeded,
                System.Net.HttpStatusCode.BadRequest => OtpMutationOutcome.Invalid,
                System.Net.HttpStatusCode.Gone => OtpMutationOutcome.Expired,
                System.Net.HttpStatusCode.TooManyRequests => OtpMutationOutcome.RateLimited,
                _ => OtpMutationOutcome.Failed,
            };
        }
        catch (Exception ex) { _logger.LogError(ex, "Verify SMS phone failed"); return OtpMutationOutcome.Failed; }
    }

    /// <inheritdoc />
    public async Task<bool> EnableSmsOtpAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsync("/api/me/2fa/sms/enable", null, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogError(ex, "Enable SMS OTP failed"); return false; }
    }

    /// <inheritdoc />
    public async Task<bool> DisableSmsOtpAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.DeleteAsync("/api/me/2fa/sms", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogError(ex, "Disable SMS OTP failed"); return false; }
    }

    private async Task<PasswordMutationOutcome> PostPasswordAsync(
        string path,
        string password,
        string? challengeToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(new PasswordBody(password)),
            };
            if (!string.IsNullOrEmpty(challengeToken))
            {
                request.Headers.Add("X-Auth-Challenge", challengeToken);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return MapPassword(response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Password mutation POST {Path} failed", path);
            return PasswordMutationOutcome.Failed;
        }
    }

    private static PasswordMutationOutcome MapPassword(System.Net.HttpStatusCode code) => code switch
    {
        System.Net.HttpStatusCode.NoContent => PasswordMutationOutcome.Succeeded,
        System.Net.HttpStatusCode.OK => PasswordMutationOutcome.Succeeded,
        System.Net.HttpStatusCode.BadRequest => PasswordMutationOutcome.PolicyViolation,
        System.Net.HttpStatusCode.UnprocessableEntity => PasswordMutationOutcome.PolicyViolation,
        System.Net.HttpStatusCode.Conflict => PasswordMutationOutcome.Conflict,
        System.Net.HttpStatusCode.Unauthorized => PasswordMutationOutcome.Forbidden,
        _ => PasswordMutationOutcome.Failed,
    };

    private sealed record PasswordBody(
        [property: System.Text.Json.Serialization.JsonPropertyName("password")] string Password);
}
