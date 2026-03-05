// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Admin;

public enum ExpirationPreset { ThirtyDays, NinetyDays, OneYear, NoExpiry }

public record ServicePrincipalViewModel
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("serviceName")] public string ServiceName { get; init; } = string.Empty;
    [JsonPropertyName("clientId")] public string ClientId { get; init; } = string.Empty;
    [JsonPropertyName("scopes")] public string[] Scopes { get; init; } = [];
    [JsonPropertyName("status")] public string Status { get; init; } = "Active";
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("lastUsedAt")] public DateTimeOffset? LastUsedAt { get; init; }
    [JsonPropertyName("expiresAt")] public DateTimeOffset? ExpiresAt { get; init; }

    public bool IsNearExpiration =>
        ExpiresAt.HasValue && ExpiresAt.Value - DateTimeOffset.UtcNow < TimeSpan.FromDays(7);
}

public record CreateServicePrincipalRequest
{
    [JsonPropertyName("serviceName")] public string ServiceName { get; set; } = string.Empty;
    [JsonPropertyName("scopes")] public string[] Scopes { get; set; } = [];
    [JsonPropertyName("expirationDuration")] public ExpirationPreset ExpirationDuration { get; set; } = ExpirationPreset.NinetyDays;
}

public record ServicePrincipalSecretViewModel
{
    [JsonPropertyName("clientId")] public string ClientId { get; init; } = string.Empty;
    [JsonPropertyName("clientSecret")] public string ClientSecret { get; init; } = string.Empty;
    [JsonPropertyName("warning")] public string Warning { get; init; } = "This secret will not be shown again. Copy it now.";
}

public record ServicePrincipalListResult
{
    [JsonPropertyName("items")] public List<ServicePrincipalViewModel> Items { get; init; } = [];
    [JsonPropertyName("totalCount")] public int TotalCount { get; init; }
    [JsonPropertyName("includesInactive")] public bool IncludesInactive { get; init; }
}
