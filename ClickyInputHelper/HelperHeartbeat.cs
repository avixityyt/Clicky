namespace ClickyInputHelper;

using System;
using System.IO;
using System.Threading;

internal sealed class HelperHeartbeat : IDisposable
{
    private readonly string _heartbeatFilePath;
    private readonly Timer _timer;
    private bool _disposed;

    public HelperHeartbeat(string heartbeatFilePath)
    {
        this._heartbeatFilePath = heartbeatFilePath;
        EnsureDirectoryExists(this._heartbeatFilePath);
        this.WriteHeartbeat();
        this._timer = new Timer(_ => this.WriteHeartbeat(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;
        this._timer.Dispose();
    }

    private void WriteHeartbeat()
    {
        try
        {
            File.WriteAllText(this._heartbeatFilePath, $"{DateTimeOffset.UtcNow:O}|{Environment.ProcessId}");
        }
        catch
        {
        }
    }

    private static void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
