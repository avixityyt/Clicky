namespace ClickyInputHelper;

using System;
using System.IO;
using System.Text.Json;

internal sealed class SupportedPointerDevicesConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public SupportedPointerDevice[] SupportedDevices { get; set; } = [];

    public static SupportedPointerDevicesConfig LoadFromDefaultPath()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "devices.json");
        if (!File.Exists(configPath))
        {
            return CreateDefault();
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var parsed = JsonSerializer.Deserialize<SupportedPointerDevicesConfig>(json, JsonOptions);
            if (parsed?.SupportedDevices is { Length: > 0 })
            {
                return parsed;
            }
        }
        catch
        {
        }

        return CreateDefault();
    }

    public static SupportedPointerDevicesConfig CreateDefault() =>
        new()
        {
            SupportedDevices =
            [
                new SupportedPointerDevice
                {
                    Name = "mx_master_4",
                    Label = "MX Master 4",
                    AllowByVidPidFallback = true,
                    Rules =
                    [
                        new SupportedPointerDeviceRule
                        {
                            Connection = "bluetooth",
                            VendorId = "046D",
                            ProductId = "B042",
                            PathContains =
                            [
                                "00001812",
                                "dev_vid&02046d_pid&b042",
                            ],
                        },
                        new SupportedPointerDeviceRule
                        {
                            Connection = "receiver",
                            VendorId = "046D",
                            ProductId = "B042",
                            PathContains =
                            [
                                "vid_046d",
                                "pid_b042",
                            ],
                        },
                        new SupportedPointerDeviceRule
                        {
                            Connection = "receiver",
                            VendorId = "046D",
                            ProductId = "C548",
                            PathContains =
                            [
                                "vid_046d",
                                "pid_c548",
                            ],
                        },
                    ],
                },
            ],
        };
}

internal sealed class SupportedPointerDevice
{
    public string Name { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public bool AllowByVidPidFallback { get; set; }

    public SupportedPointerDeviceRule[] Rules { get; set; } = [];
}

internal sealed class SupportedPointerDeviceRule
{
    public string Connection { get; set; } = string.Empty;

    public string VendorId { get; set; } = string.Empty;

    public string ProductId { get; set; } = string.Empty;

    public string[] PathContains { get; set; } = [];
}
