namespace Loupedeck.ClickyPlugin;

using System;

internal sealed class ClickySelectedPointerDevice
{
    public string DeviceLabel { get; set; } = string.Empty;

    public string DevicePath { get; set; } = string.Empty;

    public string VendorId { get; set; } = string.Empty;

    public string ProductId { get; set; } = string.Empty;

    public string ConnectionType { get; set; } = string.Empty;

    public bool IsMxMaster4 { get; set; }

    public ClickySelectedPointerDevice Clone() => new()
    {
        DeviceLabel = this.DeviceLabel,
        DevicePath = this.DevicePath,
        VendorId = this.VendorId,
        ProductId = this.ProductId,
        ConnectionType = this.ConnectionType,
        IsMxMaster4 = this.IsMxMaster4,
    };

    public bool Matches(ClickyInputEventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!string.IsNullOrWhiteSpace(this.DevicePath) && !string.IsNullOrWhiteSpace(record.DevicePath))
        {
            return string.Equals(this.DevicePath, record.DevicePath, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(this.VendorId, record.VendorId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(this.ProductId, record.ProductId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(this.ConnectionType, record.ConnectionType, StringComparison.OrdinalIgnoreCase);
    }

    public static ClickySelectedPointerDevice FromRecord(ClickyInputEventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new ClickySelectedPointerDevice
        {
            DeviceLabel = record.DeviceLabel,
            DevicePath = record.DevicePath,
            VendorId = record.VendorId,
            ProductId = record.ProductId,
            ConnectionType = record.ConnectionType,
            IsMxMaster4 = record.IsMxMaster4,
        };
    }

    public static ClickySelectedPointerDevice Sanitize(ClickySelectedPointerDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new ClickySelectedPointerDevice
        {
            DeviceLabel = (device.DeviceLabel ?? string.Empty).Trim(),
            DevicePath = (device.DevicePath ?? string.Empty).Trim(),
            VendorId = (device.VendorId ?? string.Empty).Trim().ToUpperInvariant(),
            ProductId = (device.ProductId ?? string.Empty).Trim().ToUpperInvariant(),
            ConnectionType = (device.ConnectionType ?? string.Empty).Trim().ToLowerInvariant(),
            IsMxMaster4 = device.IsMxMaster4,
        };
    }
}
