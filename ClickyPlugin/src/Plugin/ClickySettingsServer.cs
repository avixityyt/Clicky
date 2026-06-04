namespace Loupedeck.ClickyPlugin;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

internal sealed class ClickySettingsServer : IDisposable
{
    private const int PreferredPort = 65439;
    private const string HostedSettingsPage = "https://clicky.dzintarsit.lv/settings/";

    private static readonly HashSet<string> AllowedBrowserOrigins = new(StringComparer.OrdinalIgnoreCase)
    {
        "https://clicky.dzintarsit.lv",
        "http://clicky.dzintarsit.lv:65439",
        "https://clicky.dzintarsit.lv:65439",
        "https://dzintarsit.lv",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ClickySettingsService _settingsService;
    private readonly Action<string> _previewAction;
    private readonly Func<ClickyDiagnosticsSnapshot> _diagnosticsFactory;
    private readonly ClickyInputEventStore _inputEventStore;
    private readonly ClickySelectedPointerDeviceService _selectedPointerDeviceService;
    private readonly Func<ClickyInputEventRecord, ClickyInputEventRecord> _inputEventHandler;
    private readonly Func<bool> _isInputHelperRunning;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private Task? _serverTask;
    private string? _rootUrl;
    private bool _disposed;

    public ClickySettingsServer(
        ClickySettingsService settingsService,
        Action<string> previewAction,
        Func<ClickyDiagnosticsSnapshot> diagnosticsFactory,
        ClickyInputEventStore inputEventStore,
        ClickySelectedPointerDeviceService selectedPointerDeviceService,
        Func<ClickyInputEventRecord, ClickyInputEventRecord> inputEventHandler,
        Func<bool> isInputHelperRunning)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(previewAction);
        ArgumentNullException.ThrowIfNull(diagnosticsFactory);
        ArgumentNullException.ThrowIfNull(inputEventStore);
        ArgumentNullException.ThrowIfNull(selectedPointerDeviceService);
        ArgumentNullException.ThrowIfNull(inputEventHandler);
        ArgumentNullException.ThrowIfNull(isInputHelperRunning);
        this._settingsService = settingsService;
        this._previewAction = previewAction;
        this._diagnosticsFactory = diagnosticsFactory;
        this._inputEventStore = inputEventStore;
        this._selectedPointerDeviceService = selectedPointerDeviceService;
        this._inputEventHandler = inputEventHandler;
        this._isInputHelperRunning = isInputHelperRunning;
    }

    public string RootUrl => this._rootUrl ?? throw new InvalidOperationException("Settings server is not started.");

    public void Start()
    {
        if (this._serverTask != null)
        {
            return;
        }

        // Prefer the stable localhost port, but fall back cleanly if it is already occupied.
        var port = FindPort();
        this._rootUrl = $"http://127.0.0.1:{port}/";
        this._listener.Prefixes.Add(this._rootUrl);
        this._listener.Start();
        this._serverTask = Task.Run(() => this.RunAsync(this._cancellationTokenSource.Token));
        Logger.Info($"Settings server started at {this._rootUrl}");
    }

    public void OpenBrowser()
    {
        // Pass the active bridge base in the hash so the hosted page can attach to the right plugin instance.
        var bridgeBase = Uri.EscapeDataString(this.RootUrl.TrimEnd('/'));
        Process.Start(new ProcessStartInfo
        {
            FileName = $"{HostedSettingsPage}#bridge={bridgeBase}",
            UseShellExecute = true,
        });
    }

    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;

