// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentValidation;
using Sorcha.CitizenWallet.Abstractions.Models;

namespace Sorcha.CitizenWallet.Abstractions.Validators;

/// <summary>
/// Validates a citizen-initiated device label update.
/// </summary>
public sealed class DeviceLabelUpdateRequestValidator : AbstractValidator<DeviceLabelUpdateRequest>
{
    /// <summary>Initialise validation rules.</summary>
    public DeviceLabelUpdateRequestValidator()
    {
        RuleFor(x => x.Label)
            .NotEmpty()
            .MaximumLength(120);
    }
}
