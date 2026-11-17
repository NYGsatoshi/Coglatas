using System.Security.Cryptography;
using BoundProduct = Coglatas.Tests.SecurityArchitecture.SecurityArchitectureRlsComposedHostFixture.BoundProduct;

namespace Coglatas.Tests.SecurityArchitecture;

[Trait("Portability", "CrossPlatform")]
public sealed class SecurityArchitectureRlsComposedHostCopyTests
{
    private const string Module = "Coglatas.SyntheticRecoveryAcceptance.dll";

    [Fact]
    public void BoundToolCopiesCanonicalProducerBytesInsteadOfInstrumentedTestBytes()
    {
        using var sandbox = new Sandbox();
        var products = sandbox.Products();

        SecurityArchitectureRlsComposedHostFixture.PrepareOwnedApplication(sandbox.Web, sandbox.Loaded, sandbox.Owned, products);

        Assert.Equal(File.ReadAllBytes(sandbox.ProducerModule), File.ReadAllBytes(sandbox.OwnedModule));
        Assert.NotEqual(File.ReadAllBytes(sandbox.LoadedModule), File.ReadAllBytes(sandbox.OwnedModule));
    }

    [Fact]
    public void UnclassifiedProductStillCannotEnterCanonicalChild()
    {
        const string unexpected = "Coglatas.Unclassified.dll";
        using var sandbox = new Sandbox();
        var products = sandbox.Products();
        File.WriteAllBytes(Path.Combine(sandbox.Loaded, unexpected), [9]);

        Assert.Throws<Xunit.Sdk.FalseException>(() => SecurityArchitectureRlsComposedHostFixture.PrepareOwnedApplication(
            sandbox.Web, sandbox.Loaded, sandbox.Owned, products));
        Assert.False(File.Exists(Path.Combine(sandbox.Owned, unexpected)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedProducerOrTestModuleIsRejected(bool changeLoaded)
    {
        using var sandbox = new Sandbox();
        var products = sandbox.Products();
        File.WriteAllBytes(changeLoaded ? sandbox.LoadedModule : sandbox.ProducerModule, [9]);

        Assert.Throws<Xunit.Sdk.EqualException>(() => SecurityArchitectureRlsComposedHostFixture.PrepareOwnedApplication(
            sandbox.Web, sandbox.Loaded, sandbox.Owned, products));
    }

    [Fact]
    public void ExistingWrongDestinationIsRejectedRatherThanOverwritten()
    {
        using var sandbox = new Sandbox();
        var products = sandbox.Products();
        Directory.CreateDirectory(sandbox.Owned);
        File.WriteAllBytes(sandbox.OwnedModule, [9]);

        Assert.Throws<Xunit.Sdk.EqualException>(() => SecurityArchitectureRlsComposedHostFixture.PrepareOwnedApplication(
            sandbox.Web, sandbox.Loaded, sandbox.Owned, products));
        Assert.Equal((byte)9, Assert.Single(File.ReadAllBytes(sandbox.OwnedModule)));
    }

    private sealed class Sandbox : IDisposable
    {
        private string Root { get; } = Path.Combine(Path.GetTempPath(), "coglatas-owned-copy-" + Guid.NewGuid().ToString("N"));
        public string Web => Path.Combine(Root, "producer");
        public string Loaded => Path.Combine(Root, "loaded");
        public string Owned => Path.Combine(Root, "owned");
        public string ProducerModule => Path.Combine(Web, Module);
        public string LoadedModule => Path.Combine(Loaded, Module);
        public string OwnedModule => Path.Combine(Owned, Module);

        public Sandbox()
        {
            Directory.CreateDirectory(Web);
            Directory.CreateDirectory(Loaded);
        }

        public Dictionary<string, BoundProduct> Products()
        {
            File.WriteAllBytes(ProducerModule, [1, 2, 3]);
            File.WriteAllBytes(LoadedModule, [4, 5, 6]);
            var original = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(ProducerModule)));
            var loaded = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(LoadedModule)));
            var binding = new InventoryAssemblyBinding(Path.GetFileNameWithoutExtension(Module), original, loaded, original,
                "COLLECTOR_ORIGINAL_BACKUP_AND_DISTINCT_INSTRUMENTED_TEST_MODULE", "UNVERIFIED",
                "EXACT_CANONICAL_ORIGINAL_BYTES", "CANONICAL_PRODUCER_CHILD_HOST");
            return new Dictionary<string, BoundProduct>(StringComparer.Ordinal) { [Module] = new(ProducerModule, binding) };
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
