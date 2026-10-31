using System.Globalization;
using System.Numerics;

namespace Coglatas.Domain.ProjectIde;

/// <summary>Local processing bounds. These defaults are provisional, not product capacity guarantees.</summary>
public sealed record ExactNumberLimits(int MaximumDigits = 4096, int MaximumScale = 4096)
{
    internal void Validate()
    {
        if (MaximumDigits is < 1 or > 100_000 || MaximumScale is < 0 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(ExactNumberLimits));
    }

    internal void Check(BigInteger value)
    {
        Validate();
        if (BigInteger.Abs(value).ToString(CultureInfo.InvariantCulture).Length > MaximumDigits)
            throw new OverflowException("Exact numeric processing bound exceeded.");
    }

    internal BigInteger ReadInteger(string text)
    {
        Validate();
        ArgumentNullException.ThrowIfNull(text);
        var start = text.StartsWith('-') ? 1 : 0;
        if (text.Length == start || text.Length - start > MaximumDigits ||
            text.AsSpan(start).ContainsAnyExceptInRange('0', '9'))
            throw new FormatException("Expected a bounded exact integer string.");
        return BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }
}

/// <summary>Finite base-ten input, with no decimal or binary floating point conversion.</summary>
public sealed record ExactDecimal
{
    public BigInteger Coefficient { get; }
    public int Scale { get; }
    public string CanonicalText { get; }

    private ExactDecimal(BigInteger coefficient, int scale)
    {
        Coefficient = coefficient;
        Scale = scale;
        var digits = BigInteger.Abs(coefficient).ToString(CultureInfo.InvariantCulture);
        var magnitude = scale == 0 ? digits : digits.Length > scale ? digits.Insert(digits.Length - scale, ".")
            : "0." + new string('0', scale - digits.Length) + digits;
        CanonicalText = coefficient.Sign < 0 ? "-" + magnitude : magnitude;
    }

    public static ExactDecimal Parse(string text, ExactNumberLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        limits ??= new();
        limits.Validate();
        var start = text.StartsWith('-') ? 1 : 0;
        var dot = text.IndexOf('.');
        var scale = dot < 0 ? 0 : text.Length - dot - 1;
        var digitCount = text.Length - start - (dot < 0 ? 0 : 1);
        if (digitCount < 1 || digitCount > limits.MaximumDigits || scale > limits.MaximumScale ||
            dot == start || (dot >= 0 && scale == 0))
            throw new FormatException("Expected a bounded finite decimal string.");
        var digits = dot < 0 ? text[start..] : text[start..dot] + text[(dot + 1)..];
        if (digits.AsSpan().ContainsAnyExceptInRange('0', '9'))
            throw new FormatException("Decimal strings do not accept exponents, whitespace or locale separators.");
        var coefficient = limits.ReadInteger((start == 1 ? "-" : "") + digits);
        if (coefficient.IsZero) return new(BigInteger.Zero, 0);
        while (scale > 0 && coefficient % 10 == 0) { coefficient /= 10; scale--; }
        return new(coefficient, scale);
    }

    public ExactRational ToRational(ExactNumberLimits? limits = null)
    {
        limits ??= new();
        limits.Validate();
        if (Scale > limits.MaximumScale || (Scale > 0 && Scale >= limits.MaximumDigits))
            throw new OverflowException("Decimal denominator exceeds the exact numeric processing bound.");
        return ExactRational.Create(Coefficient, BigInteger.Pow(10, Scale), limits);
    }

    public override string ToString() => CanonicalText;
}

/// <summary>Reduced exact value. Non-terminating decimal results remain rational.</summary>
public sealed record ExactRational
{
    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }
    private ExactRational(BigInteger numerator, BigInteger denominator) { Numerator = numerator; Denominator = denominator; }

    public static ExactRational Parse(string numerator, string denominator, ExactNumberLimits? limits = null)
    {
        limits ??= new();
        return Create(limits.ReadInteger(numerator), limits.ReadInteger(denominator), limits);
    }

    public static ExactRational Create(BigInteger numerator, BigInteger denominator, ExactNumberLimits? limits = null)
    {
        limits ??= new();
        limits.Check(numerator);
        limits.Check(denominator);
        if (denominator.IsZero) throw new DivideByZeroException("An exact rational requires a nonzero denominator.");
        if (denominator.Sign < 0) { numerator = -numerator; denominator = -denominator; }
        var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return new(numerator / gcd, denominator / gcd);
    }

    public ExactRational Add(ExactRational other, ExactNumberLimits? limits = null) =>
        Operate(other, limits, (a, b, c, d) => (a * d + c * b, b * d));
    public ExactRational Multiply(ExactRational other, ExactNumberLimits? limits = null) =>
        Operate(other, limits, (a, b, c, d) => (a * c, b * d));
    public ExactRational Divide(ExactRational other, ExactNumberLimits? limits = null) =>
        Operate(other, limits, (a, b, c, d) => (a * d, b * c));

    private ExactRational Operate(ExactRational other, ExactNumberLimits? limits,
        Func<BigInteger, BigInteger, BigInteger, BigInteger, (BigInteger, BigInteger)> operation)
    {
        ArgumentNullException.ThrowIfNull(other);
        limits ??= new();
        limits.Check(Numerator); limits.Check(Denominator);
        limits.Check(other.Numerator); limits.Check(other.Denominator);
        var (numerator, denominator) = operation(Numerator, Denominator, other.Numerator, other.Denominator);
        if (denominator.IsZero) throw new DivideByZeroException("Exact division by zero.");
        var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return Create(numerator / gcd, denominator / gcd, limits);
    }

    public int CompareTo(ExactRational other, ExactNumberLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(other);
        limits ??= new();
        limits.Check(Numerator); limits.Check(Denominator);
        limits.Check(other.Numerator); limits.Check(other.Denominator);
        return (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    }

    /// <summary>Explicit quantization only; retain the exact source and rounding decision as evidence.</summary>
    public QuantizedExactValue Quantize(int scale, ExactNumberLimits? limits = null)
    {
        limits ??= new();
        limits.Check(Numerator); limits.Check(Denominator);
        if (scale < 0 || scale > limits.MaximumScale || scale >= limits.MaximumDigits)
            throw new ArgumentOutOfRangeException(nameof(scale));
        var scaled = BigInteger.Abs(Numerator) * BigInteger.Pow(10, scale);
        var quotient = BigInteger.DivRem(scaled, Denominator, out var remainder);
        var comparison = (remainder * 2).CompareTo(Denominator);
        if (comparison > 0 || (comparison == 0 && !quotient.IsEven)) quotient++;
        if (Numerator.Sign < 0) quotient = -quotient;
        limits.Check(quotient);
        var digits = BigInteger.Abs(quotient).ToString(CultureInfo.InvariantCulture).PadLeft(scale + 1, '0');
        var text = scale == 0 ? digits : digits.Insert(digits.Length - scale, ".");
        var rounded = ExactDecimal.Parse((quotient.Sign < 0 ? "-" : "") + text, limits);
        return new(this, rounded, scale);
    }

    public override string ToString() => Numerator.ToString(CultureInfo.InvariantCulture) + "/" +
                                         Denominator.ToString(CultureInfo.InvariantCulture);
}

public sealed record QuantizedExactValue
{
    public ExactRational Original { get; }
    public ExactDecimal Rounded { get; }
    public int Scale { get; }
    public string RoundingMode => "coglatas.nearest-ties-to-even";
    internal QuantizedExactValue(ExactRational original, ExactDecimal rounded, int scale)
    { Original = original; Rounded = rounded; Scale = scale; }
}
