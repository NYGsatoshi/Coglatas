using System.Globalization;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class ExactNumberTests
{
    [Theory]
    [InlineData("00012.3400", "12.34")]
    [InlineData("-0.000", "0")]
    [InlineData("-001.0200", "-1.02")]
    [InlineData("0.000000000000000000000000000001", "0.000000000000000000000000000001")]
    [InlineData("123456789012345678901234567890.12345678901234567890123456789", "123456789012345678901234567890.12345678901234567890123456789")]
    public void DecimalCanonicalizationIsExact(string input, string expected)
    {
        var value = ExactDecimal.Parse(input);
        Assert.Equal(expected, value.CanonicalText);
        Assert.Equal(value, ExactDecimal.Parse(value.CanonicalText));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData(".5")]
    [InlineData("1.")]
    [InlineData("+1")]
    [InlineData("1e3")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1,5")]
    [InlineData(" 1")]
    [InlineData("1\n")]
    [InlineData("1..2")]
    [InlineData("１２")]
    public void DecimalRejectsNoncontractInputs(string input) => Assert.Throws<FormatException>(() => ExactDecimal.Parse(input));

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("ja-JP")]
    [InlineData("ar-SA")]
    [InlineData("tr-TR")]
    public void NumericResultsDoNotDependOnCulture(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal("3/10", ExactDecimal.Parse("0.1").ToRational().Add(ExactDecimal.Parse("0.2").ToRational()).ToString());
            Assert.Equal("-4/3", ExactRational.Parse("8", "-6").ToString());
            Assert.Equal("0.12", ExactRational.Parse("1", "8").Quantize(2).Rounded.CanonicalText);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void ArithmeticKeepsRepeatingFractionsAndExactComparisons()
    {
        var third = ExactDecimal.Parse("1").ToRational().Divide(ExactDecimal.Parse("3").ToRational());
        Assert.Equal("1/3", third.ToString());
        Assert.Equal("1/1", third.Add(third).Add(third).ToString());
        Assert.Equal("-2/1", third.Multiply(ExactRational.Parse("-6", "1")).ToString());
        Assert.True(third.CompareTo(ExactDecimal.Parse("0.333333333333333333333333333333").ToRational()) > 0);
        Assert.Throws<DivideByZeroException>(() => third.Divide(ExactRational.Parse("0", "1")));
        Assert.Throws<DivideByZeroException>(() => ExactRational.Parse("1", "0"));
    }

    [Theory]
    [InlineData("1", "8", 2, "0.12")]
    [InlineData("3", "8", 2, "0.38")]
    [InlineData("-1", "8", 2, "-0.12")]
    [InlineData("-3", "8", 2, "-0.38")]
    [InlineData("5", "2", 0, "2")]
    [InlineData("7", "2", 0, "4")]
    [InlineData("-1", "2", 0, "0")]
    [InlineData("1", "3", 6, "0.333333")]
    public void QuantizationIsExplicitAndKeepsEvidence(string numerator, string denominator, int scale, string expected)
    {
        var exact = ExactRational.Parse(numerator, denominator);
        var evidence = exact.Quantize(scale);
        Assert.Same(exact, evidence.Original);
        Assert.Equal(expected, evidence.Rounded.CanonicalText);
        Assert.Equal(scale, evidence.Scale);
        Assert.Equal("coglatas.nearest-ties-to-even", evidence.RoundingMode);
    }

    [Fact]
    public void BoundsAreCheckedWithoutWraparoundOrTruncation()
    {
        var limits = new ExactNumberLimits(3, 2);
        Assert.Equal("999", ExactDecimal.Parse("999", limits).CanonicalText);
        Assert.Throws<FormatException>(() => ExactDecimal.Parse("1000", limits));
        Assert.Throws<FormatException>(() => ExactDecimal.Parse("0.001", limits));
        Assert.Throws<FormatException>(() => ExactRational.Parse("1000", "1", limits));
        Assert.Throws<OverflowException>(() => ExactRational.Parse("999", "1").Multiply(ExactRational.Parse("2", "1"), limits));
        Assert.Equal("1/1", ExactRational.Parse("99", "97").Multiply(ExactRational.Parse("97", "99"), limits).ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => ExactDecimal.Parse("1", new(0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExactRational.Parse("1", "1").Quantize(3, limits));
    }
}