        try
        {
            this._cancellationTokenSource.Cancel();
            this._listener.Stop();
            this._listener.Close();

            if (this._serverTask != null)
            {
                this._serverTask.Wait(TimeSpan.FromSeconds(2));
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Error while stopping the Clicky settings server.");
        }
        finally
        {
            this._cancellationTokenSource.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext? context = null;

                try
                {
                    context = await this._listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException) when (cancellationToken.IsCancellationRequested || !this._listener.IsListening)
                {
                    break;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (context == null)
                {
                    continue;
                }

                _ = Task.Run(() => this.HandleRequestAsync(context), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Settings server failed.");
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            // Keep the local API surface small: settings, previews, diagnostics, and device binding.
            var path = context.Request.Url?.AbsolutePath ?? "/";
            var hasOrigin = !string.IsNullOrWhiteSpace(context.Request.Headers["Origin"]);
            var isAllowedOrigin = ApplyBrowserAccessHeaders(context.Request, context.Response);

            if (context.Request.HttpMethod == "OPTIONS")
            {
                context.Response.StatusCode = hasOrigin && !isAllowedOrigin
                    ? (int)HttpStatusCode.Forbidden
                    : (int)HttpStatusCode.NoContent;
                return;
            }

            if (hasOrigin && !isAllowedOrigin)
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                await WriteResponseAsync(context.Response, "text/plain; charset=utf-8", "Origin not allowed").ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/")
            {
                await WriteResponseAsync(context.Response, "text/html; charset=utf-8", RenderSettingsPage()).ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/assets/styles.css")
            {
                await WriteResponseAsync(context.Response, "text/css; charset=utf-8", RenderStylesheet()).ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/assets/settings.js")
            {
                await WriteResponseAsync(context.Response, "application/javascript; charset=utf-8", RenderScript()).ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/api/settings")
            {
                var json = JsonSerializer.Serialize(this._settingsService.GetSnapshot(), JsonOptions);
                await WriteResponseAsync(context.Response, "application/json; charset=utf-8", json).ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/api/diagnostics")
            {
                var json = JsonSerializer.Serialize(this._diagnosticsFactory(), JsonOptions);
                await WriteResponseAsync(context.Response, "application/json; charset=utf-8", json).ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/api/input-events")
            {
                var json = JsonSerializer.Serialize(this._inputEventStore.CreateSnapshot(this._isInputHelperRunning()), JsonOptions);
                await WriteResponseAsync(context.Response, "application/json; charset=utf-8", json).ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/api/device-binding")
            {
                var json = JsonSerializer.Serialize(
                    this._inputEventStore.CreateBindingSnapshot(
                        this._isInputHelperRunning(),
                        this._selectedPointerDeviceService.GetSnapshot()),
                    JsonOptions);
                await WriteResponseAsync(context.Response, "application/json; charset=utf-8", json).ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/api/settings")
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                var incoming = JsonSerializer.Deserialize<ClickySettings>(body, JsonOptions) ?? ClickySettings.Defaults;
                var saved = this._settingsService.Update(incoming);
                var json = JsonSerializer.Serialize(saved, JsonOptions);
                await WriteResponseAsync(context.Response, "application/json; charset=utf-8", json).ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/api/preview")
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                var request = JsonSerializer.Deserialize<PreviewRequest>(body, JsonOptions);
                this._previewAction(HapticWaveformCatalog.Normalize(request?.Waveform));
                await WriteResponseAsync(context.Response, "application/json; charset=utf-8", "{\"ok\":true}").ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/api/input-events")
            {
                if (context.Request.RemoteEndPoint?.Address is not { } remoteAddress || !IPAddress.IsLoopback(remoteAddress))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    await WriteResponseAsync(context.Response, "text/plain; charset=utf-8", "Loopback requests only").ConfigureAwait(false);
                    return;
                }

                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                var record = JsonSerializer.Deserialize<ClickyInputEventRecord>(body, JsonOptions);
                if (record == null)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    await WriteResponseAsync(context.Response, "text/plain; charset=utf-8", "Invalid input event payload").ConfigureAwait(false);
                    return;
                }

                var processedRecord = this._inputEventHandler(record);
                this._inputEventStore.Record(processedRecord);
                await WriteResponseAsync(context.Response, "application/json; charset=utf-8", "{\"ok\":true}").ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/api/device-binding")
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                var request = JsonSerializer.Deserialize<DeviceBindingRequest>(body, JsonOptions) ?? new DeviceBindingRequest();

                if (request.Clear || request.SelectedDevice == null)
                {
                    this._selectedPointerDeviceService.Clear();
                }
                else
                {
                    this._selectedPointerDeviceService.Update(request.SelectedDevice);
                }

                var json = JsonSerializer.Serialize(
                    this._inputEventStore.CreateBindingSnapshot(
                        this._isInputHelperRunning(),
                        this._selectedPointerDeviceService.GetSnapshot()),
                    JsonOptions);
                await WriteResponseAsync(context.Response, "application/json; charset=utf-8", json).ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/api/reset-all")
            {
                var resetSettings = this._settingsService.Reset();
                this._selectedPointerDeviceService.Clear();
                this._inputEventStore.Clear();

                var json = JsonSerializer.Serialize(
                    new
                    {
                        ok = true,
                        settings = resetSettings,
                        binding = this._inputEventStore.CreateBindingSnapshot(
                            this._isInputHelperRunning(),
                            this._selectedPointerDeviceService.GetSnapshot()),
                    },
                    JsonOptions);
                await WriteResponseAsync(context.Response, "application/json; charset=utf-8", json).ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/health")
            {
                await WriteResponseAsync(context.Response, "text/plain; charset=utf-8", "ok").ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/favicon.ico")
            {
                context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                return;
            }

            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteResponseAsync(context.Response, "text/plain; charset=utf-8", "Not found").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Settings server request failed.");

            try
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await WriteResponseAsync(context.Response, "text/plain; charset=utf-8", "Internal server error").ConfigureAwait(false);
            }
            catch
            {
            }
        }
        finally
        {
            context.Response.OutputStream.Close();
        }
    }

    private static async Task WriteResponseAsync(HttpListenerResponse response, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentType = contentType;
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = bytes.Length;
        response.Headers["Cache-Control"] = "no-store";
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
    }

    private static bool ApplyBrowserAccessHeaders(HttpListenerRequest request, HttpListenerResponse response)
    {
        var origin = request.Headers["Origin"];
        if (string.IsNullOrWhiteSpace(origin) || !AllowedBrowserOrigins.Contains(origin))
        {
            return false;
        }

        response.Headers["Access-Control-Allow-Origin"] = origin;
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
        response.Headers["Access-Control-Allow-Private-Network"] = "true";
        response.Headers["Vary"] = "Origin";
        return true;
    }

    private static int FindPort()
    {
        if (IsPortAvailable(PreferredPort))
        {
            return PreferredPort;
        }

        return FindAvailablePort();
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var tcpListener = new TcpListener(IPAddress.Loopback, port);
            tcpListener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static int FindAvailablePort()
    {
        using var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        return ((IPEndPoint)tcpListener.LocalEndpoint).Port;
    }

    private static string RenderSettingsPage() =>
        """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta name="theme-color" content="#171129">
          <title>Clicky Settings</title>
          <link rel="preconnect" href="https://fonts.googleapis.com">
          <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
          <link href="https://fonts.googleapis.com/css2?family=Manrope:wght@400;500;600;700;800&family=Prata&display=swap" rel="stylesheet">
          <link rel="stylesheet" href="/assets/styles.css">
        </head>
        <body>
          <div class="page-shell">
            <header class="site-header shell">
              <a class="brand" href="/">Clicky</a>
              <div class="site-nav site-nav-static">Settings</div>
            </header>

            <main class="shell simple-settings-shell">
              <section class="simple-settings-hero panel reveal" style="--index: 0;">
                <div class="simple-settings-head">
                  <p class="eyebrow">Clicky Settings</p>
                  <h1>Make clicks feel right.</h1>
                  <p>Choose the feel for each mouse button and test it before saving.</p>
                </div>
                <div class="connection-pill" id="connectionPill">
                  <span class="connection-dot"></span>
                  <span id="connectionLabel">Checking Clicky...</span>
                </div>
              </section>

              <section class="simple-settings-card panel reveal" style="--index: 1;">
                <div class="master-row">
                  <div class="master-copy">
                    <h2>Haptic feedback</h2>
                    <p>Turn Clicky on or off.</p>
                  </div>
                  <label class="toggle toggle-large">
                    <input id="enabled" type="checkbox">
                    <span>Enabled</span>
                  </label>
                </div>

                <div class="control-list">
                  <article class="control-row">
                    <div class="control-main">
                      <h3>Left click</h3>
                      <p>Main click.</p>
                    </div>
                    <div class="control-actions">
                      <label class="toggle">
                        <input id="leftEnabled" type="checkbox">
                        <span>On</span>
                      </label>
                      <select id="leftWaveform"></select>
                      <button class="button button-ghost" type="button" data-preview-target="leftWaveform">Test</button>
                    </div>
                  </article>

                  <article class="control-row">
                    <div class="control-main">
                      <h3>Right click</h3>
                      <p>Context click.</p>
                    </div>
                    <div class="control-actions">
                      <label class="toggle">
                        <input id="rightEnabled" type="checkbox">
                        <span>On</span>
                      </label>
                      <select id="rightWaveform"></select>
                      <button class="button button-ghost" type="button" data-preview-target="rightWaveform">Test</button>
                    </div>
                  </article>

                  <article class="control-row">
                    <div class="control-main">
                      <h3>Middle click</h3>
                      <p>Tab and pan click.</p>
                    </div>
                    <div class="control-actions">
                      <label class="toggle">
                        <input id="middleEnabled" type="checkbox">
                        <span>On</span>
                      </label>
                      <select id="middleWaveform"></select>
                      <button class="button button-ghost" type="button" data-preview-target="middleWaveform">Test</button>
                    </div>
                  </article>
                </div>

                <div class="preview-row">
                  <div class="preview-copy">
                    <h3>Try a feel</h3>
                    <p>Preview any supported feedback pattern.</p>
                  </div>
                  <div class="preview-actions">
                    <select id="previewWaveform"></select>
                    <button id="previewButton" class="button button-ghost" type="button">Play</button>
                  </div>
                </div>

                <footer class="simple-settings-footer">
                  <div id="status" class="status" aria-live="polite">Loading settings...</div>
                  <div class="simple-settings-actions">
                    <button id="resetButton" class="button button-ghost" type="button">Reset</button>
                    <button id="saveButton" class="button button-primary" type="button">Save</button>
                  </div>
                </footer>
              </section>
            </main>

            <script src="/assets/settings.js"></script>
          </div>
        </body>
        </html>
        """;

    private static string RenderStylesheet() =>
        """
        :root {
          --bg: #473952;
          --bg-strong: #51356a;
          --bg-soft: #4f3d66;
          --line: rgba(237, 173, 199, 0.24);
          --line-strong: rgba(237, 173, 199, 0.44);
          --text: #fff7fb;
          --muted: #f0dfe8;
          --accent: #c46fd0;
          --shadow: 0 28px 80px rgba(5, 7, 15, 0.3);
          --radius: 18px;
          --radius-lg: 28px;
          --font-body: "Manrope", "Segoe UI Variable", "Segoe UI", sans-serif;
          --font-display: "Prata", Georgia, serif;
        }

        * { box-sizing: border-box; }
        html, body { margin: 0; min-height: 100%; }
        html { background: var(--bg); }
        body {
          min-width: 320px;
          color: var(--text);
          font-family: var(--font-body);
          background: linear-gradient(125deg, var(--bg-strong), var(--bg) 24%, #6c4386 56%, var(--bg-soft));
          background-size: 340% 340%;
          animation: gradient-drift 8s ease-in-out infinite alternate;
          position: relative;
          overflow-x: hidden;
          -webkit-font-smoothing: antialiased;
          text-rendering: optimizeLegibility;
        }

        body::before,
        body::after,
        .page-shell::before {
          content: "";
          position: fixed;
          pointer-events: none;
          z-index: 0;
          filter: blur(90px);
        }

        body::before {
          width: min(92rem, 100vw);
          height: 28rem;
          top: -8rem;
          left: 50%;
          transform: translateX(-50%);
          background: radial-gradient(circle, rgba(137, 77, 166, 0.72), rgba(0, 0, 0, 0) 58%);
          animation: float-top 7.5s ease-in-out infinite alternate;
        }

        body::after {
          width: 34rem;
          height: 28rem;
          top: 14%;
          right: -10rem;
          background: radial-gradient(circle, rgba(146, 81, 176, 0.54), rgba(0, 0, 0, 0) 60%);
          animation: float-side 9s ease-in-out infinite alternate;
        }

        .page-shell::before {
          width: min(82rem, 96vw);
          height: 34rem;
          bottom: -4rem;
          left: 50%;
          transform: translateX(-50%);
          background:
            radial-gradient(circle at 50% 18%, rgba(255, 247, 251, 0.34), rgba(0, 0, 0, 0) 25%),
            linear-gradient(rgba(142, 80, 176, 0.24), rgba(90, 63, 146, 0.22));
          border-radius: 50% 50% 0 0;
          animation: dome-breathe 6.5s ease-in-out infinite;
        }

        a { color: inherit; text-decoration: none; }
        h1, h2, h3, p { margin: 0; }
        h1, h2 {
          font-family: var(--font-display);
          font-weight: 400;
          letter-spacing: -0.02em;
        }

        p {
          color: var(--muted);
          line-height: 1.72;
        }

        .shell {
          width: min(calc(100% - 2rem), 960px);
          margin: 0 auto;
        }

        .site-header {
          position: sticky;
          top: 0;
          z-index: 10;
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 1rem;
          min-height: 5rem;
          padding: 0.9rem 0;
          backdrop-filter: blur(14px);
          -webkit-backdrop-filter: blur(14px);
        }

        .brand {
          font-family: var(--font-display);
          font-size: 1.2rem;
          letter-spacing: 0.08em;
          text-transform: uppercase;
        }

        .site-nav-static {
          color: var(--muted);
          font-size: 0.95rem;
        }

        .button {
          appearance: none;
          border: 1px solid var(--line);
          border-radius: 999px;
          min-height: 2.95rem;
          padding: 0.9rem 1.15rem;
          color: var(--text);
          font: inherit;
          font-weight: 800;
          cursor: pointer;
          background: rgba(255, 247, 251, 0.08);
          transition: transform 180ms ease, border-color 180ms ease, background-color 180ms ease, opacity 180ms ease;
        }

        .button:hover {
          border-color: var(--line-strong);
          transform: translateY(-2px);
        }

        .button:active {
          transform: translateY(-1px) scale(0.985);
        }

        .button:disabled {
          opacity: 0.55;
          cursor: wait;
          transform: none;
        }

        .button-primary {
          background: linear-gradient(135deg, rgba(106, 68, 165, 0.8), rgba(90, 64, 140, 0.92));
          border-color: rgba(208, 152, 228, 0.34);
        }

        .button-ghost {
          background: rgba(255, 247, 251, 0.08);
        }

        .panel {
          border: 1px solid var(--line);
          background: linear-gradient(180deg, rgba(255, 255, 255, 0.07), rgba(255, 255, 255, 0.03));
          border-radius: var(--radius-lg);
          box-shadow: var(--shadow);
          backdrop-filter: blur(12px);
          -webkit-backdrop-filter: blur(12px);
          position: relative;
          overflow: hidden;
        }

        .reveal {
          opacity: 0;
          transform: translateY(12px);
          animation: reveal 560ms cubic-bezier(0.2, 0.78, 0.16, 1) forwards;
          animation-delay: calc(var(--index, 0) * 90ms);
        }

        .eyebrow {
          color: var(--muted);
          letter-spacing: 0.14em;
          text-transform: uppercase;
          font-size: 0.76rem;
          font-weight: 700;
        }

        .simple-settings-shell {
          display: grid;
          gap: 1rem;
          padding-top: 0.6rem;
        }

        .simple-settings-hero {
          display: flex;
          justify-content: space-between;
          gap: 1rem;
          align-items: center;
          padding: 22px 24px;
        }

        .simple-settings-head {
          display: grid;
          gap: 0.7rem;
        }

        .simple-settings-head h1 {
          font-size: clamp(2.3rem, 4.6vw, 4rem);
          line-height: 0.95;
        }

        .connection-pill {
          display: inline-flex;
          align-items: center;
          gap: 10px;
          min-height: 2.9rem;
          padding: 0.8rem 1rem;
          border-radius: 999px;
          border: 1px solid var(--line);
          background: rgba(255, 247, 251, 0.08);
          white-space: nowrap;
        }

        .connection-pill[data-state="connected"] {
          border-color: rgba(166, 229, 188, 0.24);
        }

        .connection-pill[data-state="disconnected"] {
          border-color: rgba(255, 215, 232, 0.24);
        }

        .connection-dot {
          width: 10px;
          height: 10px;
          border-radius: 999px;
          background: var(--accent);
          box-shadow: 0 0 0 6px rgba(196, 111, 208, 0.14);
        }

        .connection-pill[data-state="connected"] .connection-dot {
          background: #9be0b1;
          box-shadow: 0 0 0 6px rgba(155, 224, 177, 0.14);
        }

        .connection-pill[data-state="disconnected"] .connection-dot {
          background: #ffd7e8;
          box-shadow: 0 0 0 6px rgba(255, 215, 232, 0.14);
        }

        .simple-settings-card {
          display: grid;
          gap: 0;
          padding: 8px 0 0;
        }

        .master-row,
        .control-row,
        .preview-row,
        .simple-settings-footer {
          padding: 18px 24px;
        }

        .master-row,
        .control-row,
        .preview-row {
          border-bottom: 1px solid rgba(255, 247, 251, 0.08);
        }

        .master-row {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 1rem;
        }

        .master-copy {
          display: grid;
          gap: 4px;
        }

        .master-copy h2 {
          font-size: 1.6rem;
        }

        .toggle {
          display: inline-flex;
          align-items: center;
          gap: 10px;
          color: var(--text);
          font-weight: 700;
          cursor: pointer;
        }

        .toggle input[type="checkbox"] {
          width: 18px;
          height: 18px;
          accent-color: var(--accent);
        }

        .toggle-large {
          font-size: 1rem;
        }

        .control-list {
          display: grid;
        }

        .control-row {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 1.2rem;
        }

        .control-main,
        .preview-copy {
          display: grid;
          gap: 4px;
        }

        .control-main h3,
        .preview-copy h3 {
          font-size: 1.08rem;
        }

        .control-actions,
        .preview-actions,
        .simple-settings-actions {
          display: flex;
          align-items: center;
          gap: 12px;
          flex-wrap: wrap;
        }

        .control-actions select,
        .preview-actions select {
          min-width: 220px;
        }

        select {
          min-height: 3.1rem;
          padding: 0.92rem 1rem;
          border-radius: 14px;
          border: 1px solid rgba(255, 255, 255, 0.12);
          background: rgba(255, 247, 251, 0.08);
          color: var(--text);
          font: inherit;
          outline: none;
          transition: border-color 160ms ease, background-color 160ms ease;
        }

        select:focus {
          border-color: var(--line-strong);
          background: rgba(255, 247, 251, 0.12);
        }

        .preview-row {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 1rem;
        }

        .simple-settings-footer {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 1rem;
        }

        .status {
          min-height: 1.4rem;
          color: var(--muted);
          font-size: 0.96rem;
        }

        .status.error { color: #ffd7e8; }
        .status.success { color: #f5d0ff; }

        @keyframes reveal {
          from {
            opacity: 0;
            transform: translateY(12px);
          }
          to {
            opacity: 1;
            transform: translateY(0);
          }
        }

        @keyframes gradient-drift {
          from { background-position: 0 15%; }
          to { background-position: 100% 88%; }
        }

        @keyframes float-top {
          from { transform: translateX(-60%) translateY(-1.4rem) scale(0.92); }
          to { transform: translateX(-40%) translateY(1.9rem) scale(1.12); }
        }

        @keyframes float-side {
          from { transform: translate(2.2rem, -1.6rem) scale(0.9); }
          to { transform: translate(-3.6rem, 2rem) scale(1.17); }
        }

        @keyframes dome-breathe {
          0%, 100% { transform: translateX(-50%) scaleX(1); }
          50% { transform: translateX(-50%) scaleX(1.05); }
        }

        @media (max-width: 980px) {
          .simple-settings-hero,
          .master-row,
          .control-row,
          .preview-row,
          .simple-settings-footer {
            flex-direction: column;
            align-items: start;
          }

          .control-actions,
          .preview-actions,
          .simple-settings-actions {
            width: 100%;
          }

          .control-actions > *,
          .preview-actions > *,
          .simple-settings-actions > * {
            width: 100%;
          }
        }

        @media (max-width: 720px) {
          .site-header {
            padding-top: 1rem;
          }

          .simple-settings-hero,
          .master-row,
          .control-row,
          .preview-row,
          .simple-settings-footer {
            padding-inline: 18px;
          }

          .simple-settings-head h1 {
            font-size: clamp(2.4rem, 14vw, 3.6rem);
          }
        }
        """;

    private static string RenderScript() =>
        """
        (() => {
          const defaultBridgeBase = "http://127.0.0.1:65439";
          const storageKey = "clicky.bridgeBase";
          const localHosts = new Set(["127.0.0.1", "localhost"]);
          const waveforms = [
            { id: "sharp_state_change", label: "Sharp State Change" },
            { id: "damp_state_change", label: "Damp State Change" },
            { id: "sharp_collision", label: "Sharp Collision" },
            { id: "damp_collision", label: "Damp Collision" },
            { id: "subtle_collision", label: "Subtle Collision" },
            { id: "happy_alert", label: "Happy Alert" },
            { id: "angry_alert", label: "Angry Alert" },
            { id: "completed", label: "Completed" },
            { id: "square", label: "Square" },
            { id: "wave", label: "Wave" },
            { id: "firework", label: "Firework" },
            { id: "mad", label: "Mad" },
            { id: "knock", label: "Knock" },
            { id: "jingle", label: "Jingle" },
            { id: "ringing", label: "Ringing" }
          ];

          const defaults = {
            enabled: true,
            leftEnabled: true,
            rightEnabled: true,
            middleEnabled: true,
            leftWaveform: "sharp_state_change",
            rightWaveform: "sharp_state_change",
            middleWaveform: "sharp_state_change"
          };

          const toggleIds = ["enabled", "leftEnabled", "rightEnabled", "middleEnabled"];
          const selectIds = ["leftWaveform", "rightWaveform", "middleWaveform", "previewWaveform"];

          const controls = Object.fromEntries([...toggleIds, ...selectIds].map((id) => [id, document.getElementById(id)]));
          const saveButton = document.getElementById("saveButton");
          const resetButton = document.getElementById("resetButton");
          const previewButton = document.getElementById("previewButton");
          const previewButtons = Array.from(document.querySelectorAll("[data-preview-target]"));
          const status = document.getElementById("status");
          const connectionLabel = document.getElementById("connectionLabel");
          const connectionPill = document.getElementById("connectionPill");

          let apiBase = resolveApiBase();
          populateWaveformOptions();

          function resolveApiBase() {
            const params = new URLSearchParams(window.location.search);
            const hashParams = new URLSearchParams(window.location.hash.replace(/^#/, ""));
            const fromQuery = params.get("bridge");
            const fromHash = hashParams.get("bridge");
            const fromStorage = window.localStorage.getItem(storageKey);
            const defaultBase = localHosts.has(window.location.hostname) ? window.location.origin : defaultBridgeBase;
            const rawValue = window.CLICKY_API_BASE || fromHash || fromQuery || fromStorage || defaultBase;
            return normalizeBase(rawValue);
          }

          function normalizeBase(value) {
            return (value || defaultBridgeBase).trim().replace(/\/$/, "");
          }

          function apiUrl(path) {
            return `${apiBase}${path}`;
          }

          function populateWaveformOptions() {
            for (const selectId of selectIds) {
              const select = controls[selectId];
              select.innerHTML = "";

              for (const waveform of waveforms) {
                const option = document.createElement("option");
                option.value = waveform.id;
                option.textContent = waveform.label;
                select.appendChild(option);
              }
            }

            controls.previewWaveform.value = defaults.leftWaveform;
          }

          function setBusy(isBusy) {
            saveButton.disabled = isBusy;
            resetButton.disabled = isBusy;
            previewButton.disabled = isBusy;

            for (const button of previewButtons) {
              button.disabled = isBusy;
            }
          }

          function setStatus(message, tone) {
            status.textContent = message;
            status.className = tone ? `status ${tone}` : "status";
          }

          function setConnectionState(isConnected, detail) {
            connectionLabel.textContent = isConnected ? "Connected" : detail;
            connectionPill.dataset.state = isConnected ? "connected" : "disconnected";
          }

          function rememberBridgeBase(value) {
            apiBase = normalizeBase(value);
            window.localStorage.setItem(storageKey, apiBase);
          }

          function readForm() {
            return {
              enabled: controls.enabled.checked,
              leftEnabled: controls.leftEnabled.checked,
              rightEnabled: controls.rightEnabled.checked,
              middleEnabled: controls.middleEnabled.checked,
              leftWaveform: controls.leftWaveform.value,
              rightWaveform: controls.rightWaveform.value,
              middleWaveform: controls.middleWaveform.value
            };
          }

          function writeForm(settings) {
            controls.enabled.checked = !!settings.enabled;
            controls.leftEnabled.checked = !!settings.leftEnabled;
            controls.rightEnabled.checked = !!settings.rightEnabled;
            controls.middleEnabled.checked = !!settings.middleEnabled;
            controls.leftWaveform.value = settings.leftWaveform || settings.leftProfile || defaults.leftWaveform;
            controls.rightWaveform.value = settings.rightWaveform || settings.rightProfile || defaults.rightWaveform;
            controls.middleWaveform.value = settings.middleWaveform || settings.middleProfile || defaults.middleWaveform;
          }

          async function checkHealth() {
            try {
              const response = await fetch(apiUrl("/health"), {
                cache: "no-store",
                headers: { "Accept": "text/plain" }
              });

              if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
              }

              rememberBridgeBase(apiBase);
              setConnectionState(true, "Connected");
              return true;
            } catch (error) {
              console.error(error);
              setConnectionState(false, "Open Clicky in Logitech Options+ first");
              return false;
            }
          }

          async function loadSettings() {
            setBusy(true);
            setStatus("Loading settings...");

            const isHealthy = await checkHealth();
            if (!isHealthy) {
              setStatus("Clicky is not available right now.", "error");
              setBusy(false);
              return;
            }

            try {
              const response = await fetch(apiUrl("/api/settings"), {
                cache: "no-store",
                headers: { "Accept": "application/json" }
              });

              if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
              }

              const settings = await response.json();
              writeForm(settings);
              setStatus("Ready.", "success");
            } catch (error) {
              console.error(error);
              setStatus("Could not load settings.", "error");
            } finally {
              setBusy(false);
            }
          }

          async function saveSettings(settings) {
            setBusy(true);
            setStatus("Saving...");

            try {
              const response = await fetch(apiUrl("/api/settings"), {
                method: "POST",
                headers: {
                  "Accept": "application/json",
                  "Content-Type": "application/json"
                },
                body: JSON.stringify(settings)
              });

              if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
              }

              const saved = await response.json();
              writeForm(saved);
              setStatus("Saved.", "success");
              setConnectionState(true, "Connected");
            } catch (error) {
              console.error(error);
              setStatus("Could not save settings.", "error");
              setConnectionState(false, "Open Clicky in Logitech Options+ first");
            } finally {
              setBusy(false);
            }
          }

          async function previewWaveform(waveform) {
            setStatus("Playing preview...");

            try {
              const response = await fetch(apiUrl("/api/preview"), {
                method: "POST",
                headers: {
                  "Accept": "application/json",
                  "Content-Type": "application/json"
                },
                body: JSON.stringify({ waveform })
              });

              if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
              }

              setStatus("Preview played.", "success");
              setConnectionState(true, "Connected");
            } catch (error) {
              console.error(error);
              setStatus("Could not play preview.", "error");
              setConnectionState(false, "Open Clicky in Logitech Options+ first");
            }
          }

          saveButton.addEventListener("click", () => saveSettings(readForm()));
          resetButton.addEventListener("click", () => {
            writeForm(defaults);
            saveSettings(defaults);
          });
          previewButton.addEventListener("click", () => previewWaveform(controls.previewWaveform.value));

          for (const button of previewButtons) {
            button.addEventListener("click", () => {
              const targetId = button.dataset.previewTarget;
              previewWaveform(controls[targetId].value);
            });
          }

          loadSettings();
        })();
        """;

    private sealed class PreviewRequest
    {
        public string? Waveform { get; set; }
    }

    private sealed class DeviceBindingRequest
    {
        public bool Clear { get; set; }

        public ClickySelectedPointerDevice? SelectedDevice { get; set; }
    }
}
