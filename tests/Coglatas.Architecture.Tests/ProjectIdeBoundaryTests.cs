using System.Reflection;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Coglatas.Architecture.Tests;

public sealed class ProjectIdeBoundaryTests
{
    private const string EnvironmentDependencyPattern = "^System\\.Environment($|[.+])";

    [Fact]
    public void ShadowSeamCannotObtainExistingMutationOrMergeServices()
    {
        var application = Assembly.Load("Coglatas.Application");
        var architecture = new ArchLoader().LoadAssemblies(application, Assembly.Load("Coglatas.Domain"),
            Assembly.Load("Coglatas.Infrastructure"), Assembly.Load("Coglatas.Web")).Build();
        var seam = Types().That().ResideInNamespace("Coglatas.Application.ProjectIde.Evaluations");
        Assert.Contains(application.GetTypes(), type => type.Namespace == "Coglatas.Application.ProjectIde.Evaluations");
        foreach (var pattern in new[]
                 {
                     "^Coglatas\\.Application\\.(?!ProjectIde[.+])",
                     "^Coglatas\\.(Infrastructure|Web|UI)[.+]",
                     "^(Avalonia|Microsoft\\.EntityFrameworkCore|Microsoft\\.AspNetCore|Npgsql)[.+]"
                 })
            Types().That().Are(seam).Should().NotDependOnAny(Types().That().HaveFullNameMatching(pattern))
                .Because("The non-authoritative adapter may evaluate and record, but cannot mutate Source, permissions or Merge.")
                .Check(architecture);
    }

    [Fact]
    public void SecurityEvaluationEngineCannotDependOnStateOrTransportServices()
    {
        var application = Assembly.Load("Coglatas.Application");
        var architecture = new ArchLoader().LoadAssemblies(application, Assembly.Load("Coglatas.Domain"),
            Assembly.Load("Coglatas.Infrastructure"), Assembly.Load("Coglatas.Web"), Assembly.Load("Coglatas.UI.Core"),
            typeof(Stream).Assembly, typeof(HttpClient).Assembly).Build();
        var engine = Types().That().ResideInNamespace("Coglatas.Application.ProjectIde.Security");
        Assert.Contains(application.GetTypes(), type => type.Namespace == "Coglatas.Application.ProjectIde.Security");
        foreach (var prefix in new[] { "Avalonia", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore",
                     "Microsoft.Extensions", "Npgsql", "Coglatas.Infrastructure", "Coglatas.Web", "Coglatas.UI",
                     "System.IO", "System.Net", "System.Data", "System.Environment" })
        {
            var pattern = prefix == "System.Environment" ? EnvironmentDependencyPattern :
                "^" + System.Text.RegularExpressions.Regex.Escape(prefix) + "[.+]";
            Types().That().Are(engine).Should().NotDependOnAny(Types().That().HaveFullNameMatching(pattern))
                .Because("Foundation evaluation must consume explicit immutable evidence without state, transport, or platform I/O.")
                .Check(architecture);
        }
        Types().That().Are(engine).Should().NotDependOnAny(Types().That().HaveFullNameMatching(
                "^Coglatas\\.Application\\.(?!ProjectIde\\.Security[.+])"))
            .Because("Rules cannot obtain ambient repositories, authorization mutations, or Merge services.")
            .Check(architecture);
    }

    [Fact]
    public void SecurityFoundationContractsRemainInsideThePureSourceBoundary()
    {
        var domain = Assembly.Load("Coglatas.Domain");
        var required = new[]
        {
            "SecurityDecisionOutcome", "SecurityEvaluationStatus", "SecurityEnforcementMode", "SecuritySubjectRef",
            "SecurityOperationRef", "SecurityResourceRef", "SecurityPolicySnapshot", "SecurityCompilerProvenance",
            "SecurityEvaluationRequest", "SecurityDecision", "SecurityRuleResult", "SecurityAnalysisSummary",
            "SecurityPolicyEvidence", "SecurityEvaluationEvidence", "SecurityBinding"
        };
        foreach (var name in required)
        {
            var type = domain.GetType("Coglatas.Domain.ProjectIde." + name, throwOnError: true)!;
            Assert.Equal("Coglatas.Domain.ProjectIde", type.Namespace);
            Assert.All(type.GetProperties(BindingFlags.Public | BindingFlags.Instance), property =>
                Assert.Null(property.GetSetMethod(nonPublic: true)));
        }
        // The executable pure-core rule inspects dependencies of all these
        // types, including method bodies, not just public property signatures.
        ProductSourceCoreUsesOnlyPureBclAndOwnedTypes();
    }

