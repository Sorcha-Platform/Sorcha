// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Services;

/// <summary>Model for the <c>verify</c> template.</summary>
public sealed record VerifyEmailTemplateModel(
    string DisplayName,
    string VerifyUrl,
    int ExpiresInHours,
    EmailBranding Branding);

/// <summary>Model for the <c>invite</c> template — carries per-org branding.</summary>
public sealed record InviteEmailTemplateModel(
    string InviterName,
    string OrganizationName,
    string RoleDisplayName,
    string AcceptUrl,
    int ExpiresInDays,
    EmailBranding Branding);

/// <summary>Model for the <c>reset</c> template.</summary>
public sealed record ResetPasswordTemplateModel(
    string DisplayName,
    string ResetUrl,
    int ExpiresInMinutes,
    EmailBranding Branding);

/// <summary>Model for the <c>welcome-public</c> template.</summary>
public sealed record WelcomePublicTemplateModel(
    string DisplayName,
    string DashboardUrl,
    string BrowseRegistersUrl,
    string DemoWorkflowsUrl,
    string DocsUrl,
    EmailBranding Branding);

/// <summary>Model for the <c>welcome-invited</c> template — carries per-org branding.</summary>
public sealed record WelcomeInvitedTemplateModel(
    string DisplayName,
    string OrganizationName,
    string RoleDisplayName,
    string DashboardUrl,
    EmailBranding Branding);

/// <summary>Model for the <c>pairing-resumption</c> template (Feature 128 US2).</summary>
public sealed record PairingResumptionTemplateModel(
    string DisplayName,
    string ResumptionUrl,
    int ExpiresInHours,
    EmailBranding Branding);

/// <summary>Model for the <c>twofactor-code</c> template (Feature 150) — always Sorcha-branded.</summary>
public sealed record TwoFactorCodeTemplateModel(
    string DisplayName,
    string Code,
    int ExpiresInMinutes,
    EmailBranding Branding);

/// <summary>Model for the <c>security-change</c> always-notify template (Feature 150) — always Sorcha-branded.</summary>
public sealed record SecurityChangeTemplateModel(
    string DisplayName,
    string Title,
    string Summary,
    string ManageUrl,
    EmailBranding Branding);
