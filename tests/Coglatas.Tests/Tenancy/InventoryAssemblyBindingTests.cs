using Coglatas.Tests.SecurityArchitecture;

namespace Coglatas.Tests.Tenancy;

public sealed class InventoryAssemblyBindingTests
{
    [Theory]
    [InlineData(false, "DIRECT_PRODUCER_AND_TEST_BYTE_IDENTITY")]
    [InlineData(true, "COLLECTOR_ORIGINAL_BACKUP_AND_DISTINCT_INSTRUMENTED_TEST_MODULE")]
    public void DigestBindingKeepsOriginalAndInstrumentedExecutionDistinct(bool instrumented, string expected)
    {
        var producer = new string('a', 64);
        Assert.Equal(expected, SecurityArchitectureInventoryAssemblyBinding.ValidateDigests(producer,
            instrumented ? new string('b', 64) : producer, instrumented ? producer : null,
            instrumented ? "2f04b322-3dc9-484b-a214-cbdeafea432d" : null));
    }

    [Theory]
    [InlineData("MISSING_BACKUP")]
    [InlineData("MISMATCHED_BACKUP")]
    [InlineData("MISSING_TRACKER")]
    [InlineData("FORGED_TRACKER")]
    [InlineData("INVALID_LOADED_DIGEST")]
    [InlineData("AMBIGUOUS_DIRECT_IDENTITY")]
    public void DigestBindingRejectsIncompleteChangedOrForgedIdentity(string mutation)
    {
        var producer = new string('a', 64);
        var loaded = new string('b', 64);
        string? backup = producer;
        string? identifier = "2f04b322-3dc9-484b-a214-cbdeafea432d";
        switch (mutation)
        {
            case "MISSING_BACKUP": backup = null; break;
            case "MISMATCHED_BACKUP": backup = new string('c', 64); break;
            case "MISSING_TRACKER": identifier = null; break;
            case "FORGED_TRACKER": identifier = "../untrusted-module"; break;
            case "INVALID_LOADED_DIGEST": loaded = new string('z', 64); break;
            case "AMBIGUOUS_DIRECT_IDENTITY": loaded = producer; break;
            default: throw new InvalidOperationException("Unclassified binding mutation.");
        }
        Assert.Throws<InvalidOperationException>(() =>
            SecurityArchitectureInventoryAssemblyBinding.ValidateDigests(producer, loaded, backup, identifier));
    }
}
