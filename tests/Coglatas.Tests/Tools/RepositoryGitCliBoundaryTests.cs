using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Coglatas.SecurityArchitecture;

namespace Coglatas.Tests.Tools;

[Trait("Portability", "CrossPlatform")]
public sealed class RepositoryGitCliBoundaryTests
{
    public static IEnumerable<object[]> Cases => new[] { "cross-repository" }
        .Concat(!OperatingSystem.IsWindows() ? new[] { "host-file-link-chain" } : [])
        .Concat(OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("CI") == "true"
            ? new[] { "repository-file-link", "host-file-link-chain", "host-multicall-link" } : []).Select(kind => new object[] { kind });

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TraceabilityUsesHostGitAcrossBothInspectedRoots(string kind)
    {
        using var fixture = new Fixture();
        const string statement = "Synthetic scope must reject a foreign caller.";
        const string specification = "<a id=\"synthetic-authority\"></a>\n" + statement + "\n";
        const string verifierSource = "namespace Synthetic.Tests;\npublic sealed class ScopeTests\n{\n    public void RejectForeignCaller() { }\n}\n";
        var git = RepositoryGitExecutable.Resolve(fixture.Specification, Environment.GetEnvironmentVariable("PATH"));
        var specificationSha = await fixture.Commit(git, fixture.Specification, "synthetic/authority.md", specification);
        var candidateSha = await fixture.Commit(git, fixture.Implementation, "synthetic/ScopeTests.cs", verifierSource);
        const string id = "SPEC-AUTH-SYNTHETIC-001";
        var contract = new FlowContract("SEC-ARCH-SYNTHETIC-SCOPE", [id], "synthetic-owner",
            ContractType.Api, "synthetic-caller", "synthetic-service", "synthetic-http", "synthetic-alpha",
            "read", "synthetic-alpha", "synthetic-resource", "synthetic-boundary", AccessDecision.Deny,
            "synthetic-deny-rule", [EvidenceClass.Runtime], ActivationState.Active, ["SYNTHETIC-STRIDE-001"],
            Api: new("/synthetic/resource", "GET", false, "synthetic-policy"));
        var registry = new SpecRegistryDocument(1, 1, specificationSha,
            [new(id, [new(1, SpecStatus.Active,
                new(specificationSha, "synthetic/authority.md", "synthetic-authority", SpecDigest.Text(specification)),
                statement, SpecDigest.Text(statement), "synthetic-owner", SpecSeverity.Advisory,
                [SpecVerificationClass.ContractTest], "Synthetic fixture only; no product assurance.", [], "synthetic-review-pending")])]);
        var manifest = new SpecTraceabilityDocument(1, 1, SpecDigest.Document(registry),
            [new(id, 1, contract.ContractId, SpecDigest.Document(contract),
                [new("Synthetic.Scope.Verifier", "1.0", SpecVerificationClass.ContractTest,
                    new(candidateSha, "synthetic/ScopeTests.cs", null, SpecDigest.Text(verifierSource)),
                    "Synthetic.Tests.ScopeTests.RejectForeignCaller", ["Synthetic.Scope.Execution"])])]);
        var registryPath = await fixture.WriteJson("registry.json", registry);
        var manifestPath = await fixture.WriteJson("manifest.json", manifest);
        var contractsPath = await fixture.WriteJson("contracts.json", new ContractDocument(1, [contract]));
        string[] arguments = ["traceability-check", registryPath, manifestPath, contractsPath,
            fixture.Specification, fixture.Implementation, specificationSha, candidateSha, "2026-10-10T00:00:00Z"];
        var hostPath = Path.GetDirectoryName(git)!;
        var control = await fixture.Cli(arguments, hostPath);
        Assert.Equal(0, control.ExitCode);
        Assert.False(File.Exists(fixture.Marker));

        var hostile = kind == "cross-repository" ? fixture.Implementation : fixture.Specification;
        if (kind == "host-multicall-link") hostile = await fixture.AddMulticallInstallation(git);
        else if (kind == "host-file-link-chain")
        {
            var target = Directory.CreateDirectory(Path.Combine(fixture.Root, "host-target")).FullName;
            hostile = Directory.CreateDirectory(Path.Combine(fixture.Root, "host-entry")).FullName;
            await fixture.AddMarkerExecutable(target, false);
            var name = OperatingSystem.IsWindows() ? "git.exe" : "git";
            var choice = Path.Combine(fixture.Implementation, "executable-choice");
            File.CreateSymbolicLink(choice, Path.Combine(target, name));
            File.CreateSymbolicLink(Path.Combine(hostile, name), choice);
        }
        else await fixture.AddMarkerExecutable(hostile, kind == "repository-file-link");
        var result = await fixture.Cli(arguments, hostile + Path.PathSeparator + hostPath);
        Assert.False(File.Exists(fixture.Marker));
        Assert.Equal(0, result.ExitCode);
        var report = ContractJson.Read<SpecValidationResult>(result.Output);
        Assert.True(report.Valid);
        Assert.False(report.NormativeReady);
        Assert.DoesNotContain(statement, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Root, result.Output, StringComparison.Ordinal);
        if (kind == "host-multicall-link")
        {
            var configured = await fixture.Cli(arguments, hostPath, Path.Combine(hostile, "git.exe"));
            Assert.Equal(0, configured.ExitCode);
            Assert.True(ContractJson.Read<SpecValidationResult>(configured.Output).Valid);
            Assert.False(File.Exists(fixture.Marker));
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "coglatas-git-cli-boundary-" + Guid.NewGuid().ToString("N"));
        public string Specification { get; }
        public string Implementation { get; }
        public string Marker => Path.Combine(Root, "execution-marker.txt");
        private string Hooks => Path.Combine(Root, "empty-hooks");

        public Fixture()
        {
            Specification = Directory.CreateDirectory(Path.Combine(Root, "specification")).FullName;
            Implementation = Directory.CreateDirectory(Path.Combine(Root, "implementation")).FullName;
            Directory.CreateDirectory(Hooks);
        }

        public async Task<string> Commit(string git, string repository, string file, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repository, file))!);
            await File.WriteAllTextAsync(Path.Combine(repository, file), text, new UTF8Encoding(false));
            await Git(git, repository, "init", "--quiet", "--object-format=sha1");
            await Git(git, repository, "add", file);
            await Git(git, repository, "-c", "core.hooksPath=" + Hooks, "-c", "user.name=Synthetic Fixture",
                "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "Synthetic CLI fixture");
            return (await Git(git, repository, "rev-parse", "HEAD")).Trim();
        }

        public async Task<string> WriteJson<T>(string name, T value)
        {
            var file = Path.Combine(Root, name);
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(value, ContractJson.Options));
            return file;
        }

        public async Task AddMarkerExecutable(string directory, bool link)
        {
            if (OperatingSystem.IsWindows())
            {
                var node = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                    .Select(entry => entry.Trim('"')).Where(Path.IsPathFullyQualified)
                    .Select(entry => Path.Combine(entry, "node.exe")).First(File.Exists);
                var executable = Path.Combine(directory, "git.exe");
                if (link) File.CreateSymbolicLink(executable, node);
                else File.Copy(node, executable);
                const string script = "require('node:fs').writeFileSync(process.env.COGLATAS_SYNTHETIC_EXEC_MARKER,'SYNTHETIC_EXECUTION_MARKER\\n');process.stdout.write('SYNTHETIC_EXECUTION_MARKER\\n');\n";
                await File.WriteAllTextAsync(Path.Combine(Specification, "cat-file"), script);
                await File.WriteAllTextAsync(Path.Combine(Implementation, "cat-file"), script);
            }
            else
            {
                var executable = Path.Combine(directory, "git");
                await File.WriteAllTextAsync(executable,
                    "#!/bin/sh\nprintf 'SYNTHETIC_EXECUTION_MARKER\\n' > \"$COGLATAS_SYNTHETIC_EXEC_MARKER\"\nprintf 'SYNTHETIC_EXECUTION_MARKER\\n'\n");
                File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        public async Task<string> AddMulticallInstallation(string git)
        {
            var directory = Directory.CreateDirectory(Path.Combine(Root, "host-multicall")).FullName;
            var node = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Select(entry => entry.Trim('"')).Where(Path.IsPathFullyQualified)
                .Select(entry => Path.Combine(entry, "node.exe")).First(File.Exists);
            var target = Path.Combine(directory, "multicall.exe");
            File.Copy(node, target);
            File.CreateSymbolicLink(Path.Combine(directory, "git.exe"), target);
            var script = "if(require('node:path').basename(process.argv0).toLowerCase()!=='git.exe')process.exit(41);" +
                "const r=require('node:child_process').spawnSync(" + JsonSerializer.Serialize(git) +
                ",['cat-file',...process.argv.slice(2)],{env:process.env});" +
                "if(r.stdout)process.stdout.write(r.stdout);process.exit(r.status??1);\n";
            await File.WriteAllTextAsync(Path.Combine(Specification, "cat-file"), script);
            await File.WriteAllTextAsync(Path.Combine(Implementation, "cat-file"), script);
            return directory;
        }

        public Task<(int ExitCode, string Output)> Cli(string[] arguments, string search, string? trustedExecutable = null)
        {
            var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Path.GetFullPath(Path.Combine(
                RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
            Assert.True(Path.IsPathFullyQualified(host) && File.Exists(host));
            var runtime = Path.Combine(AppContext.BaseDirectory, "Coglatas.Tests.runtimeconfig.json");
            Assert.True(File.Exists(runtime));
            var info = new ProcessStartInfo(host)
            {
                WorkingDirectory = Root, UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true
            };
            info.ArgumentList.Add("exec");
            info.ArgumentList.Add("--runtimeconfig");
            info.ArgumentList.Add(runtime);
            info.ArgumentList.Add(typeof(SecurityArchitectureCli).Assembly.Location);
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            // The hostile fixture's subprocess receives no build, account or GitHub credentials.
            info.Environment.Clear();
            foreach (var key in new[] { "SystemRoot", "WINDIR", "TEMP", "TMP", "DOTNET_ROOT" })
                if (Environment.GetEnvironmentVariable(key) is { } value) info.Environment[key] = value;
            info.Environment["PATH"] = search;
            info.Environment["COGLATAS_SYNTHETIC_EXEC_MARKER"] = Marker;
            if (trustedExecutable is not null)
                info.Environment[RepositoryGitExecutable.TrustedExecutableEnvironmentVariable] = trustedExecutable;
            return Execute(info);
        }

        private static async Task<string> Git(string executable, string directory, params string[] arguments)
        {
            var info = new ProcessStartInfo(executable)
            {
                WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true
            };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            var result = await Execute(info);
            Assert.Equal(0, result.ExitCode);
            return result.Output;
        }

        private static async Task<(int ExitCode, string Output)> Execute(ProcessStartInfo info)
        {
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
            finally
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                await error;
            }
            return (process.ExitCode, await output);
        }

        public void Dispose()
        {
            var prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(Root);
            Assert.StartsWith(prefix, resolved, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            Assert.StartsWith("coglatas-git-cli-boundary-", Path.GetFileName(resolved));
            Assert.Null(new DirectoryInfo(resolved).LinkTarget);
            foreach (var file in Directory.EnumerateFiles(resolved, "*", SearchOption.AllDirectories))
                if (new FileInfo(file).LinkTarget is not null) File.Delete(file);
                else File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(resolved, recursive: true);
        }
    }
}
