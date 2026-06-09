namespace ClickyInputHelper;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

internal sealed class BridgeDiscoveryServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly string _bridgeFilePath;
    private readonly string _defaultBridgeBaseUrl;
    private Task? _serverTask;
    private bool _disposed;

    private BridgeDiscoveryServer(string defaultBridgeBaseUrl, string bridgeFilePath)
    {
        this._defaultBridgeBaseUrl = NormalizeBridgeBaseUrl(defaultBridgeBaseUrl);
        this._bridgeFilePath = bridgeFilePath;
        this._listener.Prefixes.Add($"http://127.0.0.1:{HelperOptions.DefaultDiscoveryPort}/");
        this._listener.Start();
        this._serverTask = Task.Run(() => this.RunAsync(this._cancellationTokenSource.Token));
    }

    public static BridgeDiscoveryServer? TryStart(HelperOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            return new BridgeDiscoveryServer(options.BridgeBaseUrl, options.BridgeFilePath);
        }
        catch
        {
            return null;
        }
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
        catch
        {
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
        catch
        {
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
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

            if (context.Request.HttpMethod == "GET" && path == "/discover")
            {
                var payload = JsonSerializer.Serialize(new DiscoverySnapshot
                {
                    BridgeBaseUrl = this.ResolveBridgeBaseUrl(),
                    DiscoveryPort = HelperOptions.DefaultDiscoveryPort,
                    HelperRunning = true,
                    DiscoveredAtUtc = DateTimeOffset.UtcNow,
                }, JsonOptions);
                await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload).ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/health")
            {
                await WriteResponseAsync(context.Response, "text/plain; charset=utf-8", "ok").ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteResponseAsync(context.Response, "text/plain; charset=utf-8", "Not found").ConfigureAwait(false);
        }
        catch
        {
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

    private string ResolveBridgeBaseUrl()
    {
        try
        {
            if (File.Exists(this._bridgeFilePath))
            {
                var fileValue = File.ReadAllText(this._bridgeFilePath).Trim();
                if (!string.IsNullOrWhiteSpace(fileValue))
                {
                    return NormalizeBridgeBaseUrl(fileValue);
                }
            }
        }
        catch
        {
        }

        return this._defaultBridgeBaseUrl;
    }

    private static bool ApplyBrowserAccessHeaders(HttpListenerRequest request, HttpListenerResponse response)
    {
        var origin = request.Headers["Origin"];
        if (!IsAllowedOrigin(origin))
        {
            return false;
        }

        response.Headers["Access-Control-Allow-Origin"] = origin!;
        response.Headers["Access-Control-Allow-Methods"] = "GET, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
        response.Headers["Access-Control-Allow-Private-Network"] = "true";
        response.Headers["Vary"] = "Origin";
        return true;
    }

    private static bool IsAllowedOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (string.Equals(uri.Host, "clicky.dzintarsit.lv", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "dzintarsit.lv", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "node.dzintarsit.lv", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
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

    private static string NormalizeBridgeBaseUrl(string bridgeBaseUrl) =>
        (string.IsNullOrWhiteSpace(bridgeBaseUrl) ? "http://127.0.0.1:65439" : bridgeBaseUrl.Trim()).TrimEnd('/');

    private sealed class DiscoverySnapshot
    {
        public string BridgeBaseUrl { get; init; } = string.Empty;

        public int DiscoveryPort { get; init; }

        public bool HelperRunning { get; init; }

        public DateTimeOffset DiscoveredAtUtc { get; init; }
    }
}
