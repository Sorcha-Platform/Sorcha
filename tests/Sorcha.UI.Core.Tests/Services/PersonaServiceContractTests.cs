// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using FluentAssertions;
using Sorcha.Tenant.Models.Persona;
using Sorcha.UI.Core.Services.Persona;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Reflection-backed contract guard for the persona service surface.
/// Protects the two forward-compatibility promises made in the feature design:
/// <list type="number">
///   <item>The A→C migration (server-side → client-side decryption) must not
///     require any consumer change. The read shape
///     (<see cref="PersonaReadModelV1"/> / <see cref="PersonaAttribute{T}"/>)
///     is the contract that locks this in.</item>
///   <item>The Power of Attorney follow-up must slot in as a value-space
///     change on <see cref="PersonaReadOptions.ActingAs"/>, not a signature
///     change. The <c>GetAsync</c> overload with
///     <see cref="PersonaReadOptions"/> is the contract that locks this in.</item>
/// </list>
/// Any change to these assertions is a signal that the public surface is
/// shifting in a way that breaks future features — require an explicit,
/// reviewed test update before making the change.
/// </summary>
public class PersonaServiceContractTests
{
    [Fact]
    public void IPersonaService_GetAsync_Accepts_PersonaReadOptions()
    {
        var method = typeof(IPersonaService).GetMethod(
            nameof(IPersonaService.GetAsync),
            BindingFlags.Public | BindingFlags.Instance);

        method.Should().NotBeNull("IPersonaService must expose a GetAsync method");

        var parameters = method!.GetParameters();
        parameters.Should().HaveCountGreaterThanOrEqualTo(1);

        var optionsParam = parameters.FirstOrDefault(p => p.ParameterType == typeof(PersonaReadOptions));
        optionsParam.Should().NotBeNull(
            "GetAsync must accept PersonaReadOptions so the delegation follow-up " +
            "can add values to ActingAs without a breaking change.");

        optionsParam!.HasDefaultValue.Should().BeTrue(
            "PersonaReadOptions must be optional so existing callers do not need " +
            "to construct one for the 'self' case.");
    }

    [Fact]
    public void PersonaReadOptions_Defaults_ActingAs_To_Self()
    {
        var options = new PersonaReadOptions();

        options.ActingAs.Should().Be("self",
            "v1 only permits 'self'; any other default would require a client " +
            "update when the delegation follow-up ships.");
    }

    [Fact]
    public void PersonaAttribute_Has_Value_Source_VerifiedBy_LastUpdated_Properties()
    {
        // Use a concrete closed generic to inspect property shape.
        var type = typeof(PersonaAttribute<string>);

        type.GetProperty("Value").Should().NotBeNull();
        type.GetProperty("Source").Should().NotBeNull();
        type.GetProperty("VerifiedBy").Should().NotBeNull();
        type.GetProperty("LastUpdated").Should().NotBeNull();

        type.GetProperty("Source")!.PropertyType.Should().Be(typeof(PersonaAttributeSource));
        type.GetProperty("VerifiedBy")!.PropertyType.Should().Be(typeof(string),
            "VerifiedBy is a DID string when the verified-credential follow-up " +
            "ships. v1 sets it to null but the shape must be stable.");
        type.GetProperty("LastUpdated")!.PropertyType.Should().Be(typeof(DateTimeOffset));
    }

    [Fact]
    public void PersonaAttributeSource_SelfAsserted_Is_Zero()
    {
        // Zero is the default for an uninitialised enum. Keeping SelfAsserted
        // at zero means existing code that materialises a PersonaAttribute
        // without explicitly setting Source still produces a valid v1 value.
        ((int)PersonaAttributeSource.SelfAsserted).Should().Be(0);
        ((int)PersonaAttributeSource.VerifiedCredential).Should().Be(1);
    }

    [Fact]
    public void IPersonaService_Has_Autofill_Toggle_Methods()
    {
        var type = typeof(IPersonaService);
        type.GetMethod(nameof(IPersonaService.GetAutofillEnabledAsync)).Should().NotBeNull();
        type.GetMethod(nameof(IPersonaService.SetAutofillEnabledAsync)).Should().NotBeNull();
        type.GetMethod(nameof(IPersonaService.InvalidateCache)).Should().NotBeNull();
    }

    [Fact]
    public void PersonaReadModelV1_Exposes_Default_And_All_Collections_For_MultiValue_Lists()
    {
        var type = typeof(PersonaReadModelV1);

        // Defaults are the autofill-targets for multi-value attributes.
        type.GetProperty("DefaultEmail").Should().NotBeNull();
        type.GetProperty("DefaultPhone").Should().NotBeNull();
        type.GetProperty("DefaultAddress").Should().NotBeNull();
        type.GetProperty("DefaultNationality").Should().NotBeNull();

        // All* is the profile-management view.
        type.GetProperty("AllEmails").Should().NotBeNull();
        type.GetProperty("AllPhones").Should().NotBeNull();
        type.GetProperty("AllAddresses").Should().NotBeNull();
        type.GetProperty("AllNationalities").Should().NotBeNull();
    }
}
