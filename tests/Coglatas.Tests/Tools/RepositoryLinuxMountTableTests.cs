using Coglatas.SecurityArchitecture;

namespace Coglatas.Tests.Tools;

[Trait("Portability", "CrossPlatform")]
public sealed class RepositoryLinuxMountTableTests
{
    private static readonly string[] Mounts =
    [
        "1 0 8:1 / / rw - ext4 /dev/sda rw",
        @"2 1 0:70 /host/space\040name/repository /work/repository rw - 9p host rw",
        @"3 1 0:70 /host/space\040name/repository /alias rw - 9p host rw",
        @"4 1 0:70 /host/space\040name/repository/nested /child-alias rw - 9p host rw",
        "5 1 8:1 /usr/bin /host-bin rw - ext4 /dev/sda rw",
        "6 2 0:71 /foreign/source /work/repository/plugins rw - tmpfs tmpfs rw",
        "7 1 0:71 /foreign/source /plugin-alias rw - tmpfs tmpfs rw",
        "8 1 0:71 /trusted/bin /plugin-host rw - tmpfs tmpfs rw"
    ];

    [Theory]
    [InlineData("/alias", true)]
    [InlineData("/alias/git", true)]
    [InlineData("/child-alias/git", true)]
    [InlineData("/work/repository/nested/git", true)]
    [InlineData("/plugin-alias/git", true)]
    [InlineData("/plugin-host/git", false)]
    [InlineData("/work/repository-sibling/git", false)]
    [InlineData("/host-bin/git", false)]
    public void BindRootsAndBoundSubdirectoriesUseTheUnderlyingFilesystem(string candidate, bool inside)
    {
        var table = new RepositoryLinuxMountTable(Mounts);
        Assert.Equal(inside, table.IsWithinRepository("/work/repository", candidate));
        Assert.True(table.IsWithinRepository("/alias", "/work/repository/git"));
        Assert.True(table.IsWithinRepository("/work/repository/nested", "/child-alias/git"));
    }

    [Fact]
    public void OptionalMountFieldsAndEscapedBackslashesDoNotChangeIdentity()
    {
        var table = new RepositoryLinuxMountTable([
            @"1 0 8:1 / / rw - ext4 /dev/sda rw",
            @"2 1 8:1 /source\134040 /repository rw shared:1 future:tag - ext4 /dev/sda rw",
            @"3 1 8:1 /source\134040 /alias rw - ext4 /dev/sda rw"]);
        Assert.True(table.IsWithinRepository("/repository", "/alias/git"));
        Assert.False(table.IsWithinRepository("/repository", "/source name/git"));
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("1 0 invalid / / rw - ext4 /dev/sda rw")]
    [InlineData("1 0 8:1 relative / rw - ext4 /dev/sda rw")]
    [InlineData(@"1 0 8:1 /bad\999 / rw - ext4 /dev/sda rw")]
    [InlineData("1 0 8:1 /source/../other / rw - ext4 /dev/sda rw")]
    public void MalformedMountEvidenceFailsClosed(string line) =>
        Assert.Throws<IOException>(() => new RepositoryLinuxMountTable([line]));

    [Fact]
    public void MissingAndStackedMappingsFailClosedInsteadOfGuessingVisibility()
    {
        Assert.Throws<IOException>(() => new RepositoryLinuxMountTable([]).IsWithinRepository("/repository", "/git"));
        var stacked = new RepositoryLinuxMountTable(Mounts.Concat([
            "6 2 0:71 /different /work/repository rw - tmpfs tmpfs rw"]));
        Assert.Throws<IOException>(() => stacked.IsWithinRepository("/work/repository", "/usr/bin/git"));
        Assert.Throws<IOException>(() => stacked.IsWithinRepository("/usr", "/work/repository/nested/git"));
    }

    [Theory]
    [InlineData("/layers/upper/repository/git", true)]
    [InlineData("/layers/lower/repository/git", true)]
    [InlineData("/upper-alias/repository/git", true)]
    [InlineData("/layers/upper/redirected-old-name/git", true)]
    [InlineData("/usr/bin/git", false)]
    public void ExposedOverlayLayersAreNotIndependentHostInstallations(string candidate, bool inside)
    {
        var table = new RepositoryLinuxMountTable([
            "1 0 8:1 / / rw - ext4 /dev/sda rw",
            "2 1 0:72 / /merged rw - overlay overlay rw,lowerdir=/layers/lower,upperdir=/layers/upper,workdir=/layers/work",
            "3 1 8:1 /layers/upper /upper-alias rw - ext4 /dev/sda rw"]);
        Assert.Equal(inside, table.IsWithinRepository("/merged/repository", candidate));
    }

    [Fact]
    public void MountedOverlaySubtreesAndNestedLayersKeepTheirBackingBoundary()
    {
        var table = new RepositoryLinuxMountTable([
            "1 0 8:1 / / rw - ext4 /dev/sda rw",
            "2 1 0:72 / /repository/mounted rw - overlay overlay rw,lowerdir=/nested-layer,upperdir=/upper,workdir=/work",
            "3 1 0:73 / /nested-layer rw - overlay overlay rw,lowerdir=/actual/lower"]);
        Assert.True(table.IsWithinRepository("/repository", "/actual/lower/git"));
        Assert.False(table.IsWithinRepository("/repository", "/usr/bin/git"));
    }

    [Theory]
    [InlineData("rw")]
    [InlineData("rw,lowerdir=relative")]
    public void UnprovableOverlayLayersFailClosedForOtherFilesystems(string options)
    {
        var table = new RepositoryLinuxMountTable([
            "1 0 8:1 / / rw - ext4 /dev/sda rw",
            "2 1 0:72 / /merged rw - overlay overlay " + options]);
        Assert.Throws<IOException>(() => table.IsWithinRepository("/merged/repository", "/usr/bin/git"));
    }

    [Fact]
    public void InvisibleAndCyclicOverlayLayersFailClosed()
    {
        string[] lines = ["1 0 8:1 / / rw - ext4 /dev/sda rw",
            "2 1 0:72 / /merged rw - overlay overlay rw,lowerdir=/merged"];
        Assert.Throws<IOException>(() => new RepositoryLinuxMountTable(lines).IsWithinRepository("/merged/repository", "/usr/bin/git"));
        var invisible = new RepositoryLinuxMountTable(lines, _ => throw new IOException("Unavailable layer."));
        Assert.Throws<IOException>(() => invisible.IsWithinRepository("/merged/repository", "/usr/bin/git"));
    }
}
