// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Validates passwords against NIST SP 800-63B guidelines:
/// minimum 12 characters, no complexity rules, HIBP breach check.
/// </summary>
public interface IPasswordPolicyService
{
    /// <summary>
    /// Validates a password against the policy.
    /// Returns a list of validation errors (empty if valid).
    /// </summary>
    Task<PasswordValidationResult> ValidateAsync(string password, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of password policy validation.
/// </summary>
public record PasswordValidationResult
{
    /// <summary>
    /// Whether the password passed all policy checks.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Validation errors (empty if valid).
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = [];
}
