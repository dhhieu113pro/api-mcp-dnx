using ApiMcp.Helpers;
using Xunit;

namespace ApiMcp.Tests;

public sealed class FileAccessPolicyTests : IDisposable
{
    private readonly string _rootDir;
    private readonly string _outsideDir;
    private readonly string _fileUnderRoot;
    private readonly string _fileOutside;
    private readonly string? _originalEnv;

    public FileAccessPolicyTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "apimcp-roots-" + Guid.NewGuid().ToString("N"));
        _outsideDir = Path.Combine(Path.GetTempPath(), "apimcp-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
        Directory.CreateDirectory(_outsideDir);

        _fileUnderRoot = Path.Combine(_rootDir, "under.txt");
        _fileOutside = Path.Combine(_outsideDir, "outside.txt");
        File.WriteAllText(_fileUnderRoot, "x");
        File.WriteAllText(_fileOutside, "y");

        _originalEnv = Environment.GetEnvironmentVariable("APIMCP_FILE_ROOTS");
        Environment.SetEnvironmentVariable("APIMCP_FILE_ROOTS", null);
        FileAccessPolicy.LoadFromEnv();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("APIMCP_FILE_ROOTS", _originalEnv);
        Directory.Delete(_rootDir, recursive: true);
        Directory.Delete(_outsideDir, recursive: true);
    }

    [Fact]
    public void NoRootsConfigured_AllowsAnyExistingFile()
    {
        Assert.False(FileAccessPolicy.Enforced);
        Assert.Empty(FileAccessPolicy.Roots);
        Assert.Equal(Path.GetFullPath(_fileUnderRoot), FileAccessPolicy.Resolve(_fileUnderRoot));
    }

    [Fact]
    public void WithRoots_AllowsFileUnderRoot()
    {
        Environment.SetEnvironmentVariable("APIMCP_FILE_ROOTS", _rootDir);
        FileAccessPolicy.LoadFromEnv();

        Assert.True(FileAccessPolicy.Enforced);
        Assert.Equal(Path.GetFullPath(_fileUnderRoot), FileAccessPolicy.Resolve(_fileUnderRoot));
    }

    [Fact]
    public void WithRoots_RejectsFileOutsideRoot()
    {
        Environment.SetEnvironmentVariable("APIMCP_FILE_ROOTS", _rootDir);
        FileAccessPolicy.LoadFromEnv();

        var ex = Assert.Throws<InvalidOperationException>(() => FileAccessPolicy.Resolve(_fileOutside));

        Assert.Contains("APIMCP_FILE_ROOTS", ex.Message);
        Assert.Contains(_fileOutside, ex.Message);
    }

    [Fact]
    public void WithRoots_SemicolonSeparated_AllowsAllListedRoots()
    {
        Environment.SetEnvironmentVariable("APIMCP_FILE_ROOTS", $"{_rootDir};{_outsideDir}");
        FileAccessPolicy.LoadFromEnv();

        Assert.Equal(2, FileAccessPolicy.Roots.Count);
        Assert.Equal(Path.GetFullPath(_fileOutside), FileAccessPolicy.Resolve(_fileOutside));
        Assert.Equal(Path.GetFullPath(_fileUnderRoot), FileAccessPolicy.Resolve(_fileUnderRoot));
    }

    [Fact]
    public void WithRoots_RelativePathInRoot_ResolvesToFullPath()
    {
        Environment.SetEnvironmentVariable("APIMCP_FILE_ROOTS", _rootDir);
        FileAccessPolicy.LoadFromEnv();

        var relative = Path.Combine(_rootDir, "under.txt");
        Assert.Equal(Path.GetFullPath(relative), FileAccessPolicy.Resolve(relative));
    }

    [Fact]
    public void Resolve_MissingFile_ThrowsFileNotFound()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => FileAccessPolicy.Resolve(Path.Combine(_rootDir, "nope.txt")));

        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_NullOrWhitespace_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => FileAccessPolicy.Resolve("  "));
        Assert.Throws<InvalidOperationException>(() => FileAccessPolicy.Resolve(""));
    }
}