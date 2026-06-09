namespace Loupedeck.ClickyPlugin;

using System;

internal sealed class HapticClickCommand
{
    private readonly PluginEventSender _pluginEvents;
    private bool _eventsRegistered;

    public HapticClickCommand(PluginEventSender pluginEvents)
    {
        ArgumentNullException.ThrowIfNull(pluginEvents);
        this._pluginEvents = pluginEvents;
    }

    public void RegisterEvents()
    {
        if (this._eventsRegistered)
        {
            return;
        }

        // Register the whole waveform catalog once so click dispatch later stays simple.
        foreach (var waveform in HapticWaveformCatalog.All)
        {
            this.RegisterButtonEvent("leftClick", "Left Click", waveform);
            this.RegisterButtonEvent("rightClick", "Right Click", waveform);
            this.RegisterButtonEvent("middleClick", "Middle Click", waveform);

            var previewEventName = BuildPreviewEventName(waveform.Id);
            this._pluginEvents.AddEvent(previewEventName, $"Preview {waveform.Label}", $"Preview the {waveform.Label} haptic feel.");
        }

        this._eventsRegistered = true;
        Logger.Info("Haptic events registered.");
    }

    public void Trigger(GlobalMouseButton button, ClickySettings settings)
    {
        if (!this._eventsRegistered || !settings.Enabled)
        {
            return;
        }

        // Each button can point at its own waveform, or be turned off completely.
        var eventName = button switch
        {
            GlobalMouseButton.Left when settings.LeftEnabled => BuildButtonEventName("leftClick", settings.LeftWaveform),
            GlobalMouseButton.Right when settings.RightEnabled => BuildButtonEventName("rightClick", settings.RightWaveform),
            GlobalMouseButton.Middle when settings.MiddleEnabled => BuildButtonEventName("middleClick", settings.MiddleWaveform),
            _ => null,
        };

        if (eventName == null)
        {
            return;
        }

        this._pluginEvents.RaiseEvent(eventName);
        Logger.Verbose($"Raised haptic event '{eventName}'.");
    }

    public void Preview(string waveform)
    {
        if (!this._eventsRegistered)
        {
            return;
        }

        var eventName = BuildPreviewEventName(waveform);
        this._pluginEvents.RaiseEvent(eventName);
        Logger.Verbose($"Raised preview haptic event '{eventName}'.");
    }

    private void RegisterButtonEvent(string prefix, string buttonLabel, HapticWaveformDefinition waveform)
    {
        var eventName = BuildButtonEventName(prefix, waveform.Id);
        this._pluginEvents.AddEvent(eventName, $"{buttonLabel} {waveform.Label}", $"Raised when {buttonLabel.ToLowerInvariant()} uses the {waveform.Label} feel.");
    }

    private static string BuildButtonEventName(string prefix, string waveform) =>
        prefix + HapticWaveformCatalog.ToEventSuffix(waveform);

    private static string BuildPreviewEventName(string waveform) =>
        "preview" + HapticWaveformCatalog.ToEventSuffix(waveform);
}
