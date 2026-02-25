using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace SOSS555Bot
{
    public static class AppConfig
    {
        public static string BaseDir { get; private set; }
        public static string DataDir { get; private set; }
        public static string LogsDir { get; private set; }
        public static string DiscordTokenFile { get; private set; }

        public static void Initialize(IConfiguration config)
        {
            // Determine base directory: prefer explicit Paths:BaseDir, fall back to the conventional shared folder
            var configuredBase = config["Paths:BaseDir"];
            if (!string.IsNullOrWhiteSpace(configuredBase))
                BaseDir = configuredBase;
            else
                BaseDir = @"D:\Data\Dropbox\Software\GIT\SOS-S555-Bot";

            // DataDir and LogsDir can be explicitly set, otherwise derived from BaseDir
            var data = config["Paths:DataDir"];
            DataDir = !string.IsNullOrWhiteSpace(data) ? data : Path.Combine(BaseDir, "data");

            var logs = config["Paths:LogsDir"];
            LogsDir = !string.IsNullOrWhiteSpace(logs) ? logs : Path.Combine(BaseDir, "logs");

            // Discord token file: prefer explicit config, otherwise default alongside BaseDir
            var tokenFile = config["DiscordTokenFile"];
            DiscordTokenFile = !string.IsNullOrWhiteSpace(tokenFile) ? tokenFile : Path.Combine(BaseDir, "Discord-token.txt");

            // Ensure directories exist
            try { Directory.CreateDirectory(DataDir); } catch { }
            try { Directory.CreateDirectory(LogsDir); } catch { }
        }
    }
}
