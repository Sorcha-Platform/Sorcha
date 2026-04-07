// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Agent.Execution;

namespace Sorcha.Agent.Tests.Execution;

public class FileUploadHandlerTests : IDisposable
{
    private readonly string _tempDir;

    public FileUploadHandlerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sorcha-fileupload-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GenerateTestFile_ProducesDeterministicOutput()
    {
        var file1 = FileUploadHandler.GenerateTestFile(1024, 85);
        var file2 = FileUploadHandler.GenerateTestFile(1024, 85);

        file1.Should().HaveCount(1024);
        file2.Should().HaveCount(1024);
        file1.Should().BeEquivalentTo(file2);
    }

    [Fact]
    public void GenerateTestFile_DifferentSeedsProduceDifferentOutput()
    {
        var file1 = FileUploadHandler.GenerateTestFile(1024, 85);
        var file2 = FileUploadHandler.GenerateTestFile(1024, 42);

        file1.Should().NotBeEquivalentTo(file2);
    }

    [Fact]
    public void GenerateTestFile_RespectsSize()
    {
        var file512 = FileUploadHandler.GenerateTestFile(512, 1);
        var file4mb = FileUploadHandler.GenerateTestFile(4 * 1024 * 1024, 1);

        file512.Should().HaveCount(512);
        file4mb.Should().HaveCount(4 * 1024 * 1024);
    }

    [Fact]
    public void ComputeSha256_ReturnsConsistentHash()
    {
        var data = FileUploadHandler.GenerateTestFile(1024, 85);

        var hash1 = FileUploadHandler.ComputeSha256(data);
        var hash2 = FileUploadHandler.ComputeSha256(data);

        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(64); // 256 bits = 64 hex chars
        hash1.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ComputeSha256_DifferentDataProducesDifferentHash()
    {
        var data1 = FileUploadHandler.GenerateTestFile(1024, 85);
        var data2 = FileUploadHandler.GenerateTestFile(1024, 42);

        FileUploadHandler.ComputeSha256(data1).Should().NotBe(FileUploadHandler.ComputeSha256(data2));
    }

    [Fact]
    public void GenerateTestFile_EmptyFile_ReturnsEmptyArray()
    {
        var file = FileUploadHandler.GenerateTestFile(0, 1);
        file.Should().BeEmpty();
    }
}
