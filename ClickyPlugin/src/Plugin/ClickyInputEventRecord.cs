namespace Loupedeck.ClickyPlugin;

using System;

internal sealed class ClickyInputEventRecord
{
    public DateTimeOffset OccurredAtUtc { get; set; }

    public string Button { get; set; } = string.Empty;

    public string DeviceLabel { get; set; } = string.Empty;

    public string DevicePath { get; set; } = string.Empty;

    public string VendorId { get; set; } = string.Empty;

    public string ProductId { get; set; } = string.Empty;

    public string ConnectionType { get; set; } = string.Empty;

    public bool IsMxMaster4 { get; set; }

    public bool AllowedForHaptics { get; set; }

    public string Source { get; set; } = "plugin";

    public ClickyInputEventRecord Clone() => new()
    {
        OccurredAtUtc = this.OccurredAtUtc,
        Button = this.Button,
        DeviceLabel = this.DeviceLabel,
        DevicePath = this.DevicePath,
        VendorId = this.VendorId,
        ProductId = this.ProductId,
        ConnectionType = this.ConnectionType,
        IsMxMaster4 = this.IsMxMaster4,
        AllowedForHaptics = this.AllowedForHaptics,
        Source = this.Source,
    };
}
