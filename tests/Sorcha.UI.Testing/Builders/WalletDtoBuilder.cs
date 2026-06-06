// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Wallet;

namespace Sorcha.UI.Testing.Builders;

/// <summary>
/// Fluent builder for <see cref="WalletDto"/> test fixtures. Supplies sensible
/// defaults for every required member so a test only states the fields it cares
/// about (e.g. <c>new WalletDtoBuilder().WithName("Treasury").Build()</c>).
/// </summary>
public sealed class WalletDtoBuilder
{
    private string _address = "0x0000000000000000000000000000000000000001";
    private string _name = "Test Wallet";
    private string _publicKey = "00";
    private string _algorithm = "ED25519";
    private string _status = "Active";
    private string _owner = "00000000-0000-0000-0000-000000000001";
    private string _tenant = "test-tenant";
    private DateTime _createdAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public WalletDtoBuilder WithAddress(string address) { _address = address; return this; }
    public WalletDtoBuilder WithName(string name) { _name = name; return this; }
    public WalletDtoBuilder WithAlgorithm(string algorithm) { _algorithm = algorithm; return this; }
    public WalletDtoBuilder WithStatus(string status) { _status = status; return this; }
    public WalletDtoBuilder WithOwner(string owner) { _owner = owner; return this; }
    public WalletDtoBuilder WithCreatedAt(DateTime createdAt) { _createdAt = createdAt; return this; }

    public WalletDto Build() => new()
    {
        Address = _address,
        Name = _name,
        PublicKey = _publicKey,
        Algorithm = _algorithm,
        Status = _status,
        Owner = _owner,
        Tenant = _tenant,
        CreatedAt = _createdAt,
        UpdatedAt = _createdAt,
    };
}
