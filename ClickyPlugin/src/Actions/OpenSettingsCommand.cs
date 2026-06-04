namespace Loupedeck.ClickyPlugin;

public sealed class OpenSettingsCommand : PluginDynamicCommand
{
    public OpenSettingsCommand()
        : base("Open Clicky Settings", "Open the Clicky settings page in your browser.", "Settings", DeviceType.All)
    {
    }

    protected override void RunCommand(string actionParameter)
    {
        if (this.Plugin is ClickyPlugin clickyPlugin)
        {
            clickyPlugin.OpenSettingsPage();
        }
    }
}
