// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentValidation;
using Sorcha.Wallet.Service.Models;

namespace Sorcha.Wallet.Service.Validators;

/// <summary>
/// Validates a <see cref="SetPendingApplicationRequest"/>. Label must be
/// non-empty after trim and at most 80 characters — plain text only (no HTML,
/// no claim content) is the data contract; length and non-emptiness are what
/// can be enforced here.
/// </summary>
public sealed class SetPendingApplicationRequestValidator : AbstractValidator<SetPendingApplicationRequest>
{
    /// <summary>Initialise validation rules.</summary>
    public SetPendingApplicationRequestValidator()
    {
        RuleFor(x => x.Label)
            .NotEmpty()
            .Must(label => !string.IsNullOrWhiteSpace(label))
                .WithMessage("'Label' must not be empty or whitespace.")
            .MaximumLength(80);
    }
}
