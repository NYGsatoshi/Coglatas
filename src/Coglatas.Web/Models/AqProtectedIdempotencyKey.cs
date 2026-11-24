using Coglatas.Application.Common;

namespace Coglatas.Web.Models;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class RequiresAqProtectedIdempotencyKeyAttribute : Attribute;

internal static class AqProtectedIdempotencyKey
{
    public const string Name = "Idempotency-Key";
    public const int MinimumLength = 8;
    public const int MaximumLength = 128;
    private const string Target = "header." + Name;

    public static Result<string> Read(IHeaderDictionary headers)
    {
        if (!headers.TryGetValue(Name, out var values) || values.Count == 0
            || (values.Count == 1 && string.IsNullOrWhiteSpace(values[0])))
        {
            return Result<string>.Failure(new ApplicationErrorDetail(
                "MissingIdempotencyKey", "An Idempotency-Key header is required.", Target: Target));
        }

        if (values.Count != 1 || values[0] is not { Length: >= MinimumLength and <= MaximumLength } key
            || key.Any(character => character is < ' ' or > '~'))
        {
            return Result<string>.Failure(new ApplicationErrorDetail(
                "InvalidIdempotencyKey", "The Idempotency-Key header is invalid.", Target: Target));
        }

        return Result<string>.Success(key);
    }
}
