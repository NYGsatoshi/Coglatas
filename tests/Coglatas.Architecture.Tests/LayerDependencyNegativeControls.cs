using System.Text.RegularExpressions;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Coglatas.Architecture.Tests;

/// <summary>
/// Negative controls for the ArchUnit dependency oracle; the production six
/// directional rules are still executed by LayerDependencyTests.
/// </summary>
public sealed class LayerDependencyNegativeControls
{
    [Fact]
    public void LayerRulesRejectSyntheticForbiddenReferences()
    {
        var architecture = new ArchLoader()
            .LoadAssemblies(typeof(LayerDependencyNegativeControls).Assembly)
            .Build();

        var forbidden = new (Type Source, Type Target)[]
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
            var sourceTypes = Types().That().HaveFullNameMatching(Regex.Escape(source.FullName!));
            var targetTypes = Types().That().HaveFullNameMatching(Regex.Escape(target.FullName!));
            Assert.ThrowsAny<Exception>(() =>
                Types().That().Are(sourceTypes).Should().NotDependOnAny(targetTypes).Check(architecture));
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
