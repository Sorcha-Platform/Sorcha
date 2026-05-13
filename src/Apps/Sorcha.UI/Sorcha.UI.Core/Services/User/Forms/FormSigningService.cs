// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Wallet;
using Sorcha.UI.Core.Services.Wallet;

namespace Sorcha.UI.Core.Services.Forms;

/// <summary>
/// Hashes and signs form data using the participant's wallet.
/// </summary>
public class FormSigningService : IFormSigningService
{
    private readonly IWalletApiService _walletService;
    private readonly ILogger<FormSigningService> _logger;

    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public FormSigningService(IWalletApiService walletService, ILogger<FormSigningService> logger)
    {
        _walletService = walletService;
        _logger = logger;
    }

    public string SerializeFormData(Dictionary<string, object?> data)
    {
        // Sort keys for deterministic serialization
        var sorted = new SortedDictionary<string, object?>(data);
        return JsonSerializer.Serialize(sorted, CanonicalOptions);
    }

    public string HashData(string canonicalJson)
    {
        var bytes = Encoding.UTF8.GetBytes(canonicalJson);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

    public async Task<string?> SignWithWallet(string walletAddress, Dictionary<string, object?> data, CancellationToken ct = default)
    {
        try
        {
            var canonicalJson = SerializeFormData(data);
            var hash = HashData(canonicalJson);

            var request = new SignTransactionRequest
            {
                TransactionData = hash,
                IsPreHashed = true
            };

            var response = await _walletService.SignDataAsync(walletAddress, request, ct);
            return response.Signature;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sign form data with wallet {WalletAddress}", walletAddress);
            return null;
        }
    }
}
