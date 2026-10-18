using Coglatas.Application.Files;

namespace Coglatas.Tests.Files;

[Trait("Portability", "CrossPlatform")]
public sealed class FileNameSanitizerTests
{
    [Theory]
    [InlineData("..\\secret.txt", "secret.txt")]
    [InlineData("../secret.txt", "secret.txt")]
    [InlineData("folder/secret.txt", "secret.txt")]
    [InlineData("folder\\secret.txt", "secret.txt")]
    [InlineData(" \t ", "upload")]
    [InlineData("safe\u0001name.txt", "safe_name.txt")]
    [InlineData("C:\\temp\\secret.txt", "secret.txt")]
    [InlineData("../\u7814\u7a76\u8cc7\u6599\ud83d\udcc4.txt", "\u7814\u7a76\u8cc7\u6599\ud83d\udcc4.txt")]
    [InlineData("folder\\\u8a08\u753b\0\u6848.txt", "\u8a08\u753b_\u6848.txt")]
    [InlineData("cafe\u0301.txt", "cafe\u0301.txt")]
    public void SanitizeOriginalFileNameReturnsSafeDisplayName(string originalFileName, string expected)
    {
        var sanitized = FileNameSanitizer.SanitizeOriginalFileName(originalFileName);

        Assert.Equal(expected, sanitized);
        Assert.DoesNotContain("..", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("/", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain(sanitized, char.IsControl);
    }

    [Fact]
    public void SanitizeOriginalFileNameBoundsLongUnicodeDisplayNames()
    {
        var sanitized = FileNameSanitizer.SanitizeOriginalFileName($"../{new string('\u7814', 300)}.txt");

        Assert.Equal(new string('\u7814', 260), sanitized);
    }

    [Theory]
    [InlineData(258, true)]
    [InlineData(259, false)]
    public void SanitizeOriginalFileNameDoesNotSplitASupplementaryUnicodeCharacter(int prefixLength, bool retainsSupplementaryCharacter)
    {
        var prefix = new string('\u7814', prefixLength);
        const string supplementaryCharacter = "\ud83d\udcc4";

        var sanitized = FileNameSanitizer.SanitizeOriginalFileName($"{prefix}{supplementaryCharacter}.txt");

        Assert.Equal(prefix + (retainsSupplementaryCharacter ? supplementaryCharacter : string.Empty), sanitized);
        Assert.InRange(sanitized.Length, 1, 260);
    }
}
