namespace Loupedeck.ClickyPlugin;

using System;
using System.Linq;

internal sealed class ClickyPointerDeviceMatcher
{
    private readonly ClickySupportedPointerDevicesConfig _config;

    public ClickyPointerDeviceMatcher(ClickySupportedPointerDevicesConfig config)
    {
        this._config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public ClickyPointerDeviceMatch? Match(string devicePath, string vendorId, string productId)
    {
        foreach (var device in this._config.SupportedDevices)
        {
            foreach (var rule in device.Rules)
            {
                if (!MatchesVendorAndProduct(rule, vendorId, productId))
                {
                    continue;
                }

                if (!MatchesPath(rule, devicePath))
                {
                    continue;
                }

                return new ClickyPointerDeviceMatch
                {
                    Name = device.Name,
                    Label = string.IsNullOrWhiteSpace(device.Label) ? device.Name : device.Label,
                    ConnectionType = NormalizeConnection(rule.Connection),
                    VendorId = vendorId,
                    ProductId = productId,
                };
            }

            if (device.AllowByVidPidFallback && device.Rules.Any(rule => MatchesVendorAndProduct(rule, vendorId, productId)))
            {
                return new ClickyPointerDeviceMatch
                {
                    Name = device.Name,
                    Label = string.IsNullOrWhiteSpace(device.Label) ? device.Name : device.Label,
                    ConnectionType = "unknown",
                    VendorId = vendorId,
                    ProductId = productId,
                };
            }
        }

        return null;
    }

    private static bool MatchesVendorAndProduct(ClickySupportedPointerDeviceRule rule, string vendorId, string productId)
    {
        var normalizedVendorId = (rule.VendorId ?? string.Empty).Trim().ToUpperInvariant();
        var normalizedProductId = (rule.ProductId ?? string.Empty).Trim().ToUpperInvariant();

        return string.Equals(normalizedVendorId, vendorId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(normalizedProductId, productId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesPath(ClickySupportedPointerDeviceRule rule, string devicePath)
    {
        if (rule.PathContains is not { Length: > 0 })
        {
            return true;
        }

        return rule.PathContains.All(token => devicePath.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeConnection(string connection)
    {
        var normalized = (connection ?? string.Empty).Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }
}

internal sealed class ClickyPointerDeviceMatch
{
    public string Name { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string ConnectionType { get; init; } = string.Empty;

    public string VendorId { get; init; } = string.Empty;

    public string ProductId { get; init; } = string.Empty;
}
