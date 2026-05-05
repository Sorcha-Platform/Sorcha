// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Service.Hubs;

/// <summary>
/// Group-name builder for <see cref="RegisterHub"/>. All RegisterHub group strings
/// MUST be constructed via these helpers — no inline interpolation in service code.
/// </summary>
/// <remarks>
/// Per Feature 118 spec FR-013 — FR-015. Existing inline <c>$"register:{registerId}"</c>
/// callers in <c>RegisterHub.cs</c> and emit sites are migrated to this builder by
/// the Phase 7 / US5 sweep. New code MUST use this builder.
/// </remarks>
public static class RegisterHubGroups
{
    /// <summary>Per-register group. Hosts every register-domain event scoped to that register.</summary>
    public static string Register(Guid registerId) => $"register:{registerId:N}";

    /// <summary>
    /// String overload preserving the existing inline format used by
    /// <c>RegisterHub.SubscribeToRegister</c>, which receives the register id
    /// as a string from the client. Identical to the GUID overload after parsing.
    /// </summary>
    /// <remarks>
    /// Existing call sites pass <c>{Guid:N}</c> already; the format is consistent.
    /// </remarks>
    public static string Register(string registerId) => $"register:{registerId}";
}
