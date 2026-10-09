using System.Globalization;
using System.Text.Json;
using Coglatas.SecurityArchitecture;

return await SecurityArchitectureCli.RunAsync(args, Console.Out);

public static class SecurityArchitectureCli
{
    public static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        ValidationResult result;
        try
        {
            result = args switch
            {
                ["validate", var path, var time] =>
                    ContractValidator.Validate(await ReadAsync<ContractDocument>(path), Time(time)),
                ["inventory-diff", var baseline, var candidate, var time] =>
                    InventoryDiff.Compare(await ReadAsync<ContractDocument>(baseline),
                        await ReadAsync<ContractDocument>(candidate), Time(time)),
                ["evidence-check", var contracts, var evidence, var sha, var environment, var time] =>
                    EvidenceValidator.Check(await ReadAsync<ContractDocument>(contracts),
                        await ReadAsync<EvidenceDocument>(evidence), sha, environment, Time(time)),
                _ => new(false, [new("CLI_USAGE", "command",
                    "Use validate <contracts> <as-of-UTC>; inventory-diff <baseline> <candidate> <as-of-UTC>; " +
                    "evidence-check <contracts> <evidence> <candidate-SHA> <environment-digest> <as-of-UTC>.")])
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or FormatException or ArgumentException)
        {
            // Never echo input, paths, exception text, policy contents or credentials.
            result = new(false, [new("INPUT_ERROR", "document", "Input is unreadable, malformed or unsupported.")]);
        }
        await output.WriteLineAsync(JsonSerializer.Serialize(result, ContractJson.Options));
        return result.Valid ? 0 : 1;
    }

    private static async Task<T> ReadAsync<T>(string path) => ContractJson.Read<T>(await File.ReadAllTextAsync(path));
    private static DateTimeOffset Time(string value)
    {
        var result = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (result.Offset != TimeSpan.Zero) throw new FormatException("UTC required.");
        return result;
    }
}
