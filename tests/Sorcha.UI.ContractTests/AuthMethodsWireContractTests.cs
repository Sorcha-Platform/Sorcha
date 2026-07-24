// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Text.Json;

using FluentAssertions;

using Sorcha.ContractTests.Shared;
using Sorcha.Tenant.Models.Auth;

using ServerModels = Sorcha.Tenant.Service.Models;
using ServerServices = Sorcha.Tenant.Service.Services;
using UiModels = Sorcha.UI.Core.Models;

namespace Sorcha.UI.ContractTests;

/// <summary>
/// Pins the auth-methods / 2FA / step-up-challenge contract now that the Tenant Service and the
/// Blazor UI share one set of types (<c>Sorcha.Tenant.Models.Auth</c>) instead of hand-maintained
/// mirrors.
/// </summary>
/// <remarks>
/// <para>
/// Eleven type names were declared on both sides, and the copies had already drifted in ways
/// nothing could catch — the UI's <c>ChallengeMethod</c> stopped at <c>ReOAuth</c>, missing the
/// <c>EmailOtp</c> and <c>SmsOtp</c> members the server's ladder can select, and its
/// <c>ScopedOperation</c> omitted <c>LinkSocial</c> entirely. Both sides compiled, and both sides'
/// unit tests passed, because each was internally consistent.
/// </para>
/// <para>
/// Sharing the types makes divergence impossible, so this suite guards the two things sharing
/// cannot: that the duplicates do not quietly return, and that the wire-visible <i>names</i>
/// (enum members, JSON properties) are treated as the contract they are.
/// </para>
/// </remarks>
public sealed class AuthMethodsWireContractTests
{
    /// <summary>The contract types both sides must resolve to the same CLR type.</summary>
    private static readonly Type[] SharedContractTypes =
    [
        typeof(ChallengeMethod),
        typeof(ScopedOperation),
        typeof(AuthMethodKind),
        typeof(AuthAssuranceTier),
        typeof(ChallengeInitiateRequest),
        typeof(ChallengeInitiateResponse),
        typeof(ChallengeVerifyRequest),
        typeof(ChallengeVerifyResponse),
        typeof(AuthMethodsResponse),
        typeof(AuthMethodsPassword),
        typeof(AuthMethodsSocial),
        typeof(AuthMethodsPasskey),
        typeof(TotpSetupResponse),
        typeof(TotpCodeRequest),
        typeof(TotpStatusResponse),
    ];

    [Fact]
    public void SharedContractTypes_AllLiveInTheZeroDependencyLeaf()
    {
        // Anti-vacuity: if a type were moved back into a service or UI assembly, every other
        // assertion here would still pass while the drift risk returned.
        var leaf = typeof(TokenResponse).Assembly;

        SharedContractTypes.Should().OnlyContain(
            t => t.Assembly == leaf,
            "every auth wire contract must live in Sorcha.Tenant.Models so the Tenant Service, the "
            + "Blazor UI and the PWA can all reference the same declaration");
    }

