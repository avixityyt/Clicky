namespace Loupedeck.ClickyPlugin;

using System;
using System.Text.Json;

internal sealed class ClickySelectedPointerDeviceService
{
    private const string SelectedDeviceKey = "clicky.selectedPointerDevice";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly Plugin _plugin;
    private readonly object _sync = new();
    private ClickySelectedPointerDevice? _selectedDevice;

    public ClickySelectedPointerDeviceService(Plugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        this._plugin = plugin;
        this._selectedDevice = this.Load();
    }

    public ClickySelectedPointerDevice? GetSnapshot()
    {
        lock (this._sync)
        {
            return this._selectedDevice?.Clone();
        }
    }

    public ClickySelectedPointerDevice Update(ClickySelectedPointerDevice selectedDevice)
    {
        ArgumentNullException.ThrowIfNull(selectedDevice);

        lock (this._sync)
        {
            this._selectedDevice = ClickySelectedPointerDevice.Sanitize(selectedDevice);
            this.Save(this._selectedDevice);
            return this._selectedDevice.Clone();
        }
    }

    public void Clear()
    {
        lock (this._sync)
        {
            this._selectedDevice = null;
            this.Save(null);
        }
    }

    private ClickySelectedPointerDevice? Load()
    {
        try
        {
            if (this._plugin.TryGetPluginSetting(SelectedDeviceKey, out var selectedDeviceJson) && !string.IsNullOrWhiteSpace(selectedDeviceJson))
            {
                var selectedDevice = JsonSerializer.Deserialize<ClickySelectedPointerDevice>(selectedDeviceJson, JsonOptions);
                if (selectedDevice != null)
                {
                    return ClickySelectedPointerDevice.Sanitize(selectedDevice);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to load the selected Clicky pointer device.");
        }

        return null;
    }

    private void Save(ClickySelectedPointerDevice? selectedDevice)
    {
        try
        {
            var value = selectedDevice == null
                ? string.Empty
                : JsonSerializer.Serialize(selectedDevice, JsonOptions);
            this._plugin.SetPluginSetting(SelectedDeviceKey, value);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to save the selected Clicky pointer device.");
        }
    }
}
