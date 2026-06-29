// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using FluentAssertions;
using Sorcha.UI.Core.Services.Credentials;
using Sorcha.Wallet.Pwa.Pages;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Pages;

/// <summary>
/// Feature 174 spot-check: the PWA <c>Verify</c> page does NOT inject
/// <see cref="IHaipOfferService"/>. It uses the local doorstep
/// (<c>IVerifierEngine</c> via <c>VerifyFlow</c>), and is therefore not
/// on the broken BFF polling path that this feature fixes.
/// </summary>
public sealed class VerifyPageTests
{
    [Fact]
    public void VerifyPage_DoesNotDeclareIHaipOfferServiceInjection()
    {
        var verifyPageType = typeof(Verify);

        // Blazor compiles @inject directives into [Inject]-annotated auto-properties.
        var injectProps = verifyPageType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p => p.GetCustomAttribute<Microsoft.AspNetCore.Components.InjectAttribute>() != null)
            .Select(p => p.PropertyType)
            .ToList();

        injectProps.Should().NotContain(
            typeof(IHaipOfferService),
            "Verify.razor uses IVerifierEngine (local doorstep) and must never inject IHaipOfferService");
    }
}