    [Fact]
    public void NeitherSide_ReDeclaresASharedContractType()
    {
        // The failure mode this whole item exists to prevent: someone adds a local copy "just for
        // the client", the two drift, and nothing fails until a user hits the diverged path.
        var sharedNames = SharedContractTypes.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        Assembly[] mustNotDeclare =
        [
            typeof(UiModels.ChallengeVerifyResult).Assembly,   // Sorcha.UI.Components.User
            typeof(ServerServices.IAuthMethodService).Assembly, // Sorcha.Tenant.Service
        ];

        var offenders = mustNotDeclare
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => sharedNames.Contains(t.Name))
            .Select(t => $"{t.FullName} in {t.Assembly.GetName().Name}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "these names are shared contracts in Sorcha.Tenant.Models.Auth — a second declaration "
            + "is a mirror that will drift. Reference the shared type instead.");
    }

    [Theory]
    [InlineData(typeof(ChallengeMethod), "Totp,Password,Passkey,ReOAuth,EmailOtp,SmsOtp")]
    [InlineData(typeof(ScopedOperation), "RemoveAuthMethod,ChangePassword,SetPassword,RemovePassword,Disable2Fa,LinkSocial")]
    [InlineData(typeof(AuthMethodKind), "Password,Social,Passkey")]
    [InlineData(typeof(AuthAssuranceTier), "Basic,Strong,Strongest")]
    public void EnumMemberNames_AreThePinnedWireContract(Type enumType, string expectedCsv)
    {
        // These names cross the network as JSON strings. A rename is a breaking wire change that
        // the compiler will happily perform across both sides at once, so pin them here.
        Enum.GetNames(enumType).Should().BeEquivalentTo(
            expectedCsv.Split(','),
            $"{enumType.Name} member names are serialised verbatim; adding one is fine, renaming or "
            + "removing one breaks every deployed client");
    }

    [Fact]
    public void ChallengeMethod_CoversEveryLadderRungTheServerCanSelect()
    {
        // The concrete bug: the UI's copy had four members. The moment the server's ladder returned
        // EmailOtp or SmsOtp the client's deserialiser would throw on an unknown enum name and the
        // step-up dialog would fail outright — not degrade.
        // Every concrete verification channel the service ships must be expressible as a member of
        // the shared enum. Channels are named <Member>Channel (EmailOtpChannel -> EmailOtp), so the
        // mapping is checked by name — no instantiation, no DI graph.
        var channelMembers = typeof(ServerServices.IVerificationChannel).Assembly
            .GetTypes()
            .Where(t => typeof(ServerServices.IVerificationChannel).IsAssignableFrom(t)
                        && t is { IsAbstract: false, IsInterface: false })
            .Select(t => t.Name.Replace("Channel", string.Empty, StringComparison.Ordinal))
            .ToList();

        channelMembers.Should().NotBeEmpty("the server ships at least the email channel");
        channelMembers.Should().OnlyContain(
            name => Enum.IsDefined(typeof(ChallengeMethod), name),
            "a channel the server can select but the enum cannot name makes the client's "
            + "deserialiser throw on an unknown enum value — the exact defect the UI's four-member "
            + "copy of this enum was carrying");
    }

    [Fact]
    public void PasskeyStatus_StringCarriesExactlyTheServerEnumNames()
    {
        // AuthMethodsPasskey.Status is deliberately a string in the shared leaf: the Tenant Service's
        // CredentialStatus is EF-mapped with ~40 service-internal usages, so hoisting it here to
        // satisfy one response property would drag persistence into a zero-dependency leaf.
        //
        // That trade swaps a compile-time link for a string, so this test restores the guarantee:
        // the names the server can emit must stay exactly the set clients switch on.
        var serverNames = Enum.GetNames(typeof(ServerModels.CredentialStatus));

        serverNames.Should().BeEquivalentTo(
            ["Active", "Disabled", "Revoked"],
            "AuthMethodService.PasskeyStatusWireValue kebab-cases these names onto the wire; "
            + "renaming a member silently changes the value the UI compares against");

        typeof(AuthMethodsPasskey).GetProperty(nameof(AuthMethodsPasskey.Status))!
            .PropertyType.Should().Be<string>();
    }

    [Fact]
    public void EndpointsBindTheSharedTypes_NotALocalLookalike()
    {
        // Reflection over the service's own signatures: if someone reintroduces a server-local DTO
        // and rebinds the endpoint to it, the shared type stops being the contract and this fails.
        var aggregate = typeof(ServerServices.IAuthMethodService)
            .GetMethod(nameof(ServerServices.IAuthMethodService.GetAggregateAsync))!;

        aggregate.ReturnType.Should().Be(typeof(Task<AuthMethodsResponse?>));

        var floorCheck = typeof(ServerServices.IAuthMethodService)
            .GetMethod(nameof(ServerServices.IAuthMethodService.WouldRemovingLeaveZeroAsync))!;

        floorCheck.GetParameters().Select(p => p.ParameterType)
            .Should().Contain(typeof(AuthMethodKind));
    }

    [Fact]
    public void ClientAndServer_RoundTripTheAggregateThroughTheServersOwnOptions()
    {
        // End-to-end on the real serializer: the Tenant Service configures SorchaJson (camelCase
        // members, kebab-case string enums), while the assurance enums carry their own
        // JsonStringEnumConverter. Which one wins decides whether "Strong" or "strong" goes on the
        // wire — pin the answer instead of reasoning about converter precedence.
        var options = new JsonSerializerOptions();
        Sorcha.Serialization.SorchaJson.Configure(options);

        var original = new AuthMethodsResponse(
            Email: "ada@example.test",
            EmailVerified: true,
            Password: new AuthMethodsPassword(
                IsSet: true, LastChangedAt: null, CanRemove: true,
                AssuranceTier: AuthAssuranceTier.Strong, RequiredProofTier: AuthAssuranceTier.Basic),
            Socials: [],
            Passkeys:
            [
                new AuthMethodsPasskey(
                    Id: Guid.Empty, DisplayName: "iPhone", DeviceType: null, Status: "Active",
                    DisabledReason: null, CreatedAt: DateTimeOffset.UnixEpoch, LastUsedAt: null,
                    CanRemove: false, CanRename: true,
                    AssuranceTier: AuthAssuranceTier.Strongest,
                    RequiredProofTier: AuthAssuranceTier.Strongest),
            ],
            SmsAvailable: false);

        var json = JsonSerializer.Serialize(original, options);

        json.Should().Contain("\"emailVerified\":true");

        // Converter precedence, pinned because it is counter-intuitive and load-bearing:
        // System.Text.Json ranks the options.Converters collection ABOVE a type-level
        // [JsonConverter] attribute. AuthAssuranceTier carries such an attribute (default
        // PascalCase names), but SorchaJson registers a kebab-case JsonStringEnumConverter in
        // Converters — so kebab wins and "Strong" goes on the wire as "strong".
        json.Should().Contain("\"assuranceTier\":\"strong\"");
        json.Should().Contain("\"requiredProofTier\":\"basic\"");

        // Status is a string, so the enum policy does not touch it. AuthMethodService therefore
        // applies the same kebab policy explicitly, preserving the exact value the wire carried
        // when this property was typed as CredentialStatus.
        json.Should().Contain("\"status\":\"Active\"");

        JsonSerializer.Deserialize<AuthMethodsResponse>(json, options)
            .Should().BeEquivalentTo(original);
    }

    [Fact]
    public void ChallengeRequestShapes_SerialiseToTheNamesTheServerBinds()
    {
        var options = new JsonSerializerOptions();
        Sorcha.Serialization.SorchaJson.Configure(options);

        var initiate = JsonSerializer.Serialize(
            new ChallengeInitiateRequest(ScopedOperation.RemoveAuthMethod, ChallengeMethod.Totp, AuthMethodKind.Passkey),
            options);

        initiate.Should().Be(
            """{"scopedOperation":"remove-auth-method","preferredMethod":"totp","targetMethodKind":"passkey"}""",
            "the UI posts this body verbatim to /api/auth/challenge/initiate; enum members are "
            + "kebab-cased by SorchaJson's converter, which outranks the type-level attribute");

        WireShape.Of(typeof(ChallengeVerifyRequest)).Should().BeEquivalentTo(
            ["method", "scopedOperation", "proof", "targetMethodKind"]);
    }
}
