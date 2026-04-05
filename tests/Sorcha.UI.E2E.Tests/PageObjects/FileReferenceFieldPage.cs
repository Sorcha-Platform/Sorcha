// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.PageObjects;

/// <summary>
/// Page object for interacting with the FileReferenceField component in E2E tests.
/// Provides locators for upload controls, camera capture, and rendered file items.
/// </summary>
public class FileReferenceFieldPage
{
    private readonly IPage _page;

    public FileReferenceFieldPage(IPage page) => _page = page;

    // Upload controls
    /// <summary>Gets the file upload button.</summary>
    public ILocator UploadButton => MudBlazorHelpers.TestId(_page, "file-upload-btn");

    /// <summary>Gets the camera capture button (mobile/webcam).</summary>
    public ILocator CameraButton => MudBlazorHelpers.TestId(_page, "camera-capture-btn");

    // File items (use prefix matching for indexed items)
    /// <summary>Gets all rendered file item elements.</summary>
    public ILocator FileItems => MudBlazorHelpers.TestIdPrefix(_page, "file-item-");

    /// <summary>Gets the upload progress indicator for a specific file by index.</summary>
    public ILocator FileProgress(int index) => MudBlazorHelpers.TestId(_page, $"file-progress-{index}");

    /// <summary>Gets the download link for a specific file by index.</summary>
    public ILocator FileDownload(int index) => MudBlazorHelpers.TestId(_page, $"file-download-{index}");

    /// <summary>Gets the display element (name, size, type) for a specific file by index.</summary>
    public ILocator FileDisplay(int index) => MudBlazorHelpers.TestId(_page, $"file-display-{index}");

    /// <summary>Returns the current count of rendered file items.</summary>
    public async Task<int> GetFileCountAsync() => await FileItems.CountAsync();
}
