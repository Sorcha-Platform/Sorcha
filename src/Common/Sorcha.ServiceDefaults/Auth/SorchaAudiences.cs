// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceDefaults.Auth;

/// <summary>
/// The trust tier a JWT belongs to. The tier is carried in the token's audience
/// (<c>{installation}:{suffix}</c>) and is the unit of cross-tier isolation:
/// consumer ⊥ platform ⊥ service. See spec 136 (tiered-audience identity model).
/// </summary>
public enum Tier
{
    /// <summary>Citizen / wallet holder — web + PWA consumer surfaces. Lowest-privilege human tier.</summary>
    Consumer,

    /// <summary>Org admin / designer / auditor / operator — admin and org-management surfaces.</summary>
    Platform,

    /// <summary>Service-to-service / infrastructure — internal APIs only.</summary>
    Service,

    /// <summary>One-time device-pairing session — enrolment redemption only, not a general access token.</summary>
    EnrolSession,
}

/// <summary>
/// Single source of truth for Sorcha JWT audience strings. Both token issuance
/// (Tenant Service) and token validation (every service) MUST derive audiences
/// from here so the two can never diverge.
/// <para>
/// Audiences are namespaced by installation: <c>{installationName}:{tier-suffix}</c>
/// (e.g. <c>sorcha:consumer</c>, <c>acme:platform</c>). The installation name defaults
/// to <see cref="DefaultInstallation"/> and is overridable per deployment for white-label
/// installs. The namespace is defense-in-depth on top of the per-installation signing key
/// + issuer — it is not the primary cross-installation boundary. See spec 136.
/// </para>
/// </summary>
public sealed class SorchaAudiences
{
    /// <summary>The installation-name default when none is configured.</summary>
    public const string DefaultInstallation = "sorcha";

    private const string ConsumerSuffix = "consumer";
    private const string PlatformSuffix = "platform";
    private const string ServiceSuffix = "service";
    private const string EnrolSessionSuffix = "enrol-session";

    /// <summary>The normalised installation name driving this instance's audiences.</summary>
    public string InstallationName { get; }

    private readonly string _consumer;
    private readonly string _platform;
    private readonly string _service;
    private readonly string _enrolSession;

    /// <summary>
    /// Creates an audience set for the given installation name. A null, empty, or
    /// whitespace name normalises to <see cref="DefaultInstallation"/>; the name is
    /// trimmed and lower-cased so audiences are stable regardless of casing/whitespace.
    /// </summary>
    public SorchaAudiences(string? installationName)
    {
        InstallationName = Normalize(installationName);
        _consumer = $"{InstallationName}:{ConsumerSuffix}";
        _platform = $"{InstallationName}:{PlatformSuffix}";
        _service = $"{InstallationName}:{ServiceSuffix}";
        _enrolSession = $"{InstallationName}:{EnrolSessionSuffix}";
    }

    /// <summary>Normalises an installation name: null/blank → <see cref="DefaultInstallation"/>; trimmed + lower-cased.</summary>
    public static string Normalize(string? installationName) =>
        string.IsNullOrWhiteSpace(installationName)
            ? DefaultInstallation
            : installationName.Trim().ToLowerInvariant();

    /// <summary>The audience string for a given <see cref="Tier"/>.</summary>
    public string For(Tier tier) => tier switch
    {
        Tier.Consumer => _consumer,
        Tier.Platform => _platform,
        Tier.Service => _service,
        Tier.EnrolSession => _enrolSession,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown tier."),
    };

    /// <summary>All four tier audiences for this installation — use as the bearer <c>ValidAudiences</c> set.</summary>
    public IReadOnlyList<string> All => [_consumer, _platform, _service, _enrolSession];

    /// <summary>
    /// Resolves the <see cref="Tier"/> for a known audience string, or null if the value
    /// is not one of this installation's tier audiences. Used by validation/authorization.
    /// </summary>
    public Tier? TierFor(string? audience)
    {
        if (audience == _consumer) return Tier.Consumer;
        if (audience == _platform) return Tier.Platform;
        if (audience == _service) return Tier.Service;
        if (audience == _enrolSession) return Tier.EnrolSession;
        return null;
    }
}
