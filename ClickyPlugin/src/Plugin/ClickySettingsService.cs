namespace Loupedeck.ClickyPlugin;

using System;
using System.Text.Json;

internal sealed class ClickySettingsService
{
    private const string SettingsKey = "clicky.settings";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly Plugin _plugin;
    private readonly object _sync = new();
    private ClickySettings _settings;

    public ClickySettingsService(Plugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        this._plugin = plugin;
        this._settings = this.Load();
    }

    public ClickySettings GetSnapshot()
    {
        lock (this._sync)
        {
            return this._settings.Clone();
        }
    }

    public ClickySettings Update(ClickySettings updatedSettings)
    {
        ArgumentNullException.ThrowIfNull(updatedSettings);

        lock (this._sync)
        {
            this._settings = Sanitize(updatedSettings);
            this.Save(this._settings);
            return this._settings.Clone();
        }
    }

    public ClickySettings Reset()
    {
        lock (this._sync)
        {
            this._settings = ClickySettings.Defaults;
            this.Save(this._settings);
            return this._settings.Clone();
        }
    }

    private ClickySettings Load()
    {
        try
        {
            if (this._plugin.TryGetPluginSetting(SettingsKey, out var settingsJson) && !string.IsNullOrWhiteSpace(settingsJson))
            {
                var settings = JsonSerializer.Deserialize<ClickySettings>(settingsJson, JsonOptions);
                if (settings != null)
                {
                    return Sanitize(settings);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to load Clicky settings. Using defaults.");
        }

        return ClickySettings.Defaults;
    }

    private void Save(ClickySettings settings)
    {
        try
        {
            var settingsJson = JsonSerializer.Serialize(settings, JsonOptions);
            this._plugin.SetPluginSetting(SettingsKey, settingsJson);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to save Clicky settings.");
        }
    }

    private static ClickySettings Sanitize(ClickySettings settings)
    {
        settings.LeftWaveform = HapticWaveformCatalog.Normalize(!string.IsNullOrWhiteSpace(settings.LeftProfile) ? settings.LeftProfile : settings.LeftWaveform);
        settings.RightWaveform = HapticWaveformCatalog.Normalize(!string.IsNullOrWhiteSpace(settings.RightProfile) ? settings.RightProfile : settings.RightWaveform);
        settings.MiddleWaveform = HapticWaveformCatalog.Normalize(!string.IsNullOrWhiteSpace(settings.MiddleProfile) ? settings.MiddleProfile : settings.MiddleWaveform);
        settings.LeftProfile = null;
        settings.RightProfile = null;
        settings.MiddleProfile = null;
        return settings;
    }
}
