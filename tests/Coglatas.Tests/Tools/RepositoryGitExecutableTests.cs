using System.Diagnostics;
using Coglatas.SecurityArchitecture;

namespace Coglatas.Tests.Tools;

[Trait("Portability", "CrossPlatform")]
public sealed class RepositoryGitExecutableTests
{
    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("relative-installation")]
    [InlineData("repository")]
    [InlineData("repository-child")]
    [InlineData("quoted-repository")]
    public void RepositoryAndImplicitDirectoriesCannotSelectAnExecutable(string kind)
    {
        using var fixture = new Fixture();
        fixture.Executable(fixture.Repository);
        var child = Directory.CreateDirectory(Path.Combine(fixture.Repository, "bin")).FullName;
        fixture.Executable(child);
        var entry = kind switch
        {
            "repository" => fixture.Repository,
            "repository-child" => child,
            "quoted-repository" => '"' + fixture.Repository + '"',
            _ => kind
        };
        Assert.Throws<FileNotFoundException>(() => RepositoryGitExecutable.Resolve(fixture.Repository, entry));
        Assert.Equal(fixture.TrustedExecutable, RepositoryGitExecutable.Resolve(fixture.Repository,
            entry + Path.PathSeparator + fixture.Installation));
    }

    [Fact]
    public async Task ImmutableBlobUsesTheHostInstallationWithARepositoryExecutablePresent()
    {
        using var fixture = new Fixture();
        var git = RepositoryGitExecutable.Resolve(fixture.Repository, Environment.GetEnvironmentVariable("PATH"));
        var bytes = "Synthetic immutable source\n"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(fixture.Repository, "source.txt"), bytes);
        var hooks = Directory.CreateDirectory(Path.Combine(fixture.Root, "empty-hooks")).FullName;
        await Git(git, fixture.Repository, "init", "--quiet");
        await Git(git, fixture.Repository, "add", "source.txt");
        await Git(git, fixture.Repository, "-c", "core.hooksPath=" + hooks, "-c", "commit.gpgsign=false",
            "-c", "user.name=Synthetic Fixture", "-c", "user.email=fixture@example.invalid",
            "commit", "--quiet", "-m", "Synthetic immutable source");
        var revision = (await Git(git, fixture.Repository, "rev-parse", "HEAD")).Trim();
        fixture.Executable(fixture.Repository);

        var reader = new RepositoryArtifactReader(fixture.Repository);
        Assert.Equal(bytes, await reader.ReadAsync(new(revision, "source.txt", null, new('b', 64))));
        Assert.Null(await reader.ReadAsync(new(revision, "missing.txt", null, new('b', 64))));
    }

    private static async Task<string> Git(string executable, string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await error;
        Assert.Equal(0, process.ExitCode);
        return await output;
    }

    public static IEnumerable<object[]> HostInvocationCases => (OperatingSystem.IsWindows()
        ? new[] { "ordinary" } : ["ordinary", "shim"]).Select(kind => new object[] { kind });

    [Theory]
    [MemberData(nameof(HostInvocationCases))]
    public async Task HostInvocationNamePreservesExactImmutableBlobBytes(string kind)
    {
        using var fixture = new Fixture();
        var git = RepositoryGitExecutable.Resolve(fixture.Repository, Environment.GetEnvironmentVariable("PATH"));
        const string text = "Synthetic invocation-name source\n";
        await File.WriteAllTextAsync(Path.Combine(fixture.Repository, "source.txt"), text);
        await Git(git, fixture.Repository, "init", "--quiet");
        await Git(git, fixture.Repository, "add", "source.txt");
        var hooks = Directory.CreateDirectory(Path.Combine(fixture.Root, "empty-hooks")).FullName;
        await Git(git, fixture.Repository, "-c", "core.hooksPath=" + hooks, "-c", "commit.gpgsign=false",
            "-c", "user.name=Synthetic Fixture", "-c", "user.email=fixture@example.invalid",
            "commit", "--quiet", "-m", "Synthetic invocation fixture");
        var revision = (await Git(git, fixture.Repository, "rev-parse", "HEAD")).Trim();
        var entry = Path.GetDirectoryName(git)!;
        if (kind == "shim")
        {
            var shim = Path.Combine(fixture.Root, "multicall");
            var quoted = "'" + git.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
            await File.WriteAllTextAsync(shim, $"#!/bin/sh\ncase \"$0\" in\n */git) exec {quoted} \"$@\" ;;\n *) exit 41 ;;\nesac\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(shim, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.Delete(fixture.TrustedExecutable);
            File.CreateSymbolicLink(Path.Combine(fixture.Installation, "git"), shim);
            entry = fixture.Installation;
        }
        var selected = RepositoryGitExecutable.Resolve(fixture.Repository, entry);
        Assert.Equal(OperatingSystem.IsWindows() ? "git.exe" : "git", Path.GetFileName(selected));
        Assert.Equal(text, await Git(selected, fixture.Repository, "cat-file", "blob", revision + ":source.txt"));
    }

    [Fact]
    public void NullSearchPathDoesNotEnableImplicitSearch()
    {
        using var fixture = new Fixture();
        fixture.Executable(fixture.Repository);
        Assert.Throws<FileNotFoundException>(() => RepositoryGitExecutable.Resolve(fixture.Repository, null));
    }

    [Fact]
    public void OperatorSelectedAbsoluteInstallationDoesNotSearchTheRepositoryOrPath()
    {
        using var fixture = new Fixture();
        fixture.Executable(fixture.Repository);
        Assert.Equal(fixture.TrustedExecutable,
            RepositoryGitExecutable.Resolve(fixture.Repository, fixture.Repository, fixture.TrustedExecutable));
        Assert.Equal(fixture.TrustedExecutable,
            RepositoryGitExecutable.Resolve(fixture.Repository, null, fixture.TrustedExecutable));
    }

    public static IEnumerable<object[]> InvalidOperatorCases => new[]
        { "empty", "relative", "missing", "directory", "root", "embedded-null", "repository", "repository-alias" }
        .Concat(OperatingSystem.IsWindows() ? [] : new[] { "not-executable" }).Select(kind => new object[] { kind });

    [Theory]
    [MemberData(nameof(InvalidOperatorCases))]
    public void InvalidOperatorConfigurationDoesNotFallBackToAnotherPath(string kind)
    {
        using var fixture = new Fixture();
        var repositoryExecutable = fixture.Executable(fixture.Repository);
        var alias = Path.Combine(fixture.Root, "repository-alias");
        if (kind == "repository-alias") LinkDirectory(alias, fixture.Repository);
        if (kind == "not-executable" && !OperatingSystem.IsWindows())
            File.SetUnixFileMode(fixture.TrustedExecutable, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var configured = kind switch
        {
            "empty" => "", "relative" => "git", "missing" => Path.Combine(fixture.Root, "missing-git"),
            "directory" => fixture.Installation, "root" => Path.GetPathRoot(fixture.Root)!,
            "embedded-null" => fixture.TrustedExecutable + "\0ignored", "repository" => repositoryExecutable,
            "not-executable" => fixture.TrustedExecutable,
            _ => Path.Combine(alias, OperatingSystem.IsWindows() ? "git.exe" : "git")
        };
        Assert.ThrowsAny<IOException>(() => RepositoryGitExecutable.Resolve(fixture.Repository, fixture.Installation, configured));
    }

    [Fact]
    public void OperatorSelectedInstallationRetainsNativeParentTraversal()
    {
        using var fixture = new Fixture();
        var nested = Directory.CreateDirectory(Path.Combine(fixture.Installation, "nested")).FullName;
        var alias = Path.Combine(fixture.Root, "installation-alias");
        LinkDirectory(alias, nested);
        var configured = alias + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "git" +
            (OperatingSystem.IsWindows() ? ".exe" : "");
        var expected = OperatingSystem.IsWindows() ? fixture.Executable(fixture.Root) : fixture.TrustedExecutable;
        Assert.Equal(expected, RepositoryGitExecutable.Resolve(fixture.Repository, null, configured));
    }

    [Fact]
    public void EveryInspectedRepositoryIsExcludedForEachReader()
    {
        using var fixture = new Fixture();
        var other = Directory.CreateDirectory(Path.Combine(fixture.Root, "other-input-repository")).FullName;
        var otherExecutable = fixture.Executable(other);
        Assert.Throws<FileNotFoundException>(() => RepositoryGitExecutable.Resolve(fixture.Repository, other, null, [other]));
        Assert.Equal(fixture.TrustedExecutable, RepositoryGitExecutable.Resolve(fixture.Repository,
            other + Path.PathSeparator + fixture.Installation, null, [other]));
        Assert.ThrowsAny<IOException>(() => RepositoryGitExecutable.Resolve(fixture.Repository, fixture.Installation, otherExecutable, [other]));
        Assert.Equal(fixture.TrustedExecutable, RepositoryGitExecutable.Resolve(other, fixture.Installation, fixture.TrustedExecutable, [fixture.Repository]));
    }

    [Fact]
    public void RepositoryDirectoryLinkCannotChooseAHostInstallation()
    {
        using var fixture = new Fixture();
        var link = Path.Combine(fixture.Repository, "host-link");
        LinkDirectory(link, fixture.Installation);
        Assert.Throws<FileNotFoundException>(() => RepositoryGitExecutable.Resolve(fixture.Repository, link));
        Assert.Equal(fixture.TrustedExecutable, RepositoryGitExecutable.Resolve(fixture.Repository,
            link + Path.PathSeparator + fixture.Installation));
        Assert.ThrowsAny<IOException>(() => RepositoryGitExecutable.Resolve(fixture.Repository, fixture.Installation,
            Path.Combine(link, OperatingSystem.IsWindows() ? "git.exe" : "git")));
    }

    [Fact]
    public void HostDirectoryChainCannotDelegateInstallationChoiceToARepositoryLink()
    {
        using var fixture = new Fixture();
        var choice = Path.Combine(fixture.Repository, "installation-choice");
        var entry = Path.Combine(fixture.Root, "host-directory-chain");
        LinkDirectory(choice, fixture.Installation);
        LinkDirectory(entry, choice);
        Assert.Throws<FileNotFoundException>(() => RepositoryGitExecutable.Resolve(fixture.Repository, entry));
        Assert.Equal(fixture.TrustedExecutable, RepositoryGitExecutable.Resolve(fixture.Repository,
            entry + Path.PathSeparator + fixture.Installation));
        Assert.ThrowsAny<IOException>(() => RepositoryGitExecutable.Resolve(fixture.Repository, null,
            Path.Combine(entry, OperatingSystem.IsWindows() ? "git.exe" : "git")));
    }

    [Fact]
    public void HostInstallationSurvivesNativeLinkParentTraversal()
    {
        using var fixture = new Fixture();
        var nested = Directory.CreateDirectory(Path.Combine(fixture.Installation, "nested")).FullName;
        var alias = Path.Combine(fixture.Root, "installation-alias");
        LinkDirectory(alias, nested);
        var entry = alias + Path.DirectorySeparatorChar + "..";
        // Ordinary Windows paths normalize parents lexically; Unix resolves the link first.
        var expected = OperatingSystem.IsWindows() ? fixture.Executable(fixture.Root) : fixture.TrustedExecutable;
        Assert.Equal(expected, RepositoryGitExecutable.Resolve(fixture.Repository, entry));
    }

    [Fact]
    public void FileSystemRootDoesNotLoseItsContainmentSeparator()
    {
        using var fixture = new Fixture();
        var root = Path.GetPathRoot(fixture.Repository)!;
        Assert.Throws<FileNotFoundException>(() => RepositoryGitExecutable.Resolve(root, fixture.Installation));
    }

    [Fact]
    public void OrdinaryRelativeRepositoryRootRetainsTheHostInstallation()
    {
        using var fixture = new Fixture();
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, fixture.Repository);
        Assert.Equal(fixture.TrustedExecutable, RepositoryGitExecutable.Resolve(relative, fixture.Installation));
    }

    [Fact]
    public void SimilarSiblingPrefixIsNotTreatedAsInsideTheRepository()
    {
        using var fixture = new Fixture();
        var sibling = Directory.CreateDirectory(fixture.Repository + "-host-installation").FullName;
        var executable = fixture.Executable(sibling);
        Assert.Equal(executable, RepositoryGitExecutable.Resolve(fixture.Repository, sibling));
    }

    [Fact]
    public void UnusableEarlierEntryDoesNotOverrideTheNextHostInstallation()
    {
        using var fixture = new Fixture();
        var unusable = Directory.CreateDirectory(Path.Combine(fixture.Root, "unusable")).FullName;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(Path.Combine(unusable, "git.exe"));
        else
        {
            var file = fixture.Executable(unusable);
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        Assert.Equal(fixture.TrustedExecutable, RepositoryGitExecutable.Resolve(fixture.Repository,
            unusable + Path.PathSeparator + fixture.Installation));
    }

    [Fact]
    public void EmbeddedNullCannotReinterpretTheRepositoryOrSearchEntry()
    {
        using var fixture = new Fixture();
        Assert.Throws<ArgumentException>(() => RepositoryGitExecutable.Resolve(fixture.Repository + "\0ignored", fixture.Installation));
        Assert.Equal(fixture.TrustedExecutable, RepositoryGitExecutable.Resolve(fixture.Repository,
            fixture.Installation + "\0ignored" + Path.PathSeparator + fixture.Installation));
    }

    public static IEnumerable<object[]> NativeLinkCases => (OperatingSystem.IsWindows()
        ? new[] { "directory", "root", "dotdot" } : ["directory", "file", "root", "dotdot", "repository-entry-to-host"])
        .Select(kind => new object[] { kind });

    [Theory]
    [MemberData(nameof(NativeLinkCases))]
    public void PhysicalAliasesCannotSelectRepositoryExecutables(string kind)
    {
        using var fixture = new Fixture();
        var malicious = fixture.Executable(fixture.Repository);
        var alias = Path.Combine(fixture.Root, "alias");
        var root = fixture.Repository;
        string entry;
        if (kind == "repository-entry-to-host")
        {
            File.Delete(malicious);
            File.CreateSymbolicLink(malicious, fixture.TrustedExecutable);
            entry = fixture.Repository;
        }
        else if (kind == "file")
        {
            Directory.CreateDirectory(alias);
            File.CreateSymbolicLink(Path.Combine(alias, "git"), malicious);
            entry = alias;
        }
        else
        {
            LinkDirectory(alias, fixture.Repository);
            entry = alias;
            if (kind == "root") { root = alias; entry = fixture.Repository; }
            if (kind == "dotdot")
            {
                // The kernel resolves the symlink before '..', unlike lexical GetFullPath normalization.
                var child = Directory.CreateDirectory(Path.Combine(fixture.Repository, "nested")).FullName;
                var nestedAlias = Path.Combine(fixture.Root, "nested-alias");
                LinkDirectory(nestedAlias, child);
                entry = nestedAlias + Path.DirectorySeparatorChar + "..";
            }
        }
        Assert.Throws<FileNotFoundException>(() => RepositoryGitExecutable.Resolve(root, entry));
        Assert.Equal(fixture.TrustedExecutable, RepositoryGitExecutable.Resolve(root,
            entry + Path.PathSeparator + fixture.Installation));
    }

    private static void LinkDirectory(string alias, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(alias, target); return; }
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", alias, target }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(10000));
        Task.WaitAll(output, error);
        Assert.Equal(0, process.ExitCode);
        Assert.True(Directory.Exists(alias));
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "coglatas-git-selection-" + Guid.NewGuid().ToString("N"));
        internal string Repository { get; }
        internal string Installation { get; }
        internal string TrustedExecutable { get; }

        internal Fixture()
        {
            Repository = Directory.CreateDirectory(Path.Combine(Root, "repository")).FullName;
            Installation = Directory.CreateDirectory(Path.Combine(Root, "host installation with spaces")).FullName;
            TrustedExecutable = Executable(Installation);
        }

        internal string Executable(string directory)
        {
            var path = Path.Combine(directory, OperatingSystem.IsWindows() ? "git.exe" : "git");
            File.WriteAllText(path, "Synthetic selection fixture; never executed.");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return path;
        }

        public void Dispose()
        {
            // Unlink aliases while their targets still exist; never recurse through a fixture alias.
            RemoveDirectoryLinks(Root);
            // Git's immutable loose objects are read-only on Windows; clear only this owned fixture's files.
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }

        private static void RemoveDirectoryLinks(string parent)
        {
            foreach (var directory in Directory.EnumerateDirectories(parent))
                if (new DirectoryInfo(directory).LinkTarget is not null) Directory.Delete(directory);
                else RemoveDirectoryLinks(directory);
        }
    }
}
