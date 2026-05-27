// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>Outcomes of <see cref="Services.IPasswordManagementService.SetAsync"/>.</summary>
public enum PasswordSetOutcome
{
    /// <summary>The platform user has no password and one was created.</summary>
    Set = 0,

    /// <summary>The platform user already has a password — caller should use Change instead.</summary>
    AlreadySet = 1,

    /// <summary>The supplied password failed the platform password policy.</summary>
    PolicyViolation = 2,

    /// <summary>The platform user could not be found.</summary>
    NotFound = 3,
}

/// <summary>Outcomes of <see cref="Services.IPasswordManagementService.ChangeAsync"/>.</summary>
public enum PasswordChangeOutcome
{
    /// <summary>The password was rotated.</summary>
    Changed = 0,

    /// <summary>The platform user has no password — caller should use Set instead.</summary>
    NoCurrentPassword = 1,

    /// <summary>The supplied new password failed the platform password policy.</summary>
    PolicyViolation = 2,

    /// <summary>The platform user could not be found.</summary>
    NotFound = 3,
}

/// <summary>Outcomes of <see cref="Services.IPasswordManagementService.RemoveAsync"/>.</summary>
public enum PasswordRemoveOutcome
{
    /// <summary>The password was removed.</summary>
    Removed = 0,

    /// <summary>The platform user had no password to remove.</summary>
    NoCurrentPassword = 1,

    /// <summary>Removing the password would leave the platform user with zero remaining sign-in methods.</summary>
    BlockedByFloor = 2,

    /// <summary>The platform user could not be found.</summary>
    NotFound = 3,
}
