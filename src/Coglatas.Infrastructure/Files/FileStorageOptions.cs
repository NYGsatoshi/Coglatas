namespace Coglatas.Infrastructure.Files;

public sealed class FileStorageOptions
{
    public string Provider { get; set; } = "LocalFileSystem";

    public string RootPath { get; set; } = string.Empty;

    public long MaxFileSizeBytes { get; set; } = 50L * 1024 * 1024;

    public string[] AllowedExtensions { get; set; } = [];

    public string[] AllowedContentTypes { get; set; } = [];

    public bool UseSignedUrls { get; set; }

    public string? BucketName { get; set; }

    public string? Region { get; set; }

    public string? Endpoint { get; set; }

    public bool UsePathStyle { get; set; }

    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }
}
