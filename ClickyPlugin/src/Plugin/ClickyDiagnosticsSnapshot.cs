namespace Loupedeck.ClickyPlugin;

using System;

internal sealed class ClickyDiagnosticsSnapshot
{
    public string PluginVersion { get; set; } = "unknown";

    public string Runtime { get; set; } = string.Empty;

    public string HostedSettingsPage { get; set; } = string.Empty;

    public string BridgeBaseUrl { get; set; } = string.Empty;

    public bool IsWindows { get; set; }

    public bool MouseHookActive { get; set; }

    public bool InputHelperRunning { get; set; }

    public string InputMode { get; set; } = string.Empty;

    public DateTimeOffset? LastInputEventUtc { get; set; }

    public int WaveformCount { get; set; }

    public string[] SupportedButtons { get; set; } = [];

    public DateTimeOffset LoadedAtUtc { get; set; }
}
