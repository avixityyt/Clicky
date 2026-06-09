namespace Loupedeck.ClickyPlugin;

using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

public sealed class ClickyPlugin : Plugin
{
    private ClickyRawInputService? _rawInput;
    private GlobalMouseHookService? _mouseHook;
    private HapticClickCommand? _hapticCommand;
    private ClickySettingsService? _settingsService;
    private ClickySettingsServer? _settingsServer;
    private ClickyInputEventStore? _inputEventStore;
    private ClickyAllowedInputAuthorizer? _inputAuthorizer;
    private ClickySelectedPointerDeviceService? _selectedPointerDeviceService;
    private DateTimeOffset _loadedAtUtc;

    public override bool UsesApplicationApiOnly => true;

    public override bool HasNoApplication => true;

    public ClickyPlugin()
    {
        Logger.Init(this.Log);
    }

    public override void Load()
    {
        Logger.Info("Clicky plugin loading.");

        try
        {
            // Wire up settings, device binding, and haptic output before input starts flowing.
            this._loadedAtUtc = DateTimeOffset.UtcNow;
            this._settingsService = new ClickySettingsService(this);
            this._inputEventStore = new ClickyInputEventStore();
            this._selectedPointerDeviceService = new ClickySelectedPointerDeviceService(this);
            this._inputAuthorizer = new ClickyAllowedInputAuthorizer(this._selectedPointerDeviceService);
            this._hapticCommand = new HapticClickCommand(this.PluginEvents);
            this._hapticCommand.RegisterEvents();

            // Bring the local bridge up before the hosted settings page tries to use it.
            this._settingsServer = new ClickySettingsServer(
                this._settingsService,
                waveform => this._hapticCommand?.Preview(waveform),
                this.BuildDiagnosticsSnapshot,
                this._inputEventStore,
                this._selectedPointerDeviceService,
                this.OnInputEventReceived,
                () => this._rawInput?.IsActive ?? false);
            this._settingsServer.Start();

            this._rawInput = new ClickyRawInputService();
            try
            {
                // Device-aware raw input is the preferred trigger path for click haptics.
                this._rawInput.InputEventReceived += this.OnRawInputReceived;
                this._rawInput.Start();
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Clicky raw input did not start. Falling back to the global mouse hook.");
                this._rawInput.InputEventReceived -= this.OnRawInputReceived;
                this._rawInput.Dispose();
                this._rawInput = null;
            }

            if (this._rawInput == null)
            {
                this._mouseHook = new GlobalMouseHookService();
                this._mouseHook.MouseClicked += this.OnGlobalMouseClicked;
                try
                {
                    // The low-level hook is only a fallback when raw input is unavailable.
                    this._mouseHook.Start();
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Mouse input backend failed to start. Clicky settings will stay available, but click-triggered haptics are disabled.");
                    this._mouseHook.MouseClicked -= this.OnGlobalMouseClicked;
                    this._mouseHook.Dispose();
                    this._mouseHook = null;
                }
            }

            Logger.Info("Clicky plugin loaded.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Clicky failed to finish loading.");
            this.Cleanup();
            throw;
        }
    }

    public override void Unload()
    {
        Logger.Info("Clicky plugin unloading.");
        this.Cleanup();
        Logger.Info("Clicky plugin unloaded.");
    }

    public void OpenSettingsPage()
    {
        if (this._settingsServer == null)
        {
            throw new InvalidOperationException("Settings server is not available.");
        }

        Logger.Info("Opening Clicky settings page.");
        this._settingsServer.OpenBrowser();
    }

    private void OnRawInputReceived(object? sender, ClickyInputEventRecord record)
    {
        _ = Task.Run(() =>
        {
            try
            {
                if (this._settingsService == null || this._inputEventStore == null)
                {
                    return;
                }

                var processed = this._inputAuthorizer?.Process(record) ?? record;
                this._inputEventStore.Record(processed);

                if (!processed.AllowedForHaptics || !TryParseButton(processed.Button, out var button))
                {
                    return;
                }

                Logger.Verbose($"Raw input click detected from '{processed.DeviceLabel}' ({processed.Button}).");
                this._hapticCommand?.Trigger(button, this._settingsService.GetSnapshot());
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unhandled exception while processing raw input.");
            }
        });
    }

    private void OnGlobalMouseClicked(object? sender, GlobalMouseClickEventArgs e)
    {
        _ = Task.Run(() =>
        {
            try
            {
                Logger.Verbose($"Mouse click detected: {e.Button}.");

                if (this._settingsService == null)
                {
                    return;
                }

                this._hapticCommand?.Trigger(e.Button, this._settingsService.GetSnapshot());
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unhandled exception while processing a mouse click.");
            }
        });
    }

    private void Cleanup()
    {
        // Tear down input sources before the bridge/services disappear underneath them.
        if (this._rawInput != null)
        {
            this._rawInput.InputEventReceived -= this.OnRawInputReceived;
            this._rawInput.Dispose();
            this._rawInput = null;
        }

        if (this._mouseHook != null)
        {
            this._mouseHook.MouseClicked -= this.OnGlobalMouseClicked;
            this._mouseHook.Dispose();
            this._mouseHook = null;
        }

        this._settingsServer?.Dispose();
        this._settingsServer = null;
        this._selectedPointerDeviceService = null;
        this._inputAuthorizer = null;
        this._inputEventStore = null;
        this._settingsService = null;
        this._hapticCommand = null;
    }

    private ClickyInputEventRecord OnInputEventReceived(ClickyInputEventRecord record)
    {
        return this._inputAuthorizer?.Process(record) ?? record;
    }

    private ClickyDiagnosticsSnapshot BuildDiagnosticsSnapshot()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var bridgeBaseUrl = this._settingsServer?.RootUrl.TrimEnd('/') ?? string.Empty;
        var rawInputActive = this._rawInput?.IsActive ?? false;
        var selectedDevice = this._selectedPointerDeviceService?.GetSnapshot();
        var fallbackHookActive = this._mouseHook?.IsActive ?? false;
        return new ClickyDiagnosticsSnapshot
        {
            PluginVersion = version,
            Runtime = RuntimeInformation.OSDescription,
            HostedSettingsPage = "https://clicky.dzintarsit.lv/settings/",
            BridgeBaseUrl = bridgeBaseUrl,
            IsWindows = OperatingSystem.IsWindows(),
            MouseHookActive = rawInputActive || fallbackHookActive,
            InputHelperRunning = rawInputActive,
            InputMode = selectedDevice != null
                ? $"Selected device: {selectedDevice.DeviceLabel}"
                : rawInputActive
                    ? "In-plugin raw input"
                    : fallbackHookActive
                        ? "Global hook fallback"
                        : "Inactive",
            LastInputEventUtc = this._inputEventStore?.CreateSnapshot(rawInputActive).LastInputEventUtc,
            WaveformCount = HapticWaveformCatalog.All.Count,
            SupportedButtons = Enum.GetNames<GlobalMouseButton>().Select(name => name.ToLowerInvariant()).ToArray(),
            LoadedAtUtc = this._loadedAtUtc,
        };
    }

    private static bool TryParseButton(string buttonName, out GlobalMouseButton button)
    {
        switch ((buttonName ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "left":
                button = GlobalMouseButton.Left;
                return true;
            case "right":
                button = GlobalMouseButton.Right;
                return true;
            case "middle":
                button = GlobalMouseButton.Middle;
                return true;
            default:
                button = default;
                return false;
        }
    }
}
