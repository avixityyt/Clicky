namespace Loupedeck.ClickyPlugin;

using System;

internal sealed class ClickyDeviceBindingSnapshot
{
    public bool HelperRunning { get; set; }

    public bool RequiresSelection { get; set; }

    public ClickySelectedPointerDevice? SelectedDevice { get; set; }

    public ClickyPointerDeviceCandidate[] Candidates { get; set; } = [];
}

internal sealed class ClickyPointerDeviceCandidate
{
    public string DeviceLabel { get; set; } = string.Empty;

    public string DevicePath { get; set; } = string.Empty;

    public string VendorId { get; set; } = string.Empty;

    public string ProductId { get; set; } = string.Empty;

    public string ConnectionType { get; set; } = string.Empty;

    public bool IsMxMaster4 { get; set; }

    public bool IsSelected { get; set; }

    public int SeenCount { get; set; }

    public string LastButton { get; set; } = string.Empty;

    public DateTimeOffset LastSeenUtc { get; set; }
}
