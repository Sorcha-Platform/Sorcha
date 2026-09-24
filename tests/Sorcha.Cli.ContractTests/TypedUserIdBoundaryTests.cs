// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;

using FluentAssertions;

using Sorcha.ServiceClients.Inbox;
using Sorcha.Tenant.Models.Identity;

namespace Sorcha.Cli.ContractTests;

/// <summary>
/// Ratchet for issue #1709 (CLAUDE.md pattern 26): at the inbox boundary a person id is TYPED —
/// <see cref="PlatformUserId"/> or <see cref="UserIdentityId"/> — never a bare <see cref="Guid"/>.
/// </summary>
/// <remarks>
/// <para>
/// PR #1708 found five live defects of one class: a <c>UserIdentity.Id</c> (JWT <c>sub</c>) passed
/// where a <c>PlatformUser.Id</c> (JWT <c>platform_user_id</c>) belonged. Both were bare GUIDs, so
/// the compiler could not help, and in three files the correct and incorrect usage sat side by side.
/// Every one of them crossed this boundary.
/// </para>
/// <para>
/// This lives here because this is the one test project that can see all three writer-owning
/// services (Tenant, Wallet, Blueprint) plus <c>Sorcha.ServiceClients.Http</c>. The boundary is
/// DISCOVERED by name, not listed, so a new inbox writer is covered the moment it exists — and the
/// discovery itself is pinned so a rename cannot make the ratchet pass vacuously.
/// </para>
/// </remarks>
public sealed class TypedUserIdBoundaryTests
{
    private static readonly Assembly[] BoundaryAssemblies =
    [
        typeof(Sorcha.Tenant.Service.Services.IInboxService).Assembly,
        typeof(Sorcha.Wallet.Service.Services.Implementation.IWalletInboxWriter).Assembly,
        typeof(Sorcha.Blueprint.Service.Services.Implementation.IBlueprintInboxWriter).Assembly,
        typeof(IPlatformInboxClient).Assembly,
    ];

    private static readonly string[] ExplicitBoundaryTypeNames =
    [
        "IPlatformInboxClient", "PlatformInboxClient",
        "IInboxService", "InboxService",
        "ISecurityChangeNotifier", "SecurityChangeNotifier",
    ];

    /// <summary>
    /// The eight inbox writers #1709 names. Pinned so a rename that drops one out of the
    /// "*InboxWriter" discovery fails here rather than silently shrinking the ratchet.
    /// </summary>
    private static readonly string[] RequiredWriterInterfaces =
    [
        "IBlueprintInboxWriter", "IEncryptionInboxWriter", "IPersonaInboxWriter",
        "ITenantMembershipInboxWriter", "ITenantSecurityInboxWriter", "ICitizenDeviceInboxWriter",
        "IWalletInboxWriter", "IWalletWorkflowInboxWriter",
    ];

    private static IReadOnlyList<Type> BoundaryTypes() =>
        BoundaryAssemblies
            .SelectMany(SafeGetTypes)
            .Where(t => t.IsPublic)
            .Where(t => t.Name.EndsWith("InboxWriter", StringComparison.Ordinal)
                        || ExplicitBoundaryTypeNames.Contains(t.Name))
            .Distinct()
            .ToList();

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    [Fact]
    public void Discovery_FindsEveryInboxWriterAndBoundaryType()
    {
        var names = BoundaryTypes().Select(t => t.Name).ToHashSet();

        names.Should().Contain(RequiredWriterInterfaces,
            "every inbox writer #1709 typed must stay inside the ratchet's discovery");
        names.Should().Contain(ExplicitBoundaryTypeNames);
    }

    [Fact]
    public void BoundaryMethods_NoPersonIdParameterIsABareGuid()
    {
        var offenders = new List<string>();

        foreach (var type in BoundaryTypes())
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                foreach (var parameter in method.GetParameters())
                {
                    var name = parameter.Name ?? string.Empty;
                    var isPersonId =
                        name.Contains("platformUserId", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("userIdentityId", StringComparison.OrdinalIgnoreCase);
                    if (!isPersonId)
                    {
                        continue;
                    }

                    var parameterType = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
                    if (parameterType == typeof(Guid))
                    {
                        offenders.Add($"{type.FullName}.{method.Name}({parameter.ParameterType.Name} {name})");
                    }
                }
            }
        }

        // Joined so the failure lists EVERY offender, not just the first.
        string.Join(Environment.NewLine, offenders).Should().BeEmpty(
            "a PlatformUser id and a UserIdentity id are both GUIDs; at the inbox boundary they must be "
            + "typed (PlatformUserId / UserIdentityId) so passing one where the other belongs is a "
            + "compile error — see CLAUDE.md pattern 26 / #1709");
    }

    [Fact]
    public void InboxWritePayloads_AreAddressedByATypedPlatformUserId()
    {
        // The two records every writer builds to address an entry. If either reverts to Guid, a
        // UserIdentity id can once again be placed in the inbox's address field unnoticed.
        typeof(InboxWritePayload).GetProperty(nameof(InboxWritePayload.PlatformUserId))!.PropertyType
            .Should().Be(typeof(PlatformUserId));
        typeof(Sorcha.Tenant.Service.Services.InboxWriteRequest)
            .GetProperty(nameof(Sorcha.Tenant.Service.Services.InboxWriteRequest.PlatformUserId))!.PropertyType
            .Should().Be(typeof(PlatformUserId));
    }

    [Fact]
    public void EncryptionWorkItem_UserId_IsATypedPlatformUserId()
    {
        // #1703: this field was filled from `sub` and silently lost the encryption-complete notice.
        typeof(Sorcha.Blueprint.Service.Models.EncryptionWorkItem)
            .GetProperty(nameof(Sorcha.Blueprint.Service.Models.EncryptionWorkItem.UserId))!.PropertyType
            .Should().Be(typeof(PlatformUserId?));
    }

    [Fact]
    public void TypedIds_HaveNoConversionOperatorFromGuid()
    {
        // An implicit (or explicit) operator from Guid would let a bare GUID of either kind flow
        // straight back in and defeat the whole point. Construction must be `new X(guid)`.
        foreach (var type in new[] { typeof(PlatformUserId), typeof(UserIdentityId) })
        {
            type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name is "op_Implicit" or "op_Explicit")
                .Should().BeEmpty($"{type.Name} must only be constructed explicitly");
        }
    }
}