    [Fact]
    public void BoundaryRuleDetectsAnIntentionallyForbiddenPlatformDependency()
    {
        var fixtureType = typeof(ForbiddenPlatformFixture);
        Assert.Null(new ForbiddenPlatformFixture().Stream);
        var architecture = new ArchLoader().LoadAssemblies(typeof(ProjectIdeBoundaryTests).Assembly, typeof(Stream).Assembly).Build();
        var fixture = Types().That().HaveFullNameMatching(System.Text.RegularExpressions.Regex.Escape(fixtureType.FullName!));
        var forbidden = Types().That().HaveFullNameMatching("^System\\.IO[.+]");
        var rule = Types().That().Are(fixture).Should().NotDependOnAny(forbidden);
        Assert.ThrowsAny<Exception>(() => rule.Check(architecture));
    }

    [Fact]
    public void BoundaryRuleDetectsAnIntentionallyForbiddenEnvironmentDependency()
    {
        var fixtureType = typeof(ForbiddenEnvironmentFixture);
        _ = new ForbiddenEnvironmentFixture().Read();
        var architecture = new ArchLoader().LoadAssemblies(typeof(ProjectIdeBoundaryTests).Assembly, typeof(Environment).Assembly).Build();
        var fixture = Types().That().HaveFullNameMatching(System.Text.RegularExpressions.Regex.Escape(fixtureType.FullName!));
        var forbidden = Types().That().HaveFullNameMatching(EnvironmentDependencyPattern);
        var rule = Types().That().Are(fixture).Should().NotDependOnAny(forbidden);
        Assert.ThrowsAny<Exception>(() => rule.Check(architecture));
    }

    [Fact]
    public void ProductSourceCoreUsesOnlyPureBclAndOwnedTypes()
    {
        var domain = Assembly.Load("Coglatas.Domain");
        var architecture = new ArchLoader().LoadAssemblies(domain, typeof(Stream).Assembly,
            typeof(HttpClient).Assembly, Assembly.Load("Coglatas.Application"),
            Assembly.Load("Coglatas.Infrastructure"), Assembly.Load("Coglatas.Web"), Assembly.Load("Coglatas.UI.Core")).Build();
        var core = Types().That().ResideInNamespace("Coglatas.Domain.ProjectIde").As("Project IDE Slice 1");
        Assert.Contains(domain.GetTypes(), type => type.Namespace == "Coglatas.Domain.ProjectIde");
        foreach (var prefix in new[] { "Avalonia", "Dock", "Microsoft.Msagl", "Msagl", "Microsoft.Kiota",
                     "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Coglatas.UI", "Coglatas.Application",
                     "Coglatas.Infrastructure", "Coglatas.Web", "System.IO", "System.Net", "System.Environment" })
        {
            var pattern = prefix == "System.Environment" ? EnvironmentDependencyPattern :
                "^" + System.Text.RegularExpressions.Regex.Escape(prefix) + "[.+]";
            Types().That().Are(core).Should().NotDependOnAny(Types().That().HaveFullNameMatching(pattern))
                .Because("Source primitives must remain independent of UI, transport, persistence and platform I/O.")
                .Check(architecture);
        }
    }

    private sealed class ForbiddenPlatformFixture
    {
        public Stream? Stream => null;
    }

    private sealed class ForbiddenEnvironmentFixture
    {
        public string? Read() => Environment.GetEnvironmentVariable("COGLATAS_SLICE1_BOUNDARY_FIXTURE");
    }
}
