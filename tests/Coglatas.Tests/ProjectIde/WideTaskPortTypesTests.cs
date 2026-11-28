using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class WideTaskPortTypesTests
{
    [Fact]
    public void BothProjectionsUseTheDeclaredTypeForEachExactPortIdentity()
    {
        var original = Source();
        var ports = Enumerable.Range(8, 128).Reverse().Select(number => new
        {
            portId = Id(number),
            direction = "coglatas.input",
            type = Obj(new
            {
                kind = number % 2 == 0 ? "coglatas.text" : "coglatas.boolean",
                schemaVersion = 1,
                nullable = false,
                acceptsUnknown = true
            }),
            minimumConnections = 0,
            maximumConnections = 1
        }).ToArray();
        var task = Obj(new
        {
            entityId = Id(7),
            documentId = Id(4),
            kind = "coglatas.task",
            schemaVersion = 1,
            payload = new
            {
                duration = new
                {
                    type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false },
                    state = "coglatas.known",
                    value = "1"
                },
                ports
            }
        });
        var source = ProjectSource.Create(original.Context, original.Documents, Json(new JsonArray(task)));
        var input = Input(source: source);
        var model = ProjectSemanticBinder.Bind(input);
        var inspection = new CompilerSemanticInspection(model);
        var binding = CompilerBindingClosure.Build(model, input.InputDigest);
        var index = CompilerSemanticIndex.Build(binding, input.InputDigest);
        var inspected = inspection.Symbols.Where(item => item.Symbol.PortId is not null).ToDictionary(item => item.Symbol.PortId!.Value);

        Assert.Equal(ports.Length, inspected.Count);
        Assert.Equal(ports.Length, index.Symbols.Count(item => item.Symbol.PortId is not null));
        foreach (var sourcePort in ports)
        {
            var expected = Json(sourcePort.type);
            var port = inspected[PortId.Parse(sourcePort.portId)];
            Assert.Same(port, inspection.Inspect(port.Symbol.Location));
            Assert.Equal(expected, port.DeclaredType);
            var indexed = Assert.IsType<CompilerIndexedSymbol>(index.Inspect(port.Symbol.Location, input.InputDigest));
            Assert.Equal(expected, indexed.DeclaredType);
        }
    }
}
