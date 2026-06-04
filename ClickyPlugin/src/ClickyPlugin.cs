namespace Loupedeck.ClickyPlugin;

using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

public sealed class ClickyPlugin : Plugin
{
    private GlobalMouseHookService? _mouseHook;
    private HapticClickCommand? _hapticCommand;
    private ClickySettingsService? _settingsService;
    private ClickySettingsServer? _settingsServer;
    private ClickyInputEventStore? _inputEventStore;
    private ClickyAllowedInputAuthorizer? _inputAuthorizer;
    private ClickyInputHelperController? _inputHelper;
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
            // Wire up the bridge, helper, and haptic pipeline before mouse input starts flowing.
            this._loadedAtUtc = DateTimeOffset.UtcNow;
            this._settingsService = new ClickySettingsService(this);
            this._inputEventStore = new ClickyInputEventStore();
            this._selectedPointerDeviceService = new ClickySelectedPointerDeviceService(this);
            this._inputAuthorizer = new ClickyAllowedInputAuthorizer(
                this._selectedPointerDeviceService,
                ClickyInputHelperController.IsHelperResponsive);
            this._hapticCommand = new HapticClickCommand(this.PluginEvents);
            this._hapticCommand.RegisterEvents();
            this._settingsServer = new ClickySettingsServer(
                this._settingsService,
                waveform => this._hapticCommand?.Preview(waveform),
                this.BuildDiagnosticsSnapshot,
                this._inputEventStore,
                this._selectedPointerDeviceService,
                this.OnInputEventReceived,
                ClickyInputHelperController.IsHelperResponsive);
            this._settingsServer.Start();

            this._inputHelper = new ClickyInputHelperController();
            try
            {
                // The helper gives us per-device input data when it is available.
                this._inputHelper.Start(this._settingsServer.RootUrl.TrimEnd('/'));
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Clicky input helper did not start. Falling back to the global mouse hook only.");
            }

            this._mouseHook = new GlobalMouseHookService();
            this._mouseHook.MouseClicked += this.OnGlobalMouseClicked;
            try
            {
                // The global hook is still the main trigger path for click-driven haptics.
                this._mouseHook.Start();
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Mouse input backend failed to start. Clicky settings will stay available, but click-triggered haptics are disabled.");
                this._mouseHook.MouseClicked -= this.OnGlobalMouseClicked;
                this._mouseHook.Dispose();
                this._mouseHook = null;
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

    private void OnGlobalMouseClicked(object? sender, GlobalMouseClickEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                Logger.Verbose($"Mouse click detected: {e.Button}.");

                if (this._settingsService == null)
                {
                    return;
                }

                // Give the helper a brief window to post the matching device event first.
                await Task.Delay(90).ConfigureAwait(false);
                if (this._inputAuthorizer != null && !this._inputAuthorizer.ShouldTrigger(e.Button))
                {
                    Logger.Verbose($"Skipping haptic trigger for {e.Button} because no matching helper event was authorized.");
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
        if (this._mouseHook != null)
        {
            this._mouseHook.MouseClicked -= this.OnGlobalMouseClicked;
            this._mouseHook.Dispose();
            this._mouseHook = null;
        }

        this._settingsServer?.Dispose();
        this._settingsServer = null;
        this._inputHelper?.Dispose();
        this._inputHelper = null;
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
        var helperRunning = ClickyInputHelperController.IsHelperResponsive();
        var selectedDevice = this._selectedPointerDeviceService?.GetSnapshot();
        return new ClickyDiagnosticsSnapshot
        {
            PluginVersion = version,
            Runtime = RuntimeInformation.OSDescription,
            HostedSettingsPage = "https://clicky.dzintarsit.lv/settings/",
            BridgeBaseUrl = bridgeBaseUrl,
            IsWindows = OperatingSystem.IsWindows(),
            MouseHookActive = this._mouseHook?.IsActive ?? false,
            InputHelperRunning = helperRunning,
            InputMode = selectedDevice != null
                ? $"Selected device: {selectedDevice.DeviceLabel}"
                : helperRunning
                    ? "Helper-assisted MX detection"
                    : "Global hook fallback",
            LastInputEventUtc = this._inputEventStore?.CreateSnapshot(helperRunning).LastInputEventUtc,
            WaveformCount = HapticWaveformCatalog.All.Count,
            SupportedButtons = Enum.GetNames<GlobalMouseButton>().Select(name => name.ToLowerInvariant()).ToArray(),
            LoadedAtUtc = this._loadedAtUtc,
        };
    }
}
