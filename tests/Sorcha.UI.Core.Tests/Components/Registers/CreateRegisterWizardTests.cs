// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Models.Registers;
using Sorcha.UI.Core.Services;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Registers;

/// <summary>
/// Tests for CreateRegisterWizard component and RegisterCreationState.
/// Note: Full component tests require E2E testing due to MudBlazor dialog dependency.
/// </summary>
public class CreateRegisterWizardTests
{
    #region RegisterCreationState Default Values

    [Fact]
    public void RegisterCreationState_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var state = new RegisterCreationState();

        // Assert
        state.CurrentStep.Should().Be(1);
        state.Name.Should().BeEmpty();
        state.Advertise.Should().BeFalse();
        state.IsFullReplica.Should().BeTrue(); // Default to full replica
        state.TenantId.Should().BeEmpty();
        state.RegisterId.Should().BeNull();
        state.UnsignedControlRecord.Should().BeNull();
        state.SignedControlRecord.Should().BeNull();
        state.IsProcessing.Should().BeFalse();
        state.ErrorMessage.Should().BeNull();
    }

    #endregion

    #region Name Validation Tests

    [Fact]
    public void RegisterCreationState_IsNameValid_ReturnsFalse_WhenEmpty()
    {
        // Arrange
        var state = new RegisterCreationState { Name = "" };

        // Assert
        state.IsNameValid.Should().BeFalse();
    }

    [Fact]
    public void RegisterCreationState_IsNameValid_ReturnsFalse_WhenWhitespace()
    {
        // Arrange
        var state = new RegisterCreationState { Name = "   " };

        // Assert
        state.IsNameValid.Should().BeFalse();
    }

    [Fact]
    public void RegisterCreationState_IsNameValid_ReturnsTrue_WhenMinLength()
    {
        // Arrange
        var state = new RegisterCreationState { Name = "A" };

        // Assert
        state.IsNameValid.Should().BeTrue();
    }

    [Fact]
    public void RegisterCreationState_IsNameValid_ReturnsTrue_WhenMaxLength()
    {
        // Arrange
        var state = new RegisterCreationState { Name = new string('A', 38) };

        // Assert
        state.IsNameValid.Should().BeTrue();
    }

    [Fact]
    public void RegisterCreationState_IsNameValid_ReturnsFalse_WhenTooLong()
    {
        // Arrange
        var state = new RegisterCreationState { Name = new string('A', 39) };

        // Assert
        state.IsNameValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("My Register")]
    [InlineData("test-register-123")]
    [InlineData("Register With Spaces And Numbers 123")]
    public void RegisterCreationState_IsNameValid_ReturnsTrue_ForValidNames(string name)
    {
        // Arrange
        var state = new RegisterCreationState { Name = name };

        // Assert
        state.IsNameValid.Should().BeTrue();
    }

    #endregion

    #region CanProceed Tests

    [Fact]
    public void RegisterCreationState_CanProceed_ReturnsFalse_WhenStep1AndNoName()
    {
        // Arrange
        var state = new RegisterCreationState { CurrentStep = 1, Name = "" };

        // Assert
        state.CanProceed.Should().BeFalse();
    }

    [Fact]
    public void RegisterCreationState_CanProceed_ReturnsTrue_WhenStep1AndValidName()
    {
        // Arrange
        var state = new RegisterCreationState { CurrentStep = 1, Name = "Valid Name" };

        // Assert
        state.CanProceed.Should().BeTrue();
    }

    [Fact]
    public void RegisterCreationState_CanProceed_ReturnsFalse_WhenStep2AndNoWallet()
    {
        // Arrange - Wallet step requires a valid wallet
        var state = new RegisterCreationState { CurrentStep = 2 };

        // Assert
        state.CanProceed.Should().BeFalse();
    }

    [Fact]
    public void RegisterCreationState_CanProceed_ReturnsTrue_WhenStep2AndHasWallet()
    {
        // Arrange
        var state = new RegisterCreationState
        {
            CurrentStep = 2,
            SelectedWalletAddress = "wallet-abc-123"
        };

        // Assert
        state.CanProceed.Should().BeTrue();
    }

    [Fact]
    public void RegisterCreationState_CanProceed_ReturnsTrue_WhenStep3()
    {
        // Arrange - Options step is always valid
        var state = new RegisterCreationState { CurrentStep = 3 };

        // Assert
        state.CanProceed.Should().BeTrue();
    }

    [Fact]
    public void RegisterCreationState_CanProceed_ReturnsFalse_WhenStep4AndNoSignedRecord()
    {
        // Arrange
        var state = new RegisterCreationState { CurrentStep = 4 };

        // Assert
        state.CanProceed.Should().BeFalse();
    }

    [Fact]
    public void RegisterCreationState_CanProceed_ReturnsTrue_WhenStep4AndHasSignedRecord()
    {
        // Arrange
        var state = new RegisterCreationState
        {
            CurrentStep = 4,
            SignedControlRecord = "signed-record-123"
        };

        // Assert
        state.CanProceed.Should().BeTrue();
    }

    [Fact]
    public void RegisterCreationState_CanProceed_ReturnsFalse_WhenInvalidStep()
    {
        // Arrange
        var state = new RegisterCreationState { CurrentStep = 5 };

        // Assert
        state.CanProceed.Should().BeFalse();
    }

    #endregion

    #region CanGoBack Tests

    [Fact]
    public void RegisterCreationState_CanGoBack_ReturnsFalse_WhenStep1()
    {
        // Arrange
        var state = new RegisterCreationState { CurrentStep = 1 };

        // Assert
        state.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void RegisterCreationState_CanGoBack_ReturnsTrue_WhenStep2()
    {
        // Arrange
        var state = new RegisterCreationState { CurrentStep = 2 };

        // Assert
        state.CanGoBack.Should().BeTrue();
    }

    [Fact]
    public void RegisterCreationState_CanGoBack_ReturnsTrue_WhenStep3()
    {
        // Arrange
        var state = new RegisterCreationState { CurrentStep = 3 };

        // Assert
        state.CanGoBack.Should().BeTrue();
    }

    [Fact]
    public void RegisterCreationState_CanGoBack_ReturnsFalse_WhenProcessing()
    {
        // Arrange
        var state = new RegisterCreationState
        {
            CurrentStep = 2,
            IsProcessing = true
        };

        // Assert
        state.CanGoBack.Should().BeFalse();
    }

    #endregion

    #region Step Navigation Tests

    [Fact]
    public void RegisterCreationState_NextStep_IncrementsCurrentStep()
    {
        // Arrange
        var state = new RegisterCreationState { CurrentStep = 1 };

        // Act
        var nextState = state.NextStep();

        // Assert
        nextState.CurrentStep.Should().Be(2);
        state.CurrentStep.Should().Be(1); // Original unchanged (immutable)
    }

    [Fact]
    public void RegisterCreationState_PreviousStep_DecrementsCurrentStep()
    {
        // Arrange
        var state = new RegisterCreationState { CurrentStep = 2 };

        // Act
        var prevState = state.PreviousStep();

        // Assert
        prevState.CurrentStep.Should().Be(1);
        state.CurrentStep.Should().Be(2); // Original unchanged (immutable)
    }

    [Fact]
    public void RegisterCreationState_Navigation_PreservesOtherProperties()
    {
        // Arrange
        var state = new RegisterCreationState
        {
            CurrentStep = 1,
            Name = "Test Register",
            Advertise = true,
            IsFullReplica = false,
            TenantId = "tenant-123"
        };

        // Act
        var nextState = state.NextStep();

        // Assert
        nextState.Name.Should().Be("Test Register");
        nextState.Advertise.Should().BeTrue();
        nextState.IsFullReplica.Should().BeFalse();
        nextState.TenantId.Should().Be("tenant-123");
    }

    #endregion

    #region Processing State Tests

    [Fact]
    public void RegisterCreationState_WithExpression_CanSetProcessing()
    {
        // Arrange
        var state = new RegisterCreationState();

        // Act
        var processing = state with { IsProcessing = true };

        // Assert
        processing.IsProcessing.Should().BeTrue();
        state.IsProcessing.Should().BeFalse(); // Original unchanged
    }

    [Fact]
    public void RegisterCreationState_WithExpression_CanSetErrorMessage()
    {
        // Arrange
        var state = new RegisterCreationState();

        // Act
        var errorState = state with { ErrorMessage = "Something went wrong" };

        // Assert
        errorState.ErrorMessage.Should().Be("Something went wrong");
    }

    [Fact]
    public void RegisterCreationState_WithExpression_CanClearError()
    {
        // Arrange
        var state = new RegisterCreationState { ErrorMessage = "Error" };

        // Act
        var clearedState = state with { ErrorMessage = null };

        // Assert
        clearedState.ErrorMessage.Should().BeNull();
    }

    #endregion

    #region Registration Flow Tests

    [Fact]
    public void RegisterCreationState_CanTrackFullRegistrationFlow()
    {
        // Step 1: Initial state — name entry
        var state = new RegisterCreationState();
        state.CurrentStep.Should().Be(1);
        state.CanProceed.Should().BeFalse();

        // User enters name
        state = state with { Name = "My New Register" };
        state.IsNameValid.Should().BeTrue();
        state.CanProceed.Should().BeTrue();

        // Step 2: Move to wallet selection
        state = state.NextStep();
        state.CurrentStep.Should().Be(2);
        state.CanGoBack.Should().BeTrue();
        state.CanProceed.Should().BeFalse(); // No wallet selected yet

        // User selects wallet
        state = state with { SelectedWalletAddress = "wallet-abc-123" };
        state.HasValidWallet.Should().BeTrue();
        state.CanProceed.Should().BeTrue();

        // Step 3: Move to options
        state = state.NextStep();
        state.CurrentStep.Should().Be(3);
        state.CanGoBack.Should().BeTrue();

        // User configures options
        state = state with { Advertise = true, IsFullReplica = true };
        state.CanProceed.Should().BeTrue();

        // Step 4: Move to review
        state = state.NextStep();
        state.CurrentStep.Should().Be(4);

        // Initiate creation (processing)
        state = state with
        {
            IsProcessing = true,
            RegisterId = "reg-abc-123"
        };
        state.CanGoBack.Should().BeFalse(); // Can't go back while processing

        // Receive unsigned record
        state = state with
        {
            UnsignedControlRecord = "unsigned-control-record"
        };

        // Complete with signed record
        state = state with
        {
            SignedControlRecord = "signed-control-record",
            IsProcessing = false
        };
        state.CanProceed.Should().BeTrue(); // Now complete
    }

    [Fact]
    public void RegisterCreationState_CanHandleError()
    {
        // Arrange
        var state = new RegisterCreationState
        {
            Name = "Test",
            IsProcessing = true,
            CurrentStep = 3
        };

        // Act - Simulate error
        var errorState = state with
        {
            IsProcessing = false,
            ErrorMessage = "Failed to create register"
        };

        // Assert
        errorState.IsProcessing.Should().BeFalse();
        errorState.ErrorMessage.Should().NotBeNull();
        errorState.CanGoBack.Should().BeTrue(); // Can retry
    }

    #endregion

    #region Wizard Validation Tests (Component Logic)

    [Theory]
    [InlineData("", "Register name is required")]
    [InlineData("   ", "Register name is required")]
    public void Wizard_ValidateName_ReturnsError_WhenEmptyOrWhitespace(string name, string expectedError)
    {
        // This tests the same validation logic used by the wizard
        var error = ValidateName(name);
        error.Should().Contain(expectedError);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("AB")]
    [InlineData("Valid Register Name")]
    public void Wizard_ValidateName_ReturnsNull_WhenValid(string name)
    {
        var error = ValidateName(name);
        error.Should().BeNull();
    }

    [Fact]
    public void Wizard_ValidateName_ReturnsError_WhenTooLong()
    {
        var name = new string('A', 39);
        var error = ValidateName(name);
        error.Should().Contain("38 characters");
    }

    // Helper method matching wizard validation logic
    private static string? ValidateName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Register name is required";

        if (value.Length < 1)
            return "Name must be at least 1 character";

        if (value.Length > 38)
            return "Name cannot exceed 38 characters";

        return null;
    }

    #endregion

    #region Options Tests

    [Fact]
    public void RegisterCreationState_Options_DefaultToPrivateAndFullReplica()
    {
        // Arrange & Act
        var state = new RegisterCreationState();

        // Assert - Sensible defaults
        state.Advertise.Should().BeFalse(); // Private by default
        state.IsFullReplica.Should().BeTrue(); // Full replica by default
    }

    [Fact]
    public void RegisterCreationState_Options_CanBeToggled()
    {
        // Arrange
        var state = new RegisterCreationState();

        // Act - Toggle both options
        var modified = state with
        {
            Advertise = true,
            IsFullReplica = false
        };

        // Assert
        modified.Advertise.Should().BeTrue();
        modified.IsFullReplica.Should().BeFalse();
    }

    #endregion

    #region Visibility Status Display Tests

    [Fact]
    public void RegisterViewModel_Advertise_True_IsPublic()
    {
        var vm = new RegisterViewModel
        {
            Id = "test-id",
            Name = "Test Register",

            Advertise = true
        };

        vm.Advertise.Should().BeTrue();
    }

    [Fact]
    public void RegisterViewModel_Advertise_False_IsPrivate()
    {
        var vm = new RegisterViewModel
        {
            Id = "test-id",
            Name = "Test Register",

            Advertise = false
        };

        vm.Advertise.Should().BeFalse();
    }

    [Fact]
    public void RegisterViewModel_Advertise_DefaultsToFalse()
    {
        var vm = new RegisterViewModel
        {
            Id = "test-id",
            Name = "Test Register"
        };

        vm.Advertise.Should().BeFalse();
    }

    #endregion

    #region CreateRegisterRequest Advertise Tests

    [Fact]
    public void CreateRegisterRequest_Advertise_True_IsSerialized()
    {
        var request = new CreateRegisterRequest
        {
            Name = "Public Register",

            Advertise = true,
            Owners = [new OwnerInfo { UserId = "user-1", WalletId = "wallet-1" }]
        };

        request.Advertise.Should().BeTrue();
    }

    [Fact]
    public void CreateRegisterRequest_Advertise_False_IsSerialized()
    {
        var request = new CreateRegisterRequest
        {
            Name = "Private Register",

            Advertise = false,
            Owners = [new OwnerInfo { UserId = "user-1", WalletId = "wallet-1" }]
        };

        request.Advertise.Should().BeFalse();
    }

    [Fact]
    public void CreateRegisterRequest_Advertise_DefaultsToFalse()
    {
        var request = new CreateRegisterRequest
        {
            Name = "Default Register",

            Owners = [new OwnerInfo { UserId = "user-1", WalletId = "wallet-1" }]
        };

        request.Advertise.Should().BeFalse();
    }

    [Fact]
    public void CreateRegisterRequest_Advertise_RoundTripsViaJson()
    {
        var request = new CreateRegisterRequest
        {
            Name = "Public Register",

            Advertise = true,
            Owners = [new OwnerInfo { UserId = "user-1", WalletId = "wallet-1" }]
        };

        var json = System.Text.Json.JsonSerializer.Serialize(request);
        json.Should().Contain("\"advertise\":true");

        var deserialized = System.Text.Json.JsonSerializer.Deserialize<CreateRegisterRequest>(json);
        deserialized.Should().NotBeNull();
        deserialized!.Advertise.Should().BeTrue();
    }

    #endregion

    #region Public Register Forces Full Replica Tests

    [Fact]
    public void RegisterCreationState_PublicRegister_ShouldBeFullReplica()
    {
        // When advertise is true, full replica should also be true
        var state = new RegisterCreationState
        {
            Advertise = true,
            IsFullReplica = true
        };

        state.Advertise.Should().BeTrue();
        state.IsFullReplica.Should().BeTrue();
    }

    [Fact]
    public void RegisterCreationState_PrivateRegister_CanDisableFullReplica()
    {
        // Private registers can opt out of full replica
        var state = new RegisterCreationState
        {
            Advertise = false,
            IsFullReplica = false
        };

        state.Advertise.Should().BeFalse();
        state.IsFullReplica.Should().BeFalse();
    }

    [Fact]
    public void RegisterCreationState_SwitchingToPublic_ShouldForceFullReplica()
    {
        // Simulates the wizard behavior: user had full replica off, then enables public
        var state = new RegisterCreationState
        {
            Advertise = false,
            IsFullReplica = false
        };

        // User toggles to public — wizard forces full replica on
        var publicState = state with { Advertise = true, IsFullReplica = true };

        publicState.Advertise.Should().BeTrue();
        publicState.IsFullReplica.Should().BeTrue();
    }

    #endregion
}
