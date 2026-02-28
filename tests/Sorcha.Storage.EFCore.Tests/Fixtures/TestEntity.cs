// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Storage.EFCore.Tests.Fixtures;

/// <summary>
/// Simple entity used for EFCoreRepository testing.
/// </summary>
public class TestEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
