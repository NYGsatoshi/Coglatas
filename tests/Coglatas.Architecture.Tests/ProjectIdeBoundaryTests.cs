using System.Reflection;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Coglatas.Architecture.Tests;

public sealed class ProjectIdeBoundaryTests
{
    [Fact]
    public void BoundaryRuleDetectsAnIntentionallyForbiddenPlatformDependency()
    {
        var architecture = new ArchLoader().LoadAssemblies(typeof(ProjectIdeBoundaryTests).Assembly, typeof(System.IO.Stream).Assembly).Build();
        var fixture = Types().That().HaveFullNameMatching(".*ForbiddenPlatformFixture.*");
        var forbidden = Types().That().HaveFullNameMatching("^System\\.IO[.+]");
        var rule = Types().That().Are(fixture).Should().NotDependOnAny(forbidden);
        Assert.ThrowsAny<Exception>(() => rule.Check(architecture));
    }

    [Fact]
    public void ProductSourceCoreUsesOnlyPureBclAndOwnedTypes()
    {
        var domain = Assembly.Load("Coglatas.Domain");
        var architecture = new ArchLoader().LoadAssemblies(domain, typeof(System.IO.Stream).Assembly,
            typeof(System.Net.Http.HttpClient).Assembly, Assembly.Load("Coglatas.Application"),
            Assembly.Load("Coglatas.Infrastructure"), Assembly.Load("Coglatas.Web"), Assembly.Load("Coglatas.UI.Core")).Build();
        var core = Types().That().ResideInNamespace("Coglatas.Domain.ProjectIde").As("Project IDE Slice 1");
        Assert.Contains(domain.GetTypes(), type => type.Namespace == "Coglatas.Domain.ProjectIde");
        foreach (var prefix in new[] { "Avalonia", "Dock", "Microsoft.Msagl", "Msagl", "Microsoft.Kiota",
                     "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Coglatas.UI", "Coglatas.Application",
                     "Coglatas.Infrastructure", "Coglatas.Web", "System.IO", "System.Net", "System.Environment" })
            Types().That().Are(core).Should().NotDependOnAny(Types().That().HaveFullNameMatching("^" + System.Text.RegularExpressions.Regex.Escape(prefix) + "[.+]"))
                .Because("Source primitives must remain independent of UI, transport, persistence and platform I/O.")
                .Check(architecture);
    }

    private sealed class ForbiddenPlatformFixture
    {
        public System.IO.Stream? Stream { get; init; }
    }
}
