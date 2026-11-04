using System.Collections.ObjectModel;
using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

/// <summary>A declared dimension vector; zero powers are omitted and keys are namespaced stable symbols.</summary>
public sealed class Dimension : IEquatable<Dimension>
{
    public IReadOnlyDictionary<string, int> Powers { get; }
    public SourceJson Data { get; }

    public Dimension(SourceJson data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Value.ValueKind != JsonValueKind.Object) throw new FormatException("Dimension must be an object.");
        var powers = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var member in data.Value.EnumerateObject())
        {
            if (!member.Name.Contains('.') || member.Name.Any(char.IsWhiteSpace) ||
                member.Value.ValueKind != JsonValueKind.Number || !member.Value.TryGetInt32(out var power) || power == 0)
                throw new FormatException("Dimension requires namespaced keys and nonzero checked integer powers.");
            powers.Add(member.Name, power);
        }
        Data = data;
        Powers = new ReadOnlyDictionary<string, int>(powers);
    }

    public bool Equals(Dimension? other) => other is not null && Data.Equals(other.Data);
    public override bool Equals(object? obj) => obj is Dimension other && Equals(other);
    public override int GetHashCode() => Data.GetHashCode();
}

/// <summary>Declared Source unit and dimension. Registry binding is a later Compiler responsibility.</summary>
public sealed record Quantity
{
    public ExactDecimal Amount { get; }
    public string UnitId { get; }
    public Dimension Dimension { get; }
    public SourceJson Data { get; }

    public Quantity(SourceJson data, ExactNumberLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
        UnitId = SourceFields.Token(data.Value, "unitId");
        Dimension = new(SourceJson.FromElement(SourceFields.Object(data.Value, "dimension"), data.Limits));
        var amount = SourceFields.String(data.Value, "amount");
        Amount = ExactDecimal.Parse(amount, limits);
        if (amount != Amount.CanonicalText) throw new FormatException("Quantity amount must be a canonical decimal string.");
    }

    public void RequireCompatible(string unitId, Dimension dimension)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        if (!Dimension.Equals(dimension)) throw new FormatException("Quantity dimension mismatch.");
        if (UnitId != unitId) throw new UnsupportedSourceException("Unit conversion requires an explicit versioned conversion entry.");
    }
}
