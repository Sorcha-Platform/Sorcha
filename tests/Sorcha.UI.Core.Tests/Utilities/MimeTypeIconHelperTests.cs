// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using MudBlazor;
using Sorcha.UI.Core.Utilities;
using Xunit;

namespace Sorcha.UI.Core.Tests.Utilities;

/// <summary>
/// Unit tests for <see cref="MimeTypeIconHelper"/> MIME-type-to-icon mapping.
/// </summary>
public class MimeTypeIconHelperTests
{
    #region Image Types

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/gif")]
    [InlineData("image/webp")]
    [InlineData("image/svg+xml")]
    public void GetIcon_ImageType_ReturnsImageIcon(string contentType)
    {
        // Act
        var icon = MimeTypeIconHelper.GetIcon(contentType);

        // Assert
        icon.Should().Be(Icons.Material.Filled.Image, $"'{contentType}' is an image type");
    }

    #endregion

    #region PDF

    [Fact]
    public void GetIcon_ApplicationPdf_ReturnsPdfIcon()
    {
        // Act
        var icon = MimeTypeIconHelper.GetIcon("application/pdf");

        // Assert
        icon.Should().Be(Icons.Material.Filled.PictureAsPdf, "application/pdf should map to the PDF icon");
    }

    #endregion

    #region Document Types

    [Theory]
    [InlineData("application/msword")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("application/vnd.oasis.opendocument.text")]
    public void GetIcon_WordDocumentType_ReturnsDescriptionIcon(string contentType)
    {
        // Act
        var icon = MimeTypeIconHelper.GetIcon(contentType);

        // Assert
        icon.Should().Be(Icons.Material.Filled.Description, $"'{contentType}' is a word-processing document type");
    }

    #endregion

    #region Spreadsheet Types

    [Theory]
    [InlineData("application/vnd.ms-excel")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("text/csv")]
    public void GetIcon_SpreadsheetType_ReturnsTableChartIcon(string contentType)
    {
        // Act
        var icon = MimeTypeIconHelper.GetIcon(contentType);

        // Assert
        icon.Should().Be(Icons.Material.Filled.TableChart, $"'{contentType}' is a spreadsheet type");
    }

    #endregion

    #region Archive Types

    [Theory]
    [InlineData("application/zip")]
    [InlineData("application/x-zip-compressed")]
    [InlineData("application/x-compressed")]
    [InlineData("application/x-tar")]
    public void GetIcon_ArchiveType_ReturnsFolderZipIcon(string contentType)
    {
        // Act
        var icon = MimeTypeIconHelper.GetIcon(contentType);

        // Assert
        icon.Should().Be(Icons.Material.Filled.FolderZip, $"'{contentType}' is an archive type");
    }

    #endregion

    #region Video Types

    [Theory]
    [InlineData("video/mp4")]
    [InlineData("video/webm")]
    [InlineData("video/quicktime")]
    public void GetIcon_VideoType_ReturnsVideoFileIcon(string contentType)
    {
        // Act
        var icon = MimeTypeIconHelper.GetIcon(contentType);

        // Assert
        icon.Should().Be(Icons.Material.Filled.VideoFile, $"'{contentType}' is a video type");
    }

    #endregion

    #region Audio Types

    [Theory]
    [InlineData("audio/mpeg")]
    [InlineData("audio/wav")]
    [InlineData("audio/ogg")]
    public void GetIcon_AudioType_ReturnsAudioFileIcon(string contentType)
    {
        // Act
        var icon = MimeTypeIconHelper.GetIcon(contentType);

        // Assert
        icon.Should().Be(Icons.Material.Filled.AudioFile, $"'{contentType}' is an audio type");
    }

    #endregion

    #region Unknown / Fallback

    [Theory]
    [InlineData("application/octet-stream")]
    [InlineData("text/plain")]
    [InlineData("application/json")]
    [InlineData("text/html")]
    [InlineData("some/unknown-type")]
    public void GetIcon_UnknownType_ReturnsGenericFileIcon(string contentType)
    {
        // Act
        var icon = MimeTypeIconHelper.GetIcon(contentType);

        // Assert
        icon.Should().Be(Icons.Material.Filled.InsertDriveFile, $"'{contentType}' is an unknown type and should fall back to generic file icon");
    }

    [Fact]
    public void GetIcon_Null_ReturnsGenericFileIcon()
    {
        // Act
        var icon = MimeTypeIconHelper.GetIcon(null);

        // Assert
        icon.Should().Be(Icons.Material.Filled.InsertDriveFile, "null content type should fall back to generic file icon");
    }

    [Fact]
    public void GetIcon_EmptyString_ReturnsGenericFileIcon()
    {
        // Act
        var icon = MimeTypeIconHelper.GetIcon(string.Empty);

        // Assert
        icon.Should().Be(Icons.Material.Filled.InsertDriveFile, "empty string content type should fall back to generic file icon");
    }

    #endregion
}
