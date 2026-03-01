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
        public static string ConfigDir { get; private set; }
        public static string StoresDir { get; private set; }
        public static ulong[] JoinedGuildIds { get; private set; } = Array.Empty<ulong>();

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

            var cfgDir = config["Paths:ConfigDir"];
            ConfigDir = !string.IsNullOrWhiteSpace(cfgDir) ? cfgDir : Path.Combine(BaseDir, "config");

            var stores = config["Paths:StoresDir"];
            StoresDir = !string.IsNullOrWhiteSpace(stores) ? stores : Path.Combine(DataDir, "stores");

            // Discord token file: prefer explicit config, otherwise default alongside BaseDir
            var tokenFile = config["DiscordTokenFile"];
            DiscordTokenFile = !string.IsNullOrWhiteSpace(tokenFile) ? tokenFile : Path.Combine(BaseDir, "Discord-token.txt");

            // Ensure directories exist
            try { Directory.CreateDirectory(DataDir); } catch { }
            try { Directory.CreateDirectory(LogsDir); } catch { }
            try { Directory.CreateDirectory(ConfigDir); } catch { }
            try { Directory.CreateDirectory(StoresDir); } catch { }

            // Joined guilds list (optional) - read as array of numbers
            try
            {
                var guildSection = config.GetSection("Servers:GuildIds");
                if (guildSection.Exists())
                {
                    var children = guildSection.GetChildren();
                    var list = new System.Collections.Generic.List<ulong>();
                    foreach (var ch in children)
                    {
                        if (ulong.TryParse(ch.Value, out var gid))
                            list.Add(gid);
                    }
                    JoinedGuildIds = list.ToArray();
                }
            }
            catch { JoinedGuildIds = Array.Empty<ulong>(); }
        }
    }
}
