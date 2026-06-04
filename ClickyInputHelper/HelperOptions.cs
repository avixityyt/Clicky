namespace ClickyInputHelper;

using System;

internal sealed class HelperOptions
{
    public const int DefaultDiscoveryPort = 65438;

    public string BridgeBaseUrl { get; private set; } = "http://127.0.0.1:65439";

    public string BridgeFilePath { get; private set; } = DefaultBridgeFilePath;

    public string HeartbeatFilePath { get; private set; } = DefaultHeartbeatFilePath;

    public static string DefaultBridgeFilePath =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Logi",
            "LogiPluginService",
            "Temp",
            "ClickyInputHelper.bridge");

    public static string DefaultHeartbeatFilePath =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Logi",
            "LogiPluginService",
            "Temp",
            "ClickyInputHelper.heartbeat");

    public static HelperOptions Parse(string[] args)
    {
        var options = new HelperOptions();

        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--bridge-base", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("Missing value for --bridge-base.");
                }

                options.BridgeBaseUrl = args[index + 1].Trim().TrimEnd('/');
                index++;
                continue;
            }

            if (string.Equals(args[index], "--bridge-file", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("Missing value for --bridge-file.");
                }

                options.BridgeFilePath = args[index + 1].Trim();
                index++;
                continue;
            }

            if (!string.Equals(args[index], "--heartbeat-file", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= args.Length)
            {
                throw new ArgumentException("Missing value for --heartbeat-file.");
            }

            options.HeartbeatFilePath = args[index + 1].Trim();
            index++;
        }

        return options;
    }
}
