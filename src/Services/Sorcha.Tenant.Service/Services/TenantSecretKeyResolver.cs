// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Sorcha.Tenant.Service.Extensions;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Resolves the symmetric key material used by <see cref="SoftwareSecretProtectionProvider"/> and
/// for signing the short-lived 2FA intermediate (login) token.
/// </summary>
/// <remarks>
/// <para>
/// <b>At-rest protection key</b> (<see cref="ResolveProtectionKey"/>):
/// an explicit <c>Tenant:SecretProtection:Key</c> (base64, 32 bytes) takes precedence; otherwise the
/// key is HKDF-SHA256-derived from the existing JWT signing key (no new mandatory config). If neither
/// resolves the service fails closed (throws at startup) — required in Production/Staging, and applied
/// everywhere for safety.
/// </para>
/// <para>
/// <b>Login-token signing key</b> (<see cref="ResolveLoginTokenSigningKey"/>):
/// HKDF-SHA256-derived from the JWT signing key with a distinct <c>info</c> label, giving a stable,
/// deployment-wide HMAC key (replacing the previous per-process random key so 2FA intermediate tokens
/// validate across replicas/restarts). This is an HMAC signing key, not AEAD — it does not flow
/// through <see cref="ISecretProtectionProvider"/>.
/// </para>
/// <para>HKDF distinct <c>info</c> labels give cryptographic domain separation from the signing use.</para>
/// </remarks>
public sealed class TenantSecretKeyResolver
{
    /// <summary>Configuration path for the optional explicit protection-key override.</summary>
    public const string OverrideConfigPath = "Tenant:SecretProtection:Key";

    /// <summary>KeyId recorded when the protection key is HKDF-derived from the JWT signing key.</summary>
    public const string DerivedKeyId = "jwt-derived-v1";

    /// <summary>KeyId recorded when an explicit override key is configured.</summary>
    public const string ConfigKeyId = "config-v1";

    private const int KeyLength = 32;
    private const string ProtectionInfo = "sorcha:tenant:secret-protection:v1";
    private const string LoginTokenInfo = "sorcha:tenant:login-token-hmac:v1";

    private readonly JwtConfiguration _jwt;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<TenantSecretKeyResolver> _logger;

    /// <summary>DI constructor.</summary>
    public TenantSecretKeyResolver(
        IOptions<JwtConfiguration> jwtOptions,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<TenantSecretKeyResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(jwtOptions);
        _jwt = jwtOptions.Value ?? new JwtConfiguration();
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resolves the 32-byte at-rest protection key and its KeyId.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The override is set but not a valid base64 32-byte key, or neither the override nor a JWT signing
    /// key is available (fail-closed).
    /// </exception>
    public (byte[] Key, string KeyId) ResolveProtectionKey()
    {
        var overrideKey = _configuration[OverrideConfigPath];
        if (!string.IsNullOrWhiteSpace(overrideKey))
        {
            byte[] keyBytes;
            try
            {
                keyBytes = Convert.FromBase64String(overrideKey);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"{OverrideConfigPath} must be a base64-encoded 32-byte key (invalid base64).", ex);
            }

            if (keyBytes.Length != KeyLength)
            {
                throw new InvalidOperationException(
                    $"{OverrideConfigPath} must decode to exactly {KeyLength} bytes; got {keyBytes.Length}.");
            }

            _logger.LogInformation(
                "Tenant secret-protection key resolved from configured override ({KeyId}).", ConfigKeyId);
            return (keyBytes, ConfigKeyId);
        }

        if (!string.IsNullOrEmpty(_jwt.SigningKey))
        {
            var key = Derive(_jwt.SigningKey, ProtectionInfo);
            _logger.LogInformation(
                "Tenant secret-protection key derived from the JWT signing key ({KeyId}).", DerivedKeyId);
            return (key, DerivedKeyId);
        }

        throw new InvalidOperationException(
            "No Tenant secret-protection key could be resolved: set JwtSettings:SigningKey (the default " +
            $"derivation source) or {OverrideConfigPath} (a base64-encoded 32-byte key). " +
            $"The service refuses to start without one (environment: {_environment.EnvironmentName}).");
    }

    /// <summary>
    /// Resolves the 32-byte HMAC key for signing the 2FA intermediate (login) token, derived from the
    /// JWT signing key with a distinct info label. Stable across replicas/restarts.
    /// </summary>
    /// <exception cref="InvalidOperationException">No JWT signing key is configured (fail-closed).</exception>
    public byte[] ResolveLoginTokenSigningKey()
    {
        if (string.IsNullOrEmpty(_jwt.SigningKey))
        {
            throw new InvalidOperationException(
                "Cannot derive the login-token signing key: JwtSettings:SigningKey is not configured " +
                $"(environment: {_environment.EnvironmentName}).");
        }

        return Derive(_jwt.SigningKey, LoginTokenInfo);
    }

    private static byte[] Derive(string rootKey, string info) =>
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: Encoding.UTF8.GetBytes(rootKey),
            outputLength: KeyLength,
            salt: null,
            info: Encoding.UTF8.GetBytes(info));
}

/// <summary>
/// Singleton holder for the HMAC key used to sign the 2FA intermediate (login) token (Feature 146).
/// Derived once from the JWT signing key via <see cref="TenantSecretKeyResolver"/> so the key is
/// stable across replicas and restarts — replacing the previous per-process random key.
/// </summary>
public sealed class LoginTokenSigningKey
{
    /// <summary>The 32-byte HMAC-SHA256 signing key.</summary>
    public byte[] Key { get; }

    /// <summary>Constructs the holder with an already-derived key (used by tests).</summary>
    public LoginTokenSigningKey(byte[] key) =>
        Key = key ?? throw new ArgumentNullException(nameof(key));

    /// <summary>DI constructor — derives the key from the resolver.</summary>
    public LoginTokenSigningKey(TenantSecretKeyResolver resolver)
        : this((resolver ?? throw new ArgumentNullException(nameof(resolver))).ResolveLoginTokenSigningKey()) { }
}
