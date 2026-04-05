// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// E2E tests for the FileReferenceField component — upload, progress, and download flows.
/// These tests require a live Docker environment with a blueprint that contains file-type fields.
/// </summary>
[TestFixture]
[Category("Docker")]
[Category("Authenticated")]
[Category("Files")]
[Parallelizable(ParallelScope.Self)]
public class FileAttachmentTests : AuthenticatedDockerTestBase
{
    private FileReferenceFieldPage _filePage = null!;

    [SetUp]
    public override async Task BaseSetUp()
    {
        await base.BaseSetUp();
        _filePage = new FileReferenceFieldPage(Page);
    }

    [Test]
    [Ignore("Requires blueprint with file fields — run manually with Docker")]
    public async Task FileUpload_SelectFile_ShowsProgress()
    {
        // Navigate to action with file field
        // Upload a test file
        // Verify progress bar appears
        // Verify file reference populated
        await Task.CompletedTask;
    }

    [Test]
    [Ignore("Requires blueprint with file fields — run manually with Docker")]
    public async Task FileDownload_ClickLink_DownloadsFile()
    {
        // Navigate to completed action with file attachment
        // Verify metadata displayed
        // Click download
        // Verify file downloaded
        await Task.CompletedTask;
    }
}
