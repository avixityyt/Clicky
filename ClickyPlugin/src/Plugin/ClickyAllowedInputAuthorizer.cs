namespace Loupedeck.ClickyPlugin;

using System;

internal sealed class ClickyAllowedInputAuthorizer
{
    private readonly ClickySelectedPointerDeviceService _selectedPointerDeviceService;

    public ClickyAllowedInputAuthorizer(ClickySelectedPointerDeviceService selectedPointerDeviceService)
    {
        ArgumentNullException.ThrowIfNull(selectedPointerDeviceService);
        this._selectedPointerDeviceService = selectedPointerDeviceService;
    }

    public ClickyInputEventRecord Process(ClickyInputEventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var processed = record.Clone();
        var selectedDevice = this._selectedPointerDeviceService.GetSnapshot();
        processed.AllowedForHaptics = selectedDevice != null
            ? selectedDevice.Matches(processed)
            : processed.IsMxMaster4;

        return processed;
    }
}
