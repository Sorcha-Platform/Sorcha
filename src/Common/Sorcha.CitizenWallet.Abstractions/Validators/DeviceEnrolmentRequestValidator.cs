// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentValidation;
using Sorcha.CitizenWallet.Abstractions.Models;

namespace Sorcha.CitizenWallet.Abstractions.Validators;

/// <summary>
/// Validates a device enrolment request. Device label and platform are required;
/// the embedded JWK is delegated to <see cref="EcP256PublicJwkValidator"/>.
/// </summary>
public sealed class DeviceEnrolmentRequestValidator : AbstractValidator<DeviceEnrolmentRequest>
{
    /// <summary>Initialise validation rules.</summary>
    public DeviceEnrolmentRequestValidator()
    {
        RuleFor(x => x.DeviceLabel)
            .NotEmpty()
            .MaximumLength(120);

        RuleFor(x => x.Platform)
            .NotEmpty()
            .MaximumLength(120);

        RuleFor(x => x.UserAgent)
            .MaximumLength(512);

        RuleFor(x => x.DevicePublicJwk)
            .NotNull()
            .SetValidator(new EcP256PublicJwkValidator());
    }
}
