// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentValidation;
using Sorcha.CitizenWallet.Abstractions.Models;

namespace Sorcha.CitizenWallet.Abstractions.Validators;

/// <summary>
/// Validates a presentation-log report payload.
/// </summary>
public sealed class PresentationLogReportRequestValidator : AbstractValidator<PresentationLogReportRequest>
{
    /// <summary>Initialise validation rules.</summary>
    public PresentationLogReportRequestValidator()
    {
        RuleFor(x => x.Entries)
            .NotNull()
            .NotEmpty();

        RuleForEach(x => x.Entries).SetValidator(new PresentationLogEntryValidator());
    }
}

/// <summary>
/// Validates a single presentation log entry.
/// </summary>
public sealed class PresentationLogEntryValidator : AbstractValidator<PresentationLogEntry>
{
    /// <summary>Initialise validation rules.</summary>
    public PresentationLogEntryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.CredentialId).NotEmpty();
        RuleFor(x => x.PresentedAt).NotEqual(default(DateTimeOffset));
        RuleFor(x => x.VerifierLabel).MaximumLength(200);
        RuleFor(x => x.VerifierDid).MaximumLength(200);
    }
}
