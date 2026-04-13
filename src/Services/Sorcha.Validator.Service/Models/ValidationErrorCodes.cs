// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.Models;

/// <summary>
/// Canonical string constants for validator error codes. New rules MUST add their
/// code here and reference the constant rather than embedding a literal, so code
/// review and cross-file grepping stay honest.
/// </summary>
/// <remarks>
/// Legacy codes VAL_BP_001..003 are currently emitted as inline string literals
/// in <c>ValidationEngine.cs</c>. Opportunistic migration to this constants class
/// is welcome but not in scope for Feature 103.
/// </remarks>
public static class ValidationErrorCodes
{
    /// <summary>
    /// Blueprint publish rejected: a participant referenced as the sender of an
    /// <c>isStartingAction: true</c> action has a non-null <c>walletAddress</c>
    /// in the submitted blueprint. Starting-action participants MUST be left
    /// unbound so the runtime can late-bind the first qualifying submitter.
    /// </summary>
    /// <remarks>
    /// Contract: <c>specs/103-verified-citizen-v2/contracts/validator-publish-errors.md</c>.
    /// See also <c>.claude/skills/blueprint-builder/SKILL.md</c> — "Open Participants &amp; Late Binding".
    /// </remarks>
    public const string OpenParticipantPrebound = "VAL_BP_010";
}
