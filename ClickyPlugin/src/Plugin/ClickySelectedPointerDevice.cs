namespace Loupedeck.ClickyPlugin;

using System;

internal sealed class ClickySelectedPointerDevice
{
    public string BindingStrategy { get; set; } = ClickyBindingStrategies.Auto;

    public string SourceKey { get; set; } = string.Empty;

    public string DeviceLabel { get; set; } = string.Empty;

    public string DevicePath { get; set; } = string.Empty;

    public string VendorId { get; set; } = string.Empty;

    public string ProductId { get; set; } = string.Empty;

    public string ConnectionType { get; set; } = string.Empty;

    public bool IsMxMaster4 { get; set; }

    public bool IsSharedReceiver { get; set; }

    public bool IsLikelyAmbiguous { get; set; }

    public ClickySelectedPointerDevice Clone() => new()
    {
        BindingStrategy = this.BindingStrategy,
        SourceKey = this.SourceKey,
        DeviceLabel = this.DeviceLabel,
        DevicePath = this.DevicePath,
        VendorId = this.VendorId,
        ProductId = this.ProductId,
        ConnectionType = this.ConnectionType,
        IsMxMaster4 = this.IsMxMaster4,
        IsSharedReceiver = this.IsSharedReceiver,
        IsLikelyAmbiguous = this.IsLikelyAmbiguous,
    };

    public bool Matches(ClickyInputEventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return ClickyBindingStrategies.Normalize(this.BindingStrategy) switch
        {
            ClickyBindingStrategies.ExactPath => this.MatchesExactPath(record),
            ClickyBindingStrategies.VidPidConnection => this.MatchesVidPidConnection(record),
            ClickyBindingStrategies.SourceFingerprint => this.MatchesSourceFingerprint(record),
            _ => this.MatchesAuto(record),
        };
    }

    public string GetSourceKey()
    {
        if (!string.IsNullOrWhiteSpace(this.SourceKey))
        {
            return this.SourceKey.Trim();
        }

        if (!string.IsNullOrWhiteSpace(this.DevicePath))
        {
            return this.DevicePath.Trim();
        }

        return string.Join(
            "|",
            this.DeviceLabel?.Trim() ?? string.Empty,
            this.VendorId?.Trim().ToUpperInvariant() ?? string.Empty,
            this.ProductId?.Trim().ToUpperInvariant() ?? string.Empty,
            this.ConnectionType?.Trim().ToLowerInvariant() ?? string.Empty);
    }

    public static ClickySelectedPointerDevice FromRecord(ClickyInputEventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new ClickySelectedPointerDevice
        {
            BindingStrategy = ClickyBindingStrategies.Auto,
            SourceKey = BuildSourceKey(record),
            DeviceLabel = record.DeviceLabel,
            DevicePath = record.DevicePath,
            VendorId = record.VendorId,
            ProductId = record.ProductId,
            ConnectionType = record.ConnectionType,
            IsMxMaster4 = record.IsMxMaster4,
            IsSharedReceiver = string.Equals(record.ConnectionType, "receiver-shared", StringComparison.OrdinalIgnoreCase),
            IsLikelyAmbiguous = string.Equals(record.ConnectionType, "receiver-shared", StringComparison.OrdinalIgnoreCase),
        };
    }

    public static ClickySelectedPointerDevice Sanitize(ClickySelectedPointerDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new ClickySelectedPointerDevice
        {
            BindingStrategy = ClickyBindingStrategies.Normalize(device.BindingStrategy),
            SourceKey = (device.SourceKey ?? string.Empty).Trim(),
            DeviceLabel = (device.DeviceLabel ?? string.Empty).Trim(),
            DevicePath = (device.DevicePath ?? string.Empty).Trim(),
            VendorId = (device.VendorId ?? string.Empty).Trim().ToUpperInvariant(),
            ProductId = (device.ProductId ?? string.Empty).Trim().ToUpperInvariant(),
            ConnectionType = (device.ConnectionType ?? string.Empty).Trim().ToLowerInvariant(),
            IsMxMaster4 = device.IsMxMaster4,
            IsSharedReceiver = device.IsSharedReceiver,
            IsLikelyAmbiguous = device.IsLikelyAmbiguous,
        };
    }

    private bool MatchesAuto(ClickyInputEventRecord record)
    {
        if (!this.IsSharedReceiver && this.MatchesExactPath(record))
        {
            return true;
        }

        if (this.MatchesVidPidConnection(record))
        {
            return true;
        }

        return this.MatchesSourceFingerprint(record);
    }

    private bool MatchesExactPath(ClickyInputEventRecord record)
    {
        return !string.IsNullOrWhiteSpace(this.DevicePath)
            && !string.IsNullOrWhiteSpace(record.DevicePath)
            && string.Equals(this.DevicePath, record.DevicePath, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesVidPidConnection(ClickyInputEventRecord record)
    {
        return !string.IsNullOrWhiteSpace(this.VendorId)
            && !string.IsNullOrWhiteSpace(this.ProductId)
            && string.Equals(this.VendorId, record.VendorId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(this.ProductId, record.ProductId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(this.ConnectionType, record.ConnectionType, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesSourceFingerprint(ClickyInputEventRecord record)
    {
        return string.Equals(this.GetSourceKey(), BuildSourceKey(record), StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSourceKey(ClickyInputEventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!string.IsNullOrWhiteSpace(record.DevicePath))
        {
            return record.DevicePath.Trim();
        }

        return string.Join(
            "|",
            record.DeviceLabel?.Trim() ?? string.Empty,
            record.VendorId?.Trim().ToUpperInvariant() ?? string.Empty,
            record.ProductId?.Trim().ToUpperInvariant() ?? string.Empty,
            record.ConnectionType?.Trim().ToLowerInvariant() ?? string.Empty);
    }
}

internal static class ClickyBindingStrategies
{
    public const string Auto = "auto";
    public const string ExactPath = "exact_path";
    public const string VidPidConnection = "vid_pid_connection";
    public const string SourceFingerprint = "source_fingerprint";

    public static string Normalize(string? strategy)
    {
        return (strategy ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            ExactPath => ExactPath,
            VidPidConnection => VidPidConnection,
            SourceFingerprint => SourceFingerprint,
            _ => Auto,
        };
    }
}
