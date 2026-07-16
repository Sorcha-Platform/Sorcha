// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.RegularExpressions;
using FluentValidation;
using Sorcha.CitizenWallet.Abstractions.Models;

namespace Sorcha.CitizenWallet.Abstractions.Validators;

/// <summary>
/// Validates a KB-JWT signing request (#1195 Phase 2, Task 6a). Shape-level checks only —
/// the endpoint separately decodes the header and enforces <c>typ: "kb+jwt"</c> (the
/// signing-oracle guard) because that requires base64url + JSON work FluentValidation
/// shouldn't own.
/// </summary>
public sealed partial class KbJwtSignRequestValidator : AbstractValidator<KbJwtSignRequest>
{
    /// <summary>Two non-empty base64url segments joined by a single dot (a compact JWS signing input).</summary>
    [GeneratedRegex("^[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+$")]
    private static partial Regex SigningInputShape();

    /// <summary>Initialise validation rules.</summary>
    public KbJwtSignRequestValidator()
    {
        RuleFor(x => x.SigningInput)
            .NotEmpty()
            .MaximumLength(16384)
            .Matches(SigningInputShape())
            .WithMessage("SigningInput must be a compact JWS signing input: base64url(header).base64url(payload).");
    }
}
