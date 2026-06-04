namespace ClickyInputHelper;

using System;

internal sealed class ClickyInputEventPayload
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

    public string Source { get; set; } = "helper";
}
