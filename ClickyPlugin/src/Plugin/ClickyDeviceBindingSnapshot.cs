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
    public string SourceKey { get; set; } = string.Empty;

    public string DeviceLabel { get; set; } = string.Empty;

    public string DevicePath { get; set; } = string.Empty;

    public string VendorId { get; set; } = string.Empty;

    public string ProductId { get; set; } = string.Empty;

    public string ConnectionType { get; set; } = string.Empty;

    public bool IsMxMaster4 { get; set; }

    public bool IsSharedReceiver { get; set; }

    public bool IsLikelyAmbiguous { get; set; }

    public bool IsSelected { get; set; }

    public int SeenCount { get; set; }

    public string LastButton { get; set; } = string.Empty;

    public DateTimeOffset FirstSeenUtc { get; set; }

    public DateTimeOffset LastSeenUtc { get; set; }

    public string IdentityKind { get; set; } = string.Empty;

    public string Warning { get; set; } = string.Empty;
}
