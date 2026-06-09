namespace Loupedeck.ClickyPlugin;

using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Reflection;
using System.Threading;

internal sealed class ClickyInputHelperController : IDisposable
{
    private static readonly string BridgeFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Logi",
        "LogiPluginService",
        "Temp",
        "ClickyInputHelper.bridge");
    private static readonly string HeartbeatFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Logi",
        "LogiPluginService",
        "Temp",
        "ClickyInputHelper.heartbeat");
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ClickyInputHelper";
    private static readonly string LegacyStartupCommandFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "ClickyInputHelper.cmd");
    private static readonly string LegacyStartupScriptFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "ClickyInputHelper.vbs");
    private static readonly TimeSpan HeartbeatFreshnessWindow = TimeSpan.FromSeconds(4);

    private bool _disposed;

    public static bool IsHelperResponsive()
    {
        try
        {
            if (!File.Exists(HeartbeatFilePath))
            {
                return false;
            }

            var lastWriteUtc = File.GetLastWriteTimeUtc(HeartbeatFilePath);
            return DateTime.UtcNow - lastWriteUtc <= HeartbeatFreshnessWindow;
        }
        catch
        {
            return false;
        }
    }

    public void Start(string bridgeBaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bridgeBaseUrl);

        WriteBridgeFile(bridgeBaseUrl);

        var helperExe = ResolveHelperExecutablePath();
        if (!File.Exists(helperExe))
        {
            throw new FileNotFoundException("Clicky input helper executable was not found.", helperExe);
        }

        var helperArguments = BuildHelperArguments(bridgeBaseUrl.TrimEnd('/'), BridgeFilePath, HeartbeatFilePath);
        EnsureAutoStart(helperExe, helperArguments);

        if (IsHelperResponsive())
        {
            Logger.Info("Reusing existing Clicky input helper instance.");
            return;
        }

        LaunchHelper(helperExe, helperArguments);
        WaitForHeartbeat();

        if (!IsHelperResponsive())
        {
            throw new InvalidOperationException("Clicky input helper did not stay running after launch.");
        }

        Logger.Info($"Clicky input helper started from '{helperExe}'.");
    }

    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;
    }

    private static string ResolveHelperExecutablePath()
    {
        foreach (var pluginDirectory in EnumerateCandidatePluginDirectories())
        {
            var helperExecutablePath = Path.Combine(pluginDirectory, "tools", "ClickyInputHelper", "ClickyInputHelper.exe");
            if (File.Exists(helperExecutablePath))
            {
                return helperExecutablePath;
            }
        }

        throw new InvalidOperationException("Could not resolve the Clicky plugin directory.");
    }

    private static string[] EnumerateCandidatePluginDirectories()
    {
        var candidates = new System.Collections.Generic.List<string>();

        AddCandidateDirectory(candidates, AppContext.BaseDirectory);
        AddCandidateDirectory(candidates, Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
        AddCandidateDirectory(candidates, TryResolvePluginLinkDirectory());

        var expanded = new System.Collections.Generic.List<string>();
        foreach (var candidate in candidates)
        {
            expanded.Add(candidate);
            AddCandidateDirectory(expanded, Path.Combine(candidate, "bin"));
            AddCandidateDirectory(expanded, Path.Combine(candidate, ".."));
        }

        return expanded.ToArray();
    }

    private static void AddCandidateDirectory(System.Collections.Generic.ICollection<string> directories, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        foreach (var existing in directories)
        {
            if (string.Equals(existing, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        directories.Add(fullPath);
    }

    private static string? TryResolvePluginLinkDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var linkPath = Path.Combine(localAppData, "Logi", "LogiPluginService", "Plugins", "ClickyPlugin.link");
        if (!File.Exists(linkPath))
        {
            return null;
        }

        var linkedDirectory = File.ReadAllText(linkPath).Trim();
        if (string.IsNullOrWhiteSpace(linkedDirectory))
        {
            return null;
        }

        return Path.GetFullPath(linkedDirectory);
    }

    private static void WriteBridgeFile(string bridgeBaseUrl)
    {
        var directory = Path.GetDirectoryName(BridgeFilePath)
            ?? throw new InvalidOperationException("Could not resolve Clicky input helper bridge file directory.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(BridgeFilePath, bridgeBaseUrl.Trim().TrimEnd('/'));
    }

    private static void EnsureAutoStart(string helperExe, string helperArguments)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (File.Exists(LegacyStartupCommandFilePath))
        {
            File.Delete(LegacyStartupCommandFilePath);
        }

        if (File.Exists(LegacyStartupScriptFilePath))
        {
            File.Delete(LegacyStartupScriptFilePath);
        }

        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (runKey == null)
        {
            throw new InvalidOperationException("Could not create the Clicky input helper autorun registry key.");
        }

        runKey.SetValue(RunValueName, BuildCommandLine(helperExe, helperArguments), RegistryValueKind.String);
    }

    private static void LaunchHelper(string helperExe, string helperArguments)
    {
        var helperDirectory = Path.GetDirectoryName(helperExe)
            ?? throw new InvalidOperationException("Could not resolve the Clicky input helper directory.");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = helperExe,
            Arguments = helperArguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = helperDirectory,
        }) ?? throw new InvalidOperationException("Failed to start the Clicky input helper process.");

        process.WaitForExit(1000);
    }

    private static void WaitForHeartbeat()
    {
        var timeoutAt = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < timeoutAt)
        {
            if (IsHelperResponsive())
            {
                return;
            }

            Thread.Sleep(100);
        }
    }

    private static string BuildHelperArguments(string bridgeBaseUrl, string bridgeFilePath, string heartbeatFilePath) =>
        string.Join(
            " ",
            "--bridge-base",
            QuoteForCommandLine(bridgeBaseUrl),
            "--bridge-file",
            QuoteForCommandLine(bridgeFilePath),
            "--heartbeat-file",
            QuoteForCommandLine(heartbeatFilePath));

    private static string BuildCommandLine(string executablePath, string arguments) =>
        $"{QuoteForCommandLine(executablePath)} {arguments}";

    private static string QuoteForCommandLine(string value)
    {
        var escaped = value.Replace("\"", "\\\"", StringComparison.Ordinal);
        return "\"" + escaped + "\"";
    }
}
