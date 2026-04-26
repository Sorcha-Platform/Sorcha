// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentValidation;
using Sorcha.CitizenWallet.Abstractions.Models;

namespace Sorcha.CitizenWallet.Abstractions.Validators;

/// <summary>
/// Validates a delegation renewal request.
/// </summary>
public sealed class DelegationRenewalRequestValidator : AbstractValidator<DelegationRenewalRequest>
{
    /// <summary>Initialise validation rules.</summary>
    public DelegationRenewalRequestValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
    }
}
