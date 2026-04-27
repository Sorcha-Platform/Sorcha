// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models.Requests;

/// <summary>
/// Request body for <c>PUT /api/passkey/credentials/{id}</c> — renames a passkey
/// credential's user-visible display name. The caller must own the credential.
/// </summary>
/// <param name="DisplayName">New display name. Required, 1-100 chars after trim.</param>
public sealed record PasskeyRenameRequest(
    [property: JsonPropertyName("display_name")] string DisplayName);
