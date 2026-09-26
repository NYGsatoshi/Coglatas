using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using SystemNetIPNetwork = System.Net.IPNetwork;

namespace Coglatas.Web.Configuration;

public static class ForwardedHeadersConfiguration
{
    public const string TrustForwardedHeadersKey = "ReverseProxy:TrustForwardedHeaders";
    public const string RequireHeaderSymmetryKey = "ReverseProxy:RequireHeaderSymmetry";
    public const string TrustedProxiesKey = "ReverseProxy:TrustedProxies";
    public const string TrustedNetworksKey = "ReverseProxy:TrustedNetworks";

    public static bool ShouldTrustForwardedHeaders(IConfiguration configuration)
    {
        return configuration.GetValue<bool>(TrustForwardedHeadersKey);
    }

    /// <summary>
    /// Returns safe, operator-facing configuration errors for the opt-in
    /// forwarded-header boundary. Values intentionally accept only IP literals
    /// and CIDRs: resolving a hostname here would make the trust boundary
    /// mutable through DNS and introduce a startup network dependency.
    /// </summary>
    public static IReadOnlyList<string> GetConfigurationErrors(IConfiguration configuration)
    {
        if (!ShouldTrustForwardedHeaders(configuration))
        {
            return [];
        }

        return ParseTrustBoundary(configuration).Errors;
    }

    public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor |
            ForwardedHeaders.XForwardedProto |
            ForwardedHeaders.XForwardedHost;
        options.ForwardLimit = 1;
        options.RequireHeaderSymmetry = configuration.GetValue(RequireHeaderSymmetryKey, true);

        // Keep ASP.NET Core's loopback defaults and add only deliberate,
        // operator-configured proxy boundaries. Clearing these collections
        // would trust forwarded headers from every remote client.
        var boundary = ParseTrustBoundary(configuration);
        foreach (var proxy in boundary.Proxies)
        {
            if (!options.KnownProxies.Contains(proxy))
            {
                options.KnownProxies.Add(proxy);
            }
        }

        foreach (var network in boundary.Networks)
        {
            if (!options.KnownIPNetworks.Contains(network))
            {
                options.KnownIPNetworks.Add(network);
            }
        }
    }

    private static TrustBoundary ParseTrustBoundary(IConfiguration configuration)
    {
        var proxyValues = ReadValues(configuration, TrustedProxiesKey);
        var networkValues = ReadValues(configuration, TrustedNetworksKey);
        var errors = new List<string>();
        var proxies = new List<IPAddress>();
        var networks = new List<SystemNetIPNetwork>();

        if (proxyValues.Count == 0 && networkValues.Count == 0)
        {
            errors.Add(
                "ReverseProxy:TrustForwardedHeaders requires at least one explicit ReverseProxy:TrustedProxies IP address or ReverseProxy:TrustedNetworks CIDR.");
        }

        foreach (var value in proxyValues)
        {
            if (IPAddress.TryParse(value, out var proxy) && !IsUnspecifiedAddress(proxy))
            {
                proxies.Add(proxy);
            }
            else
            {
                errors.Add("ReverseProxy:TrustedProxies must contain only non-unspecified IP addresses.");
            }
        }

        foreach (var value in networkValues)
        {
            if (SystemNetIPNetwork.TryParse(value, out var network) && network.PrefixLength != 0)
            {
                networks.Add(network);
            }
            else
            {
                errors.Add("ReverseProxy:TrustedNetworks must contain only bounded CIDR ranges.");
            }
        }

        return new TrustBoundary(
            proxies.Distinct().ToArray(),
            networks.Distinct().ToArray(),
            errors.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static IReadOnlyList<string> ReadValues(IConfiguration configuration, string key)
    {
        var values = new List<string>();
        AddDelimitedValue(values, configuration[key]);
        foreach (var child in configuration.GetSection(key).GetChildren())
        {
            AddDelimitedValue(values, child.Value);
        }

        return values;
    }

    private static void AddDelimitedValue(ICollection<string> values, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return;
        }

        foreach (var value in rawValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            values.Add(value);
        }
    }

    private static bool IsUnspecifiedAddress(IPAddress address)
    {
        return address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);
    }

    private sealed record TrustBoundary(
        IReadOnlyList<IPAddress> Proxies,
        IReadOnlyList<SystemNetIPNetwork> Networks,
        IReadOnlyList<string> Errors);
}
