using Coglatas.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace Coglatas.Infrastructure.Files;

public sealed class ConfiguredFileUploadPolicy(IOptions<FileStorageOptions> options) : IFileUploadPolicy
{
    private readonly FileStorageOptions _options = options.Value;

    public long MaxFileSizeBytes => _options.MaxFileSizeBytes;

    public IReadOnlyCollection<string> AllowedExtensions => _options.AllowedExtensions;

    public IReadOnlyCollection<string> AllowedContentTypes => _options.AllowedContentTypes;
}
