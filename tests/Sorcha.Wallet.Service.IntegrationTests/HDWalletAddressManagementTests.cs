using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sorcha.Wallet.Contracts.Models;
using Sorcha.Wallet.Service.IntegrationTests.Fixtures;
using Sorcha.Wallet.Service.Models;
using Xunit;

namespace Sorcha.Wallet.Service.IntegrationTests;

/// <summary>
/// Integration tests demonstrating HD wallet address management with client-side derivation.
/// This shows the complete flow from wallet creation through address lifecycle management.
/// Uses test authentication handler to bypass JWT validation.
/// </summary>
[Collection("WalletService")]
public class HDWalletAddressManagementTests
{
    private readonly HttpClient _client;

    public HDWalletAddressManagementTests(WalletServiceWebApplicationFactory factory)
    {
        _client = factory.CreateAuthenticatedClient();
    }

    [Fact]
    public async Task CompleteHDWalletWorkflow_ShouldDemonstrateAllFeatures()
    {
        // ============================================================
        // STEP 1: Create a wallet
        // ============================================================
        var createRequest = new CreateWalletRequest
        {
            Name = "My HD Wallet",
            Algorithm = "ED25519",
            WordCount = 12
        };

        var createResponse = await _client.PostAsJsonAsync("/api/v1/wallets", createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateWalletResponse>();
        createResult.Should().NotBeNull();
        var walletAddress = createResult!.Wallet.Address;

        // In real scenario, client would use this mnemonic to derive addresses
        var mnemonic = createResult.MnemonicWords;
        mnemonic.Should().HaveCount(12);

        // ============================================================
        // STEP 2: Register first derived address (client-side derivation)
        // ============================================================
        // In a real application, the client would:
        // 1. Use the mnemonic to derive a key at path m/44'/0'/0'/0/1
        // 2. Generate the public key and address
        // 3. Send only the public info to the server

        var address1Request = new RegisterDerivedAddressRequest
        {
            DerivedPublicKey = Convert.ToBase64String(new byte[32]), // Simulated
            DerivedAddress = $"ws1q{Guid.NewGuid().ToString("N")}", // Simulated Bech32 address
            DerivationPath = "m/44'/0'/0'/0/1",
            Label = "Payment Address 1",
            Notes = "For receiving customer payments",
            Tags = "payment,customer"
        };

        var registerResponse1 = await _client.PostAsJsonAsync(
            $"/api/v1/wallets/{walletAddress}/addresses",
            address1Request);

        registerResponse1.StatusCode.Should().Be(HttpStatusCode.Created);
        var address1 = await registerResponse1.Content.ReadFromJsonAsync<WalletAddressDto>();
        address1.Should().NotBeNull();
        address1!.DerivationPath.Should().Be("m/44'/0'/0'/0/1");
        address1.Label.Should().Be("Payment Address 1");
        address1.IsChange.Should().BeFalse(); // Receive address

        // ============================================================
        // STEP 3: Register a change address
        // ============================================================
        var changeAddressRequest = new RegisterDerivedAddressRequest
        {
            DerivedPublicKey = Convert.ToBase64String(new byte[32]),
            DerivedAddress = $"ws1q{Guid.NewGuid().ToString("N")}",
            DerivationPath = "m/44'/0'/0'/1/0", // Change path
            Label = "Change Address 1"
        };

        var registerResponse2 = await _client.PostAsJsonAsync(
            $"/api/v1/wallets/{walletAddress}/addresses",
            changeAddressRequest);

        registerResponse2.StatusCode.Should().Be(HttpStatusCode.Created);
        var address2 = await registerResponse2.Content.ReadFromJsonAsync<WalletAddressDto>();
        address2!.IsChange.Should().BeTrue(); // Change address

        // ============================================================
        // STEP 4: Register address in different account
        // ============================================================
        var account1AddressRequest = new RegisterDerivedAddressRequest
        {
            DerivedPublicKey = Convert.ToBase64String(new byte[32]),
            DerivedAddress = $"ws1q{Guid.NewGuid().ToString("N")}",
            DerivationPath = "m/44'/0'/1'/0/0", // Account 1
            Label = "Savings Account"
        };

        var registerResponse3 = await _client.PostAsJsonAsync(
            $"/api/v1/wallets/{walletAddress}/addresses",
            account1AddressRequest);

        registerResponse3.StatusCode.Should().Be(HttpStatusCode.Created);
        var address3 = await registerResponse3.Content.ReadFromJsonAsync<WalletAddressDto>();
        address3!.Account.Should().Be(1);

        // ============================================================
        // STEP 5: List all addresses
        // ============================================================
        var listResponse = await _client.GetAsync($"/api/v1/wallets/{walletAddress}/addresses");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var addressList = await listResponse.Content.ReadFromJsonAsync<AddressListResponse>();
        addressList.Should().NotBeNull();
        addressList!.Addresses.Should().HaveCount(3);
        addressList.TotalCount.Should().Be(3);

        // ============================================================
        // STEP 6: Filter addresses by type (receive only)
        // ============================================================
        var receiveOnlyResponse = await _client.GetAsync(
            $"/api/v1/wallets/{walletAddress}/addresses?type=receive");

        var receiveList = await receiveOnlyResponse.Content.ReadFromJsonAsync<AddressListResponse>();
        receiveList!.Addresses.Should().HaveCount(2);
        receiveList.Addresses.Should().AllSatisfy(a => a.IsChange.Should().BeFalse());

        // ============================================================
        // STEP 7: Filter addresses by account
        // ============================================================
        var account0Response = await _client.GetAsync(
            $"/api/v1/wallets/{walletAddress}/addresses?account=0");

        var account0List = await account0Response.Content.ReadFromJsonAsync<AddressListResponse>();
        account0List!.Addresses.Should().HaveCount(2);

        // ============================================================
        // STEP 8: Get specific address by ID
        // ============================================================
        var getAddressResponse = await _client.GetAsync(
            $"/api/v1/wallets/{walletAddress}/addresses/{address1.Id}");

        getAddressResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var retrievedAddress = await getAddressResponse.Content.ReadFromJsonAsync<WalletAddressDto>();
        retrievedAddress!.Id.Should().Be(address1.Id);
        retrievedAddress.Label.Should().Be("Payment Address 1");

        // ============================================================
        // STEP 9: Update address metadata
        // ============================================================
        var updateRequest = new UpdateAddressRequest
        {
            Label = "Updated Payment Address",
            Notes = "Updated notes - used for merchant payments",
            Tags = "payment,merchant,updated",
            Metadata = new Dictionary<string, string>
            {
                ["purpose"] = "merchant-payments",
                ["priority"] = "high"
            }
        };

        var updateResponse = await _client.PatchAsJsonAsync(
            $"/api/v1/wallets/{walletAddress}/addresses/{address1.Id}",
            updateRequest);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedAddress = await updateResponse.Content.ReadFromJsonAsync<WalletAddressDto>();
        updatedAddress!.Label.Should().Be("Updated Payment Address");
        updatedAddress.Notes.Should().Contain("merchant payments");
        updatedAddress.Metadata.Should().ContainKey("purpose");

        // ============================================================
        // STEP 10: Mark address as used
        // ============================================================
        var markUsedResponse = await _client.PostAsync(
            $"/api/v1/wallets/{walletAddress}/addresses/{address1.Id}/mark-used",
            null);

        markUsedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var usedAddress = await markUsedResponse.Content.ReadFromJsonAsync<WalletAddressDto>();
        usedAddress!.IsUsed.Should().BeTrue();
        usedAddress.FirstUsedAt.Should().NotBeNull();
        usedAddress.LastUsedAt.Should().NotBeNull();

        // ============================================================
        // STEP 11: Filter by used status
        // ============================================================
        var usedOnlyResponse = await _client.GetAsync(
            $"/api/v1/wallets/{walletAddress}/addresses?used=true");

        var usedList = await usedOnlyResponse.Content.ReadFromJsonAsync<AddressListResponse>();
        usedList!.Addresses.Should().HaveCount(1);
        usedList.Addresses[0].IsUsed.Should().BeTrue();

        var unusedResponse = await _client.GetAsync(
            $"/api/v1/wallets/{walletAddress}/addresses?used=false");

        var unusedList = await unusedResponse.Content.ReadFromJsonAsync<AddressListResponse>();
        unusedList!.Addresses.Should().HaveCount(2);

        // ============================================================
        // STEP 12: List accounts with statistics
        // ============================================================
        var accountsResponse = await _client.GetAsync($"/api/v1/wallets/{walletAddress}/accounts");
        accountsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var accountsContent = await accountsResponse.Content.ReadAsStringAsync();
        accountsContent.Should().NotBeNullOrEmpty();

        // ============================================================
        // STEP 13: Check gap limit status
        // ============================================================
        var gapStatusResponse = await _client.GetAsync($"/api/v1/wallets/{walletAddress}/gap-status");
        gapStatusResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var gapStatus = await gapStatusResponse.Content.ReadFromJsonAsync<GapStatusResponse>();
        gapStatus.Should().NotBeNull();
        gapStatus!.IsCompliant.Should().BeTrue(); // Only 2-3 unused addresses
        gapStatus.Accounts.Should().NotBeEmpty();

        // All accounts should be compliant (far below 20 gap limit)
        gapStatus.Accounts.Should().AllSatisfy(a =>
            a.UnusedCount.Should().BeLessThan(20));
    }

    [Fact]
    public async Task GapLimit_ShouldEnforceMaximum20UnusedAddresses()
    {
        // Create wallet
        var createRequest = new CreateWalletRequest
        {
            Name = "Gap Limit Test Wallet",
            Algorithm = "ED25519",
            WordCount = 12
        };

        var createResponse = await _client.PostAsJsonAsync("/api/v1/wallets", createRequest);
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateWalletResponse>();
        var walletAddress = createResult!.Wallet.Address;

        // Register 20 unused addresses (maximum allowed)
        for (int i = 1; i <= 20; i++)
        {
            var addressRequest = new RegisterDerivedAddressRequest
            {
                DerivedPublicKey = Convert.ToBase64String(new byte[32]),
                DerivedAddress = $"ws1q{Guid.NewGuid().ToString("N")}",
                DerivationPath = $"m/44'/0'/0'/0/{i}",
                Label = $"Address {i}"
            };

            var response = await _client.PostAsJsonAsync(
                $"/api/v1/wallets/{walletAddress}/addresses",
                addressRequest);

            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // Try to register 21st unused address - should fail
        var failRequest = new RegisterDerivedAddressRequest
        {
            DerivedPublicKey = Convert.ToBase64String(new byte[32]),
            DerivedAddress = $"ws1q{Guid.NewGuid().ToString("N")}",
            DerivationPath = "m/44'/0'/0'/0/21",
            Label = "Should Fail"
        };

        var failResponse = await _client.PostAsJsonAsync(
            $"/api/v1/wallets/{walletAddress}/addresses",
            failRequest);

        failResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errorContent = await failResponse.Content.ReadAsStringAsync();
        errorContent.Should().Contain("Gap limit exceeded");
        errorContent.Should().Contain("20");

        // Verify gap status shows warning
        var gapStatusResponse = await _client.GetAsync($"/api/v1/wallets/{walletAddress}/gap-status");
        var gapStatus = await gapStatusResponse.Content.ReadFromJsonAsync<GapStatusResponse>();

        gapStatus!.IsCompliant.Should().BeFalse(); // At limit
        gapStatus.Accounts.Should().Contain(a => a.UnusedCount == 20);
    }

    [Fact]
    public async Task ChangeAddresses_ShouldBeSeparateFromReceiveAddresses()
    {
        // Create wallet
        var createRequest = new CreateWalletRequest
        {
            Name = "Change Address Test",
            Algorithm = "ED25519",
            WordCount = 12
        };

        var createResponse = await _client.PostAsJsonAsync("/api/v1/wallets", createRequest);
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateWalletResponse>();
        var walletAddress = createResult!.Wallet.Address;

        // Register receive addresses (change = 0)
        for (int i = 0; i < 5; i++)
        {
            var receiveRequest = new RegisterDerivedAddressRequest
            {
                DerivedPublicKey = Convert.ToBase64String(new byte[32]),
                DerivedAddress = $"ws1q{Guid.NewGuid().ToString("N")}",
                DerivationPath = $"m/44'/0'/0'/0/{i}",
                Label = $"Receive {i}"
            };
            await _client.PostAsJsonAsync($"/api/v1/wallets/{walletAddress}/addresses", receiveRequest);
        }

        // Register change addresses (change = 1)
        for (int i = 0; i < 3; i++)
        {
            var changeRequest = new RegisterDerivedAddressRequest
            {
                DerivedPublicKey = Convert.ToBase64String(new byte[32]),
                DerivedAddress = $"ws1q{Guid.NewGuid().ToString("N")}",
                DerivationPath = $"m/44'/0'/0'/1/{i}",
                Label = $"Change {i}"
            };
            await _client.PostAsJsonAsync($"/api/v1/wallets/{walletAddress}/addresses", changeRequest);
        }

        // Verify separate filtering
        var receiveResponse = await _client.GetAsync($"/api/v1/wallets/{walletAddress}/addresses?type=receive");
        var receiveList = await receiveResponse.Content.ReadFromJsonAsync<AddressListResponse>();
        receiveList!.Addresses.Should().HaveCount(5);

        var changeResponse = await _client.GetAsync($"/api/v1/wallets/{walletAddress}/addresses?type=change");
        var changeList = await changeResponse.Content.ReadFromJsonAsync<AddressListResponse>();
        changeList!.Addresses.Should().HaveCount(3);

        // Verify gap limits are tracked separately
        var gapStatusResponse = await _client.GetAsync($"/api/v1/wallets/{walletAddress}/gap-status");
        var gapStatus = await gapStatusResponse.Content.ReadFromJsonAsync<GapStatusResponse>();

        gapStatus!.Accounts.Should().Contain(a => a.AddressType == "receive" && a.UnusedCount == 5);
        gapStatus.Accounts.Should().Contain(a => a.AddressType == "change" && a.UnusedCount == 3);
    }

    [Fact]
    public async Task MultipleAccounts_ShouldBeTrackedSeparately()
    {
        // Create wallet
        var createRequest = new CreateWalletRequest
        {
            Name = "Multi-Account Wallet",
            Algorithm = "ED25519",
            WordCount = 12
        };

        var createResponse = await _client.PostAsJsonAsync("/api/v1/wallets", createRequest);
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateWalletResponse>();
        var walletAddress = createResult!.Wallet.Address;

        // Add addresses to account 0
        for (int i = 0; i < 3; i++)
        {
            var request = new RegisterDerivedAddressRequest
            {
                DerivedPublicKey = Convert.ToBase64String(new byte[32]),
                DerivedAddress = $"ws1q{Guid.NewGuid().ToString("N")}",
                DerivationPath = $"m/44'/0'/0'/0/{i}",
                Label = $"Account 0 - Address {i}"
            };
            await _client.PostAsJsonAsync($"/api/v1/wallets/{walletAddress}/addresses", request);
        }

        // Add addresses to account 1
        for (int i = 0; i < 2; i++)
        {
            var request = new RegisterDerivedAddressRequest
            {
                DerivedPublicKey = Convert.ToBase64String(new byte[32]),
                DerivedAddress = $"ws1q{Guid.NewGuid().ToString("N")}",
                DerivationPath = $"m/44'/0'/1'/0/{i}",
                Label = $"Account 1 - Address {i}"
            };
            await _client.PostAsJsonAsync($"/api/v1/wallets/{walletAddress}/addresses", request);
        }

        // Verify account filtering
        var account0Response = await _client.GetAsync($"/api/v1/wallets/{walletAddress}/addresses?account=0");
        var account0List = await account0Response.Content.ReadFromJsonAsync<AddressListResponse>();
        account0List!.Addresses.Should().HaveCount(3);

        var account1Response = await _client.GetAsync($"/api/v1/wallets/{walletAddress}/addresses?account=1");
        var account1List = await account1Response.Content.ReadFromJsonAsync<AddressListResponse>();
        account1List!.Addresses.Should().HaveCount(2);

        // Verify accounts endpoint shows both
        var accountsResponse = await _client.GetAsync($"/api/v1/wallets/{walletAddress}/accounts");
        var accountsContent = await accountsResponse.Content.ReadAsStringAsync();
        accountsContent.Should().Contain("\"account\":0");
        accountsContent.Should().Contain("\"account\":1");
    }
}
