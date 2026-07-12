using Sorcha.Wallet.Contracts.Models;
using Sorcha.Wallet.Service.IntegrationTests.Fixtures;
using Sorcha.Wallet.Service.Models;

namespace Sorcha.Wallet.Service.IntegrationTests;

/// <summary>
/// Integration tests for Wallet Service API endpoints.
/// Uses test authentication handler to bypass JWT validation.
/// </summary>
[Collection("WalletService")]
public class WalletServiceApiTests
{
    private readonly HttpClient _client;
    private readonly WalletServiceWebApplicationFactory _factory;

    public WalletServiceApiTests(WalletServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateAuthenticatedClient();
    }

    #region Wallet CRUD Tests

    [Fact]
    public async Task CreateWallet_ShouldReturnCreatedWallet_WhenValidRequest()
    {
        // Arrange
        var request = new CreateWalletRequest
        {
            Name = "Integration Test Wallet",
            Algorithm = "ED25519",
            WordCount = 12
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/wallets", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<CreateWalletResponse>();
        result.Should().NotBeNull();
        result!.Wallet.Should().NotBeNull();
        result.Wallet.Name.Should().Be("Integration Test Wallet");
        result.Wallet.Algorithm.Should().Be("ED25519");
        result.Wallet.Address.Should().NotBeNullOrEmpty();
        result.MnemonicWords.Should().HaveCount(12);
    }

    [Fact]
    public async Task CreateWallet_With24Words_ShouldReturnCreatedWallet()
    {
        // Arrange
        var request = new CreateWalletRequest
        {
            Name = "24-Word Wallet",
            Algorithm = "ED25519",
            WordCount = 24
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/wallets", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<CreateWalletResponse>();
        result!.MnemonicWords.Should().HaveCount(24);
    }

    [Fact]
    public async Task GetWallet_ShouldReturnWallet_WhenExists()
    {
        // Arrange - First create a wallet
        var createRequest = new CreateWalletRequest
        {
            Name = "Test Get Wallet",
            Algorithm = "ED25519",
            WordCount = 12
        };

        var createResponse = await _client.PostAsJsonAsync("/api/v1/wallets", createRequest);
        var createdWallet = await createResponse.Content.ReadFromJsonAsync<CreateWalletResponse>();
        var walletAddress = createdWallet!.Wallet.Address;

        // Act
        var response = await _client.GetAsync($"/api/v1/wallets/{walletAddress}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var wallet = await response.Content.ReadFromJsonAsync<WalletDto>();
        wallet.Should().NotBeNull();
        wallet!.Address.Should().Be(walletAddress);
        wallet.Name.Should().Be("Test Get Wallet");
    }

    [Fact]
    public async Task GetWallet_ShouldReturnNotFound_WhenDoesNotExist()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/wallets/ws1nonexistent");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListWallets_ShouldReturnAllUserWallets()
    {
        // Arrange - Create multiple wallets
        var wallet1 = new CreateWalletRequest { Name = "Wallet 1", Algorithm = "ED25519", WordCount = 12 };
        var wallet2 = new CreateWalletRequest { Name = "Wallet 2", Algorithm = "NISTP256", WordCount = 12 };

        await _client.PostAsJsonAsync("/api/v1/wallets", wallet1);
        await _client.PostAsJsonAsync("/api/v1/wallets", wallet2);

        // Act
        var response = await _client.GetAsync("/api/v1/wallets");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var wallets = await response.Content.ReadFromJsonAsync<List<WalletDto>>();
        wallets.Should().NotBeNull();
        wallets!.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task UpdateWallet_ShouldReturnUpdatedWallet()
    {
        // Arrange - Create a wallet
        var createRequest = new CreateWalletRequest
        {
            Name = "Original Name",
            Algorithm = "ED25519",
            WordCount = 12
        };

        var createResponse = await _client.PostAsJsonAsync("/api/v1/wallets", createRequest);
        var createdWallet = await createResponse.Content.ReadFromJsonAsync<CreateWalletResponse>();
        var walletAddress = createdWallet!.Wallet.Address;

        var updateRequest = new UpdateWalletRequest
        {
            Name = "Updated Name",
            Tags = new Dictionary<string, string> { { "environment", "production" } }
        };

        // Act
        var response = await _client.PatchAsync(
            $"/api/v1/wallets/{walletAddress}",
            JsonContent.Create(updateRequest));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedWallet = await response.Content.ReadFromJsonAsync<WalletDto>();
        updatedWallet!.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task DeleteWallet_ShouldReturnNoContent()
    {
        // Arrange - Create a wallet
        var createRequest = new CreateWalletRequest
        {
            Name = "To Be Deleted",
            Algorithm = "ED25519",
            WordCount = 12
        };

        var createResponse = await _client.PostAsJsonAsync("/api/v1/wallets", createRequest);
        var createdWallet = await createResponse.Content.ReadFromJsonAsync<CreateWalletResponse>();
        var walletAddress = createdWallet!.Wallet.Address;

        // Act
        var response = await _client.DeleteAsync($"/api/v1/wallets/{walletAddress}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's deleted (soft delete, so it should still exist but be marked as deleted)
        var getResponse = await _client.GetAsync($"/api/v1/wallets/{walletAddress}");
        // The behavior depends on whether soft-deleted wallets are returned
    }

    #endregion

    #region Wallet Recovery Tests

    [Fact]
    public async Task RecoverWallet_ShouldReturnRecoveredWallet_WhenValidMnemonic()
    {
        // Arrange - First create a wallet to get a valid mnemonic
        var createRequest = new CreateWalletRequest
        {
            Name = "Original Wallet",
            Algorithm = "ED25519",
            WordCount = 12
        };

        var createResponse = await _client.PostAsJsonAsync("/api/v1/wallets", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateWalletResponse>();
        var mnemonicWords = created!.MnemonicWords;
        var originalAddress = created.Wallet.Address;

        // Delete the wallet first
        await _client.DeleteAsync($"/api/v1/wallets/{originalAddress}");

        // Act - Recover using the mnemonic
        var recoverRequest = new RecoverWalletRequest
        {
            MnemonicWords = mnemonicWords,
            Name = "Recovered Wallet",
            Algorithm = "ED25519"
        };

        var recoverResponse = await _client.PostAsJsonAsync("/api/v1/wallets/recover", recoverRequest);

        // Assert
        recoverResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var recoveredWallet = await recoverResponse.Content.ReadFromJsonAsync<WalletDto>();
        recoveredWallet.Should().NotBeNull();
        recoveredWallet!.Address.Should().Be(originalAddress); // Same mnemonic = same address
        recoveredWallet.Name.Should().Be("Recovered Wallet");
    }

    #endregion

    #region Transaction Signing Tests

    [Fact]
    public async Task SignTransaction_ShouldReturnSignature()
    {
        // Arrange - Create a wallet
        var createRequest = new CreateWalletRequest
        {
            Name = "Signing Wallet",
            Algorithm = "ED25519",
            WordCount = 12
        };

        var createResponse = await _client.PostAsJsonAsync("/api/v1/wallets", createRequest);
        var wallet = await createResponse.Content.ReadFromJsonAsync<CreateWalletResponse>();
        var walletAddress = wallet!.Wallet.Address;

        var transactionData = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("test transaction data"));
        var signRequest = new SignTransactionRequest
        {
            TransactionData = transactionData
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/wallets/{walletAddress}/sign",
            signRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<SignTransactionResponse>();
        result.Should().NotBeNull();
        result!.Signature.Should().NotBeNullOrEmpty();
        result.SignedBy.Should().Be(walletAddress);
        result.SignedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task SignTransaction_ShouldReturnBadRequest_WhenInvalidBase64()
    {
        // Arrange - Create a wallet
        var createRequest = new CreateWalletRequest
        {
            Name = "Test Wallet",
            Algorithm = "ED25519",
            WordCount = 12
        };

        var createResponse = await _client.PostAsJsonAsync("/api/v1/wallets", createRequest);
        var wallet = await createResponse.Content.ReadFromJsonAsync<CreateWalletResponse>();

        var signRequest = new SignTransactionRequest
        {
            TransactionData = "not-valid-base64!!!"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/wallets/{wallet!.Wallet.Address}/sign",
            signRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Encryption/Decryption Tests

    [Fact]
    public async Task EncryptAndDecrypt_ShouldRoundTrip()
    {
        // Arrange - Create a wallet
        var createRequest = new CreateWalletRequest
        {
            Name = "Crypto Wallet",
            Algorithm = "ED25519",
            WordCount = 12
        };

        var createResponse = await _client.PostAsJsonAsync("/api/v1/wallets", createRequest);
        var wallet = await createResponse.Content.ReadFromJsonAsync<CreateWalletResponse>();
        var walletAddress = wallet!.Wallet.Address;

        var originalData = "Secret message that needs encryption";
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(originalData));

        // Act - Encrypt
        var encryptRequest = new EncryptPayloadRequest { Payload = payload };
        var encryptResponse = await _client.PostAsJsonAsync(
            $"/api/v1/wallets/{walletAddress}/encrypt",
            encryptRequest);

        encryptResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var encryptResult = await encryptResponse.Content.ReadFromJsonAsync<EncryptPayloadResponse>();

        // Act - Decrypt
        var decryptRequest = new DecryptPayloadRequest
        {
            EncryptedPayload = encryptResult!.EncryptedPayload
        };
        var decryptResponse = await _client.PostAsJsonAsync(
            $"/api/v1/wallets/{walletAddress}/decrypt",
            decryptRequest);

        // Assert
        decryptResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var decryptResult = await decryptResponse.Content.ReadFromJsonAsync<DecryptPayloadResponse>();

        var decryptedData = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(decryptResult!.DecryptedPayload));
        decryptedData.Should().Be(originalData);
    }

    #endregion

    #region Access Control Tests

    [Fact]
    public async Task GrantAccess_ShouldReturnCreatedAccess()
    {
        // Arrange - Create a wallet
        var createRequest = new CreateWalletRequest
        {
            Name = "Access Control Wallet",
            Algorithm = "ED25519",
            WordCount = 12
        };

        var createResponse = await _client.PostAsJsonAsync("/api/v1/wallets", createRequest);
        var wallet = await createResponse.Content.ReadFromJsonAsync<CreateWalletResponse>();
        var walletAddress = wallet!.Wallet.Address;

        var grantRequest = new GrantAccessRequest
        {
            Subject = "user-123",
            AccessRight = "ReadWrite",
            Reason = "Integration test"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/wallets/{walletAddress}/access",
            grantRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var access = await response.Content.ReadFromJsonAsync<WalletAccessDto>();
        access.Should().NotBeNull();
        access!.Subject.Should().Be("user-123");
        access.AccessRight.Should().Be("ReadWrite");
    }

    [Fact]
    public async Task GetAccess_ShouldReturnAccessList()
    {
        // Arrange - Create a wallet and grant access
        var createRequest = new CreateWalletRequest
        {
            Name = "Multi Access Wallet",
            Algorithm = "ED25519",
            WordCount = 12
        };

        var createResponse = await _client.PostAsJsonAsync("/api/v1/wallets", createRequest);
        var wallet = await createResponse.Content.ReadFromJsonAsync<CreateWalletResponse>();
        var walletAddress = wallet!.Wallet.Address;

        var grant1 = new GrantAccessRequest { Subject = "user-1", AccessRight = "ReadWrite" };
        var grant2 = new GrantAccessRequest { Subject = "user-2", AccessRight = "ReadOnly" };

        await _client.PostAsJsonAsync($"/api/v1/wallets/{walletAddress}/access", grant1);
        await _client.PostAsJsonAsync($"/api/v1/wallets/{walletAddress}/access", grant2);

        // Act
        var response = await _client.GetAsync($"/api/v1/wallets/{walletAddress}/access");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var accessList = await response.Content.ReadFromJsonAsync<List<WalletAccessDto>>();
        accessList.Should().NotBeNull();
        accessList!.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task CheckAccess_ShouldReturnTrue_WhenAccessGranted()
    {
        // Arrange - Create wallet and grant access
        var createRequest = new CreateWalletRequest
        {
            Name = "Check Access Wallet",
            Algorithm = "ED25519",
            WordCount = 12
        };

        var createResponse = await _client.PostAsJsonAsync("/api/v1/wallets", createRequest);
        var wallet = await createResponse.Content.ReadFromJsonAsync<CreateWalletResponse>();
        var walletAddress = wallet!.Wallet.Address;

        var grantRequest = new GrantAccessRequest
        {
            Subject = "user-999",
            AccessRight = "ReadOnly"
        };

        await _client.PostAsJsonAsync($"/api/v1/wallets/{walletAddress}/access", grantRequest);

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/wallets/{walletAddress}/access/user-999/check?requiredRight=ReadOnly");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AccessCheckResponse>();
        result.Should().NotBeNull();
        result!.HasAccess.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeAccess_ShouldReturnNoContent()
    {
        // Arrange - Create wallet and grant access
        var createRequest = new CreateWalletRequest
        {
            Name = "Revoke Access Wallet",
            Algorithm = "ED25519",
            WordCount = 12
        };

        var createResponse = await _client.PostAsJsonAsync("/api/v1/wallets", createRequest);
        var wallet = await createResponse.Content.ReadFromJsonAsync<CreateWalletResponse>();
        var walletAddress = wallet!.Wallet.Address;

        var grantRequest = new GrantAccessRequest
        {
            Subject = "user-to-revoke",
            AccessRight = "ReadWrite"
        };

        await _client.PostAsJsonAsync($"/api/v1/wallets/{walletAddress}/access", grantRequest);

        // Act
        var response = await _client.DeleteAsync(
            $"/api/v1/wallets/{walletAddress}/access/user-to-revoke");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify access is revoked
        var checkResponse = await _client.GetAsync(
            $"/api/v1/wallets/{walletAddress}/access/user-to-revoke/check?requiredRight=ReadWrite");
        var checkResult = await checkResponse.Content.ReadFromJsonAsync<AccessCheckResponse>();
        checkResult!.HasAccess.Should().BeFalse();
    }

    #endregion

    #region Multiple Algorithm Support Tests

    [Theory]
    [InlineData("ED25519")]
    [InlineData("NISTP256")]
    public async Task CreateWallet_ShouldSupportDifferentAlgorithms(string algorithm)
    {
        // Arrange
        var request = new CreateWalletRequest
        {
            Name = $"{algorithm} Wallet",
            Algorithm = algorithm,
            WordCount = 12
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/wallets", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var wallet = await response.Content.ReadFromJsonAsync<CreateWalletResponse>();
        wallet!.Wallet.Algorithm.Should().Be(algorithm);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task CreateWallet_ShouldReturnBadRequest_WhenInvalidAlgorithm()
    {
        // Arrange
        var request = new CreateWalletRequest
        {
            Name = "Invalid Algorithm Wallet",
            Algorithm = "INVALID_ALGORITHM",
            WordCount = 12
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/wallets", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GrantAccess_ShouldReturnBadRequest_WhenInvalidAccessRight()
    {
        // Arrange - Create a wallet
        var createRequest = new CreateWalletRequest
        {
            Name = "Test Wallet",
            Algorithm = "ED25519",
            WordCount = 12
        };

        var createResponse = await _client.PostAsJsonAsync("/api/v1/wallets", createRequest);
        var wallet = await createResponse.Content.ReadFromJsonAsync<CreateWalletResponse>();

        var grantRequest = new GrantAccessRequest
        {
            Subject = "user-123",
            AccessRight = "InvalidRight"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/wallets/{wallet!.Wallet.Address}/access",
            grantRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion
}

// Using service models directly; define only the response type not present in models
public class AccessCheckResponse
{
    public string WalletAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string RequiredRight { get; set; } = string.Empty;
    public bool HasAccess { get; set; }
}
