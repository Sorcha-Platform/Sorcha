// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Models.Identity;

/// <summary>
/// The account-wide identifier of a person — <c>PlatformUser.Id</c>, carried in the JWT as the
/// <c>platform_user_id</c> claim. It is what the durable inbox is addressed by.
/// </summary>
/// <remarks>
/// <para>
/// Issue #1709 (CLAUDE.md pattern 26). A person has ONE <see cref="PlatformUserId"/> and one
/// org-scoped <see cref="UserIdentityId"/> per organisation they belong to. Both used to be a bare
/// <see cref="Guid"/>, so passing one where the other belonged compiled, ran, and failed silently —
/// PR #1708 found five live instances. Typing the inbox boundary with this struct makes that
/// mistake a compile error.
/// </para>
/// <para>
/// There is deliberately <b>no</b> conversion operator in either direction. Construction from a
/// <see cref="Guid"/> is explicit (<c>new PlatformUserId(user.Id)</c>) so each conversion point is a
/// visible claim about which kind of id the value is; crossing back into EF or wire code is via
/// <see cref="Value"/>. An implicit conversion TO <see cref="Guid"/> was rejected because it would
/// let a <see cref="PlatformUserId"/> flow silently into any bare <c>Guid userIdentityId</c>
/// parameter outside the typed boundary. Serialises as a plain GUID string, so the wire is unchanged.
/// </para>
/// </remarks>
/// <param name="Value">The underlying <c>PlatformUser.Id</c>.</param>
[JsonConverter(typeof(PlatformUserIdJsonConverter))]
public readonly record struct PlatformUserId(Guid Value) : IFormattable
{
    /// <summary>Wraps a nullable GUID known to be a <c>PlatformUser.Id</c>; <c>null</c> stays <c>null</c>.</summary>
    /// <param name="value">A value known to be a <c>PlatformUser.Id</c>, or null.</param>
    public static PlatformUserId? FromNullable(Guid? value) =>
        value is { } v ? new PlatformUserId(v) : null;

    /// <summary>Returns the underlying GUID's string form, so log lines are unchanged.</summary>
    public override string ToString() => Value.ToString();

    /// <summary>
    /// Formats exactly as the underlying <see cref="Guid"/> (e.g. <c>:N</c>, <c>:D</c>), so an
    /// interpolated <c>{id:N}</c> — used in deterministic inbox idempotency keys — is unchanged.
    /// </summary>
    /// <param name="format">A <see cref="Guid"/> format specifier.</param>
    /// <param name="formatProvider">Ignored by <see cref="Guid"/>; accepted for the interface.</param>
    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);
}

/// <summary>Serialises <see cref="PlatformUserId"/> exactly as its underlying <see cref="Guid"/>.</summary>
public sealed class PlatformUserIdJsonConverter : JsonConverter<PlatformUserId>
{
    /// <inheritdoc />
    public override PlatformUserId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetGuid());

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, PlatformUserId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
