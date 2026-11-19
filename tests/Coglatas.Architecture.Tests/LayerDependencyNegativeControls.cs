using System.Text.RegularExpressions;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Coglatas.Architecture.Tests;

/// <summary>
/// Demonstrates that ArchUnitNET detects synthetic forbidden references in
/// test-only fixtures. Production providers and AssertNoDependency in
/// LayerDependencyTests are exercised separately, not by this control.
/// </summary>
public sealed class LayerDependencyNegativeControls
{
    [Fact]
    public void LayerRulesRejectSyntheticForbiddenReferences()
    {
        // Keep each synthetic reference in the loaded binary; these fixtures
        // exercise an inline rule, not the production rule provider.
        Assert.Null(new DomainLikeFixture().Application);
        Assert.Null(new DomainLikeFixture().Infrastructure);
        Assert.Null(new DomainLikeFixture().Web);
        Assert.Null(new ApplicationLikeFixture().Infrastructure);
        Assert.Null(new ApplicationLikeFixture().Web);
        Assert.Null(new InfrastructureLikeFixture().Web);

        var architecture = new ArchLoader()
            .LoadAssemblies(typeof(LayerDependencyNegativeControls).Assembly)
            .Build();

        var forbidden = new (System.Type Source, System.Type Target)[]
        {
            (typeof(DomainLikeFixture), typeof(ApplicationMarker)),
            (typeof(DomainLikeFixture), typeof(InfrastructureMarker)),
            (typeof(DomainLikeFixture), typeof(WebMarker)),
            (typeof(ApplicationLikeFixture), typeof(InfrastructureMarker)),
            (typeof(ApplicationLikeFixture), typeof(WebMarker)),
            (typeof(InfrastructureLikeFixture), typeof(WebMarker)),
        };

        foreach (var (source, target) in forbidden)
        {
            var sourcePattern = "^" + Regex.Escape(source.FullName!) + "$";
            var targetPattern = "^" + Regex.Escape(target.FullName!) + "$";
            var sourceTypes = Types().That().HaveFullNameMatching(sourcePattern);
            var targetTypes = Types().That().HaveFullNameMatching(targetPattern);
            Assert.Single(sourceTypes.GetObjects(architecture));
            Assert.Single(targetTypes.GetObjects(architecture));

            var error = Assert.Throws<FailedArchRuleException>(() =>
                Types().That().Are(sourceTypes).Should().NotDependOnAny(targetTypes).Check(architecture));
            Assert.Contains(source.Name, error.Message, StringComparison.Ordinal);
            Assert.Contains(target.Name, error.Message, StringComparison.Ordinal);
        }
    }

    private sealed class DomainLikeFixture
    {
        public ApplicationMarker? Application => null;
        public InfrastructureMarker? Infrastructure => null;
        public WebMarker? Web => null;
    }

    private sealed class ApplicationLikeFixture
    {
        public InfrastructureMarker? Infrastructure => null;
        public WebMarker? Web => null;
    }

    private sealed class InfrastructureLikeFixture
    {
        public WebMarker? Web => null;
    }

    private sealed class ApplicationMarker { }
    private sealed class InfrastructureMarker { }
    private sealed class WebMarker { }
}
