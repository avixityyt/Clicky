namespace Loupedeck.ClickyPlugin;

using System.Text.Json.Serialization;

internal sealed class ClickySettings
{
    public bool Enabled { get; set; } = true;

    public bool LeftEnabled { get; set; } = true;

    public bool RightEnabled { get; set; } = true;

    public bool MiddleEnabled { get; set; } = true;

    public string LeftWaveform { get; set; } = HapticWaveformCatalog.DefaultWaveform;

    public string RightWaveform { get; set; } = HapticWaveformCatalog.DefaultWaveform;

    public string MiddleWaveform { get; set; } = HapticWaveformCatalog.DefaultWaveform;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LeftProfile { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RightProfile { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MiddleProfile { get; set; }

    [JsonIgnore]
    public static ClickySettings Defaults => new();

    public ClickySettings Clone() => new()
    {
        Enabled = this.Enabled,
        LeftEnabled = this.LeftEnabled,
        RightEnabled = this.RightEnabled,
        MiddleEnabled = this.MiddleEnabled,
        LeftWaveform = this.LeftWaveform,
        RightWaveform = this.RightWaveform,
        MiddleWaveform = this.MiddleWaveform,
    };
}
