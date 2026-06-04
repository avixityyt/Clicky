namespace ClickyInputHelper;

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

internal sealed class ClickyBridgeClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2),
    };
    private readonly string _bridgeFilePath;
    private string _bridgeBaseUrl;

    public ClickyBridgeClient(string bridgeBaseUrl, string bridgeFilePath)
    {
        this._bridgeBaseUrl = NormalizeBridgeBaseUrl(bridgeBaseUrl);
        this._bridgeFilePath = bridgeFilePath;
    }

    public void Dispose() => this._httpClient.Dispose();

    public void SendInputEvent(ClickyInputEventPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        _ = Task.Run(async () =>
        {
            try
            {
                using var response = await this._httpClient.PostAsJsonAsync(
                    new Uri(new Uri(this.ResolveBridgeBaseUrl(), UriKind.Absolute), "api/input-events"),
                    payload,
                    JsonOptions).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
            }
            catch
            {
            }
        });
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
                    this._bridgeBaseUrl = NormalizeBridgeBaseUrl(fileValue);
                }
            }
        }
        catch
        {
        }

        return this._bridgeBaseUrl;
    }

    private static string NormalizeBridgeBaseUrl(string bridgeBaseUrl) =>
        (string.IsNullOrWhiteSpace(bridgeBaseUrl) ? "http://127.0.0.1:65439" : bridgeBaseUrl.Trim()).TrimEnd('/') + "/";
}
