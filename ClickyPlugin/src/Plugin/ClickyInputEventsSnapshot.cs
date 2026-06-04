namespace Loupedeck.ClickyPlugin;

using System;

internal sealed class ClickyInputEventsSnapshot
{
    public bool HelperRunning { get; set; }

    public DateTimeOffset? LastInputEventUtc { get; set; }

    public int TotalRecorded { get; set; }

    public ClickyInputEventRecord[] Events { get; set; } = [];
}
