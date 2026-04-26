// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using FluentValidation.TestHelper;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.CitizenWallet.Abstractions.Validators;

namespace Sorcha.CitizenWallet.Abstractions.Tests;

public sealed class ValidatorsTests
{
    private static EcP256PublicJwk ValidJwk() => new()
    {
        Kty = "EC",
        Crv = "P-256",
        // 43-char base64url placeholders representing 32-byte coordinates
        X = "MKBCTNIcKUSDii11ySs3526iDZ8AiTo7Tu6KPAqv7D4",
        Y = "4Etl6SRW2YiLUrN5vfvVHuhp7x8PxltmWWlbbM4IFyM"
    };

    [Fact]
    public void EcP256PublicJwkValidator_ValidJwk_Passes()
    {
        var validator = new EcP256PublicJwkValidator();
        var result = validator.TestValidate(ValidJwk());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void EcP256PublicJwkValidator_WrongKty_Fails()
    {
        var validator = new EcP256PublicJwkValidator();
        var result = validator.TestValidate(ValidJwk() with { Kty = "RSA" });
        result.ShouldHaveValidationErrorFor(x => x.Kty);
    }

    [Fact]
    public void EcP256PublicJwkValidator_WrongCurve_Fails()
    {
        var validator = new EcP256PublicJwkValidator();
        var result = validator.TestValidate(ValidJwk() with { Crv = "P-384" });
        result.ShouldHaveValidationErrorFor(x => x.Crv);
    }

    [Fact]
    public void EcP256PublicJwkValidator_BadBase64UrlInX_Fails()
    {
        var validator = new EcP256PublicJwkValidator();
        var result = validator.TestValidate(ValidJwk() with { X = "this contains spaces!" });
        result.ShouldHaveValidationErrorFor(x => x.X);
    }

    [Fact]
    public void EcP256PublicJwkValidator_WrongLengthX_Fails()
    {
        var validator = new EcP256PublicJwkValidator();
        var result = validator.TestValidate(ValidJwk() with { X = "tooshort" });
        result.ShouldHaveValidationErrorFor(x => x.X);
    }

    [Fact]
    public void DeviceEnrolmentRequestValidator_ValidRequest_Passes()
    {
        var validator = new DeviceEnrolmentRequestValidator();
        var result = validator.TestValidate(new DeviceEnrolmentRequest
        {
            DeviceLabel = "Stuart's iPhone 16",
            DevicePublicJwk = ValidJwk(),
            Platform = "iOS 19 / Safari 19",
            UserAgent = "Mozilla/5.0 ..."
        });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void DeviceEnrolmentRequestValidator_EmptyLabel_Fails()
    {
        var validator = new DeviceEnrolmentRequestValidator();
        var result = validator.TestValidate(new DeviceEnrolmentRequest
        {
            DeviceLabel = "",
            DevicePublicJwk = ValidJwk(),
            Platform = "iOS"
        });
        result.ShouldHaveValidationErrorFor(x => x.DeviceLabel);
    }

    [Fact]
    public void DeviceEnrolmentRequestValidator_LabelTooLong_Fails()
    {
        var validator = new DeviceEnrolmentRequestValidator();
        var result = validator.TestValidate(new DeviceEnrolmentRequest
        {
            DeviceLabel = new string('x', 121),
            DevicePublicJwk = ValidJwk(),
            Platform = "iOS"
        });
        result.ShouldHaveValidationErrorFor(x => x.DeviceLabel);
    }

    [Fact]
    public void DeviceLabelUpdateRequestValidator_ValidRequest_Passes()
    {
        var validator = new DeviceLabelUpdateRequestValidator();
        var result = validator.TestValidate(new DeviceLabelUpdateRequest { Label = "kitchen iPad" });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void DelegationRenewalRequestValidator_EmptyDeviceId_Fails()
    {
        var validator = new DelegationRenewalRequestValidator();
        var result = validator.TestValidate(new DelegationRenewalRequest { DeviceId = Guid.Empty });
        result.ShouldHaveValidationErrorFor(x => x.DeviceId);
    }

    [Fact]
    public void PresentationLogReportRequestValidator_EmptyEntries_Fails()
    {
        var validator = new PresentationLogReportRequestValidator();
        var result = validator.TestValidate(new PresentationLogReportRequest { Entries = [] });
        result.ShouldHaveValidationErrorFor(x => x.Entries);
    }

    [Fact]
    public void PresentationLogEntryValidator_ValidEntry_Passes()
    {
        var validator = new PresentationLogEntryValidator();
        var result = validator.TestValidate(new PresentationLogEntry
        {
            Id = Guid.NewGuid(),
            CredentialId = Guid.NewGuid(),
            PresentedAt = DateTimeOffset.UtcNow,
            Outcome = PresentationLogOutcome.Presented,
            DisclosedClaims = ["over_18", "photo"],
            VerifierLabel = "Acme Bar"
        });
        result.IsValid.Should().BeTrue();
    }
}
