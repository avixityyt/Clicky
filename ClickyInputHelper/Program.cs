namespace ClickyInputHelper;

using System;
using System.IO;
using System.Windows.Forms;

internal static class Program
{
    private static readonly string StartupLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Logi",
        "LogiPluginService",
        "Temp",
        "ClickyInputHelper.startup.log");

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            var options = HelperOptions.Parse(args);
            using var mutex = new System.Threading.Mutex(true, @"Local\ClickyInputHelper", out var createdNew);
            if (!createdNew)
            {
                return;
            }

            WriteStartupLog("Starting.");
            ApplicationConfiguration.Initialize();
            using var context = new HelperApplicationContext(options);
            WriteStartupLog("Running.");
            Application.Run(context);
        }
        catch (Exception ex)
        {
            WriteStartupLog(ex.ToString());
            throw;
        }
    }

    private static void WriteStartupLog(string message)
    {
        var directory = Path.GetDirectoryName(StartupLogPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.AppendAllText(StartupLogPath, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
    }
}
