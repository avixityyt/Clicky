namespace ClickyInputHelper;

using System;
using System.Windows.Forms;

internal sealed class HelperApplicationContext : ApplicationContext
{
    private readonly RawInputMessageWindow _window;
    private readonly ClickyBridgeClient _bridgeClient;
    private readonly PointerDeviceMatcher _matcher;
    private readonly HelperHeartbeat _heartbeat;
    private readonly BridgeDiscoveryServer? _discoveryServer;

    public HelperApplicationContext(HelperOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this._bridgeClient = new ClickyBridgeClient(options.BridgeBaseUrl, options.BridgeFilePath);
        this._matcher = new PointerDeviceMatcher(SupportedPointerDevicesConfig.LoadFromDefaultPath());
        this._heartbeat = new HelperHeartbeat(options.HeartbeatFilePath);
        this._discoveryServer = BridgeDiscoveryServer.TryStart(options);
        this._window = new RawInputMessageWindow(this._bridgeClient, this._matcher);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this._discoveryServer?.Dispose();
            this._heartbeat.Dispose();
            this._window.Dispose();
            this._bridgeClient.Dispose();
        }

        base.Dispose(disposing);
    }
}
