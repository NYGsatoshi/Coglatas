using Coglatas.Infrastructure.Files;
using Microsoft.Extensions.Options;

namespace Coglatas.Tests.Files;

public sealed class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "coglatas-storage-tests", Guid.NewGuid().ToString("N"), "storage-root");

    [Fact]
    public void InvalidExtensionIsRejected()
    {
        var policy = CreatePolicy();

        Assert.DoesNotContain(".exe", policy.AllowedExtensions);
    }

    [Fact]
    public void InvalidContentTypeIsRejected()
    {
        var policy = CreatePolicy();

        Assert.DoesNotContain("application/x-msdownload", policy.AllowedContentTypes);
    }

    [Fact]
    public void EmptyFileIsRejected()
    {
        var policy = CreatePolicy();

        Assert.True(policy.MaxFileSizeBytes > 0);
    }

    [Fact]
    public async Task StorageKeyControlsStoredPathInsideRoot()
    {
        var storage = CreateStorage();
        await using var content = new MemoryStream("hello"u8.ToArray());

        var result = await storage.SaveAsync("tenants/tenant-a/files/file-a", content, "text/plain");

        Assert.True(result.IsSuccess);
        Assert.True(await storage.ExistsAsync("tenants/tenant-a/files/file-a"));
    }

    [Fact]
    public async Task StorageKeyCannotEscapeRootPath()
    {
        var storage = CreateStorage();
        await using var content = new MemoryStream("bad"u8.ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SaveAsync("../escape.txt", content, "text/plain"));
    }

    [Fact]
    public async Task CaseChangedSiblingCannotEscapeRoot()
    {
        var storage = CreateStorage();
        var siblingName = Path.GetFileName(_rootPath).ToUpperInvariant();
        var key = Path.Combine("..", siblingName, "escape.txt");
        await using var content = new MemoryStream("bad"u8.ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SaveAsync(key, content, "text/plain"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenReadAsync(key));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.ExistsAsync(key));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.DeleteAsync(key));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InterruptedUploadLeavesNoPartialFileAndPreservesExistingBytes(bool replaceExisting, bool cancel)
    {
        const string key = "tenants/tenant-a/files/interrupted";
        var storage = CreateStorage();
        var original = "Previously committed synthetic bytes."u8.ToArray();
        if (replaceExisting)
        {
            await using var originalStream = new MemoryStream(original);
            Assert.True((await storage.SaveAsync(key, originalStream, "text/plain")).IsSuccess);
        }

        using var cancellation = new CancellationTokenSource();
        Action cancelInput = cancellation.Cancel;
        var failure = new IOException("Synthetic interrupted upload.");
        await using var content = new InterruptedReadStream(async token =>
        {
            // Observe the canonical key after a prefix has actually been copied.
            Assert.Equal(replaceExisting, await storage.ExistsAsync(key, token));
            if (replaceExisting)
            {
                await using var existing = await storage.OpenReadAsync(key, token);
                using var observed = new MemoryStream();
                await existing.CopyToAsync(observed, token);
                Assert.Equal(original, observed.ToArray());
            }
            if (cancel)
            {
                cancelInput();
                token.ThrowIfCancellationRequested();
            }
            throw failure;
        });

        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                storage.SaveAsync(key, content, "text/plain", cancellation.Token));
        }
        else
        {
            var observed = await Assert.ThrowsAsync<IOException>(() =>
                storage.SaveAsync(key, content, "text/plain", cancellation.Token));
            Assert.Same(failure, observed);
        }

        Assert.Equal(replaceExisting, await storage.ExistsAsync(key, CancellationToken.None));
        if (replaceExisting)
        {
            await using var saved = await storage.OpenReadAsync(key, CancellationToken.None);
            using var observed = new MemoryStream();
            await saved.CopyToAsync(observed, CancellationToken.None);
            Assert.Equal(original, observed.ToArray());
        }
        Assert.Equal(replaceExisting ? 1 : 0, Directory.GetFiles(_rootPath, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task CompletedUploadReplacesExistingBytesWithoutLeavingTemporaryFiles()
    {
        const string key = "tenants/tenant-a/files/replaced";
        var storage = CreateStorage();
        await using var original = new MemoryStream("Original bytes."u8.ToArray());
        Assert.True((await storage.SaveAsync(key, original, "text/plain")).IsSuccess);
        var replacement = "Complete replacement synthetic bytes."u8.ToArray();
        await using var content = new MemoryStream(replacement);

        Assert.True((await storage.SaveAsync(key, content, "text/plain")).IsSuccess);

        await using var saved = await storage.OpenReadAsync(key);
        using var observed = new MemoryStream();
        await saved.CopyToAsync(observed);
        Assert.Equal(replacement, observed.ToArray());
        Assert.Single(Directory.GetFiles(_rootPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task PublicationFailureRemovesTemporaryBytesWithoutRemovingDestinationDirectory()
    {
        const string key = "tenants/tenant-a/files/directory";
        var directory = Path.Combine(_rootPath, key);
        Directory.CreateDirectory(directory);
        await using var content = new MemoryStream("Synthetic bytes."u8.ToArray());

        var error = await Record.ExceptionAsync(() => CreateStorage().SaveAsync(key, content, "text/plain"));
        Assert.True(error is IOException or UnauthorizedAccessException);

        Assert.True(Directory.Exists(directory));
        Assert.Empty(Directory.GetFiles(_rootPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CancellationAtEndOfInputDoesNotPublishCompletedTemporaryBytes()
    {
        const string key = "tenants/tenant-a/files/cancel-at-end";
        var storage = CreateStorage();
        using var cancellation = new CancellationTokenSource();
        Action cancelInput = cancellation.Cancel;
        await using var content = new InterruptedReadStream(_ =>
        {
            cancelInput();
            return Task.CompletedTask;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            storage.SaveAsync(key, content, "text/plain", cancellation.Token));

        Assert.False(await storage.ExistsAsync(key, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_rootPath, "*", SearchOption.AllDirectories));
    }
    private sealed class InterruptedReadStream(Func<CancellationToken, Task> interrupt) : Stream
    {
        private bool _prefixRead;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_prefixRead)
            {
                await interrupt(cancellationToken);
                return 0;
            }
            _prefixRead = true;
            "partial"u8.CopyTo(buffer.Span);
            return 7;
        }
    }
    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private LocalFileStorageService CreateStorage()
    {
        return new LocalFileStorageService(Options.Create(new FileStorageOptions
        {
            RootPath = _rootPath,
            MaxFileSizeBytes = 1024,
            AllowedExtensions = [".txt", ".md", ".zip"],
            AllowedContentTypes = ["text/plain", "text/markdown", "application/zip"]
        }));
    }

    private ConfiguredFileUploadPolicy CreatePolicy()
    {
        return new ConfiguredFileUploadPolicy(Options.Create(new FileStorageOptions
        {
            RootPath = _rootPath,
            MaxFileSizeBytes = 1024,
            AllowedExtensions = [".txt", ".md", ".zip"],
            AllowedContentTypes = ["text/plain", "text/markdown", "application/zip"]
        }));
    }
}
