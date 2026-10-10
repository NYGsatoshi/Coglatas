using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace Coglatas.Tests.SecurityArchitecture;

// Coverlet 10.1.0 preserves the original module in the temporary directory and
// injects a tracker named AssemblyName_GUID. These observations are not attestation.
internal static class SecurityArchitectureInventoryAssemblyBinding
{
    private const string TrackerNamespace = "Coverlet.Core.Instrumentation.Tracker";
    private const int MaximumAssemblyBytes = 32 * 1024 * 1024;

    public static async Task<InventoryAssemblyBinding> CaptureAsync(Assembly loaded, string producerPath)
    {
        var name = loaded.GetName().Name ?? throw new InvalidOperationException("Assembly name unavailable.");
        var producer = await ReadBoundedAsync(producerPath);
        var execution = await ReadBoundedAsync(loaded.Location);
        var producerMetadata = Inspect(producer, name);
        var executionMetadata = Inspect(execution, name);
        if (producerMetadata.Trackers.Length != 0 || executionMetadata.ModuleVersionId != loaded.ManifestModule.ModuleVersionId ||
            executionMetadata.ModuleVersionId != producerMetadata.ModuleVersionId)
            throw new InvalidOperationException("Original producer and actual loaded module metadata disagree.");
        var producerDigest = Digest(producer);
        var loadedDigest = Digest(execution);
        string? identifier = null;
        string? backupDigest = null;
        if (producerDigest != loadedDigest)
        {
            if (executionMetadata.Trackers.Length != 1)
                throw new InvalidOperationException("Changed execution module has no unique collector tracker.");
            var tracker = executionMetadata.Trackers[0];
            var prefix = name + "_";
            if (!tracker.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Collector tracker belongs to another module.");
            identifier = tracker[prefix.Length..];
            if (!Guid.TryParseExact(identifier, "D", out _))
                throw new InvalidOperationException("Invalid collector tracker identity.");
            // The generated type Name contains the assembly-name dot. Match actual
            // reflection namespace/name components rather than parsing a qualified lookup.
            var actualTrackers = loaded.DefinedTypes.Where(type => type.Namespace == TrackerNamespace).ToArray();
            if (actualTrackers.Length != 1 || actualTrackers[0].Name != tracker)
                throw new InvalidOperationException("File tracker is absent from the actual loaded assembly.");
            var backup = await ReadBoundedAsync(Path.Combine(TestTemporaryDirectoryBootstrap.CollectorTemporaryDirectory,
                name + "_" + identifier + ".dll"));
            var backupMetadata = Inspect(backup, name);
            if (backupMetadata.Trackers.Length != 0 || backupMetadata.ModuleVersionId != producerMetadata.ModuleVersionId)
                throw new InvalidOperationException("Collector backup is not the original producer module.");
            backupDigest = Digest(backup);
        }
        else if (executionMetadata.Trackers.Length != 0)
            throw new InvalidOperationException("Direct producer identity cannot contain collector instrumentation.");

        var binding = ValidateDigests(producerDigest, loadedDigest, backupDigest, identifier);
        return new InventoryAssemblyBinding(name, producerDigest, loadedDigest, backupDigest, binding,
            identifier is null ? "NOT_APPLICABLE_DIRECT_BYTE_IDENTITY" : "UNVERIFIED",
            "EXACT_CANONICAL_ORIGINAL_BYTES", "CANONICAL_PRODUCER_CHILD_HOST");
    }

    internal static string ValidateDigests(string producer, string loaded, string? backup, string? identifier)
    {
        static bool IsDigest(string value) => value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
        if (!IsDigest(producer) || !IsDigest(loaded) || backup is not null && !IsDigest(backup))
            throw new InvalidOperationException("Invalid assembly digest.");
        if (producer == loaded)
        {
            if (backup is not null || identifier is not null)
                throw new InvalidOperationException("Direct identity cannot claim a distinct collector backup.");
            return "DIRECT_PRODUCER_AND_TEST_BYTE_IDENTITY";
        }
        if (backup != producer || identifier is null || !Guid.TryParseExact(identifier, "D", out _))
            throw new InvalidOperationException("Changed execution module lacks an exact original producer binding.");
        return "COLLECTOR_ORIGINAL_BACKUP_AND_DISTINCT_INSTRUMENTED_TEST_MODULE";
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static async Task<byte[]> ReadBoundedAsync(string path)
    {
        await using var source = File.OpenRead(path);
        if (source.Length is <= 0 or > MaximumAssemblyBytes)
            throw new InvalidOperationException("Assembly input exceeds the bounded size.");
        var bytes = new byte[(int)source.Length];
        await source.ReadExactlyAsync(bytes);
        if (source.ReadByte() != -1)
            throw new InvalidOperationException("Assembly input changed while being read.");
        return bytes;
    }

    private static (Guid ModuleVersionId, string[] Trackers) Inspect(byte[] bytes, string expectedName)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        if (!metadata.IsAssembly || metadata.GetString(metadata.GetAssemblyDefinition().Name) != expectedName)
            throw new InvalidOperationException("Wrong assembly module identity.");
        var trackers = metadata.TypeDefinitions.Select(handle => metadata.GetTypeDefinition(handle))
            .Where(type => metadata.GetString(type.Namespace) == TrackerNamespace)
            .Select(type => metadata.GetString(type.Name)).ToArray();
        return (metadata.GetGuid(metadata.GetModuleDefinition().Mvid), trackers);
    }
}

internal sealed record InventoryAssemblyBinding(string AssemblyName, string ProducerDigest, string TestLoadedDigest,
    string? OriginalBackupDigest, string TestExecutionBinding, string InstrumentationAuthenticity,
    string ProducerBinding, string ChildHostExecution);
