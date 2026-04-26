// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentValidation;
using Sorcha.CitizenWallet.Abstractions.Models;

namespace Sorcha.CitizenWallet.Abstractions.Validators;

/// <summary>
/// Validates an <see cref="EcP256PublicJwk"/>: enforces curve identity, base64url
/// shape on coordinates, and rejects any private-half fields if a future caller
/// accidentally posts one.
/// </summary>
public sealed class EcP256PublicJwkValidator : AbstractValidator<EcP256PublicJwk>
{
    private const string Base64UrlPattern = "^[A-Za-z0-9_-]+$";

    /// <summary>Initialise validation rules.</summary>
    public EcP256PublicJwkValidator()
    {
        RuleFor(x => x.Kty).Equal("EC").WithMessage("kty must be 'EC'");
        RuleFor(x => x.Crv).Equal("P-256").WithMessage("crv must be 'P-256'");

        RuleFor(x => x.X)
            .NotEmpty()
            .Matches(Base64UrlPattern).WithMessage("x must be base64url-encoded")
            .Length(43).WithMessage("x for P-256 must decode to 32 bytes (43 base64url chars)");

        RuleFor(x => x.Y)
            .NotEmpty()
            .Matches(Base64UrlPattern).WithMessage("y must be base64url-encoded")
            .Length(43).WithMessage("y for P-256 must decode to 32 bytes (43 base64url chars)");
    }
}
