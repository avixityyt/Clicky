namespace Loupedeck.ClickyPlugin;

internal sealed class ClickySupportedPointerDevicesConfig
{
    public ClickySupportedPointerDevice[] SupportedDevices { get; init; } = [];

    public static ClickySupportedPointerDevicesConfig CreateDefault() =>
        new()
        {
            SupportedDevices =
            [
                new ClickySupportedPointerDevice
                {
                    Name = "mx_master_4",
                    Label = "MX Master 4",
                    AllowByVidPidFallback = true,
                    Rules =
                    [
                        new ClickySupportedPointerDeviceRule
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
                        new ClickySupportedPointerDeviceRule
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
                    ],
                },
            ],
        };
}

internal sealed class ClickySupportedPointerDevice
{
    public string Name { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public bool AllowByVidPidFallback { get; init; }

    public ClickySupportedPointerDeviceRule[] Rules { get; init; } = [];
}

internal sealed class ClickySupportedPointerDeviceRule
{
    public string Connection { get; init; } = string.Empty;

    public string VendorId { get; init; } = string.Empty;

    public string ProductId { get; init; } = string.Empty;

    public string[] PathContains { get; init; } = [];
}
