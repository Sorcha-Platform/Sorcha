// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Wallet.Service.Models;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;
using WalletAccessEntity = Sorcha.Wallet.Core.Domain.Entities.WalletAccess;
using WalletAddressEntity = Sorcha.Wallet.Core.Domain.Entities.WalletAddress;

namespace Sorcha.Wallet.Service.Mappers;

/// <summary>
/// Maps between domain entities and DTOs
/// </summary>
public static class WalletMapper
{
    /// <summary>
    /// Maps Wallet entity to WalletDto
    /// </summary>
    public static WalletDto ToDto(this WalletEntity wallet)
    {
        return new WalletDto
        {
            Address = wallet.Address,
            Name = wallet.Name,
            PublicKey = wallet.PublicKey!,
            Algorithm = wallet.Algorithm,
            Status = wallet.Status.ToString(),
            Owner = wallet.Owner,
            Tenant = wallet.Tenant,
            CreatedAt = wallet.CreatedAt,
            UpdatedAt = wallet.UpdatedAt,
            SigningMode = wallet.SigningMode.ToString(),
            KmsKeyId = wallet.KmsKeyId,
            Metadata = new Dictionary<string, string>(wallet.Metadata)
        };
    }

    /// <summary>
    /// Maps WalletAccess entity to WalletAccessDto
    /// </summary>
    public static WalletAccessDto ToDto(this WalletAccessEntity access)
    {
        return new WalletAccessDto
        {
            Id = access.Id,
            Subject = access.Subject,
            AccessRight = access.AccessRight.ToString(),
            GrantedBy = access.GrantedBy,
            GrantedAt = access.GrantedAt,
            ExpiresAt = access.ExpiresAt,
            IsActive = access.IsActive,
            Reason = access.Reason
        };
    }

    /// <summary>
    /// Maps WalletAddress entity to WalletAddressDto
    /// </summary>
    public static WalletAddressDto ToDto(this WalletAddressEntity address)
    {
        return new WalletAddressDto
        {
            Id = address.Id,
            ParentWalletAddress = address.ParentWalletAddress,
            Address = address.Address,
            PublicKey = address.PublicKey,
            DerivationPath = address.DerivationPath,
            Index = address.Index,
            Account = address.Account,
            IsChange = address.IsChange,
            Label = address.Label,
            Notes = address.Notes,
            Tags = address.Tags,
            IsUsed = address.IsUsed,
            CreatedAt = address.CreatedAt,
            FirstUsedAt = address.FirstUsedAt,
            LastUsedAt = address.LastUsedAt,
            Metadata = new Dictionary<string, string>(address.Metadata)
        };
    }
}
