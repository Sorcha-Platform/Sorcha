// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Models.Identity;

/// <summary>
/// The org-scoped identifier of a person's membership — <c>UserIdentity.Id</c>, carried in the JWT
/// as the <c>sub</c> claim. A person has one per organisation; it is NOT what the inbox is keyed on.
/// </summary>
/// <remarks>
/// Issue #1709 (CLAUDE.md pattern 26). The counterpart of <see cref="PlatformUserId"/>; the pair
/// exists so that passing one where the other belongs is a compile error. To address the inbox,
/// resolve a <see cref="UserIdentityId"/> to its <see cref="PlatformUserId"/> (Tenant's
/// <c>/api/internal/users/by-identity/{id}</c>) — never reinterpret it. No conversion operators, by
/// design: construct explicitly, read back via <see cref="Value"/>. Serialises as a plain GUID string.
/// </remarks>
/// <param name="Value">The underlying <c>UserIdentity.Id</c>.</param>
[JsonConverter(typeof(UserIdentityIdJsonConverter))]
public readonly record struct UserIdentityId(Guid Value) : IFormattable
{
    /// <summary>Wraps a nullable GUID known to be a <c>UserIdentity.Id</c>; <c>null</c> stays <c>null</c>.</summary>
    /// <param name="value">A value known to be a <c>UserIdentity.Id</c>, or null.</param>
    public static UserIdentityId? FromNullable(Guid? value) =>
        value is { } v ? new UserIdentityId(v) : null;

    /// <summary>Returns the underlying GUID's string form, so log lines are unchanged.</summary>
    public override string ToString() => Value.ToString();

    /// <summary>
    /// Formats exactly as the underlying <see cref="Guid"/> (e.g. <c>:N</c>, <c>:D</c>), so an
    /// interpolated <c>{id:D}</c> in a URL or log line is unchanged.
    /// </summary>
    /// <param name="format">A <see cref="Guid"/> format specifier.</param>
    /// <param name="formatProvider">Ignored by <see cref="Guid"/>; accepted for the interface.</param>
    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);
}

/// <summary>Serialises <see cref="UserIdentityId"/> exactly as its underlying <see cref="Guid"/>.</summary>
public sealed class UserIdentityIdJsonConverter : JsonConverter<UserIdentityId>
{
    /// <inheritdoc />
    public override UserIdentityId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetGuid());

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, UserIdentityId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
