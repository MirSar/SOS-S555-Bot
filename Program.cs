using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SOSS555Bot
{
    internal class Program
    {
        private static void Main(string[] args) =>
            MainAsync(args).GetAwaiter().GetResult();

        private static async Task MainAsync(string[] args)
        {
            // Build base configuration (appsettings + user secrets)
            var baseConfig = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
                .Build();

            // Prefer reading an optional external JSON config located alongside the token file.
            // Determine token file location first (may be overridden by appsettings)
            var tokenFilePath = baseConfig["DiscordTokenFile"] ??
                                @"D:\Data\Dropbox\Software\GIT\SOS-S555-Bot\Discord-token.txt";

            // If there's a JSON config in the same folder as the token file, load it too (overrides appsettings)
            string externalConfigPath = null;
            try
            {
                var tokenDir = Path.GetDirectoryName(tokenFilePath) ?? @"D:\Data\Dropbox\Software\GIT\SOS-S555-Bot";
                var candidate = Path.Combine(tokenDir, "botsettings.json");
                if (File.Exists(candidate)) externalConfigPath = candidate;
            }
            catch { }

            string tokenFromFile = null;
            try
            {
                if (File.Exists(tokenFilePath))
                {
                    tokenFromFile = File.ReadAllText(tokenFilePath).Trim();
                    if (string.IsNullOrWhiteSpace(tokenFromFile))
                        tokenFromFile = null;
                }
                else
                {
                    Console.Error.WriteLine($"Discord token file not found at '{tokenFilePath}'.");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to read token file '{tokenFilePath}': {ex.Message}");
            }

            // Compose final configuration: baseConfig, then optional external config, then token override if present
            IConfiguration configuration;
            var builder = new ConfigurationBuilder()
                .AddConfiguration(baseConfig);

            if (!string.IsNullOrWhiteSpace(externalConfigPath))
                builder.AddJsonFile(externalConfigPath, optional: true, reloadOnChange: true);

            if (!string.IsNullOrWhiteSpace(tokenFromFile))
            {
                var dict = new Dictionary<string, string> { { "DiscordToken", tokenFromFile } };
                builder.AddInMemoryCollection(dict);
            }

            configuration = builder.Build();

            // Initialize AppConfig (paths and token file defaults)
            AppConfig.Initialize(configuration);

            // Diagnostic: only indicate presence, not the token value
            Console.WriteLine("DiscordToken present in configuration: " + (!string.IsNullOrWhiteSpace(configuration["DiscordToken"])));

            var serviceProvider = new ServiceCollection()
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton<IBot, Bot>()
                .BuildServiceProvider();

            // Global exception hooks
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Console.Error.WriteLine("Unhandled exception: " + (e.ExceptionObject?.ToString() ?? "<null>"));
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Console.Error.WriteLine("Unobserved task exception: " + e.Exception.ToString());
                e.SetObserved();
            };

            try
            {
                IBot bot = serviceProvider.GetRequiredService<IBot>();

                // StartAsync only returns after the client is ready (or throws)
                await bot.StartAsync(serviceProvider);

                // Console detection
                bool hasConsole = false;
                try
                {
                    hasConsole = !Console.IsInputRedirected && Environment.UserInteractive;
                }
                catch
                {
                    hasConsole = false;
                }

                Console.WriteLine("Connected to Discord");
                if (hasConsole)
                    Console.WriteLine("Press Ctrl+C to exit.");
                else
                    Console.WriteLine("No interactive console detected; waiting for process exit to shut down.");

                var exitTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

                if (hasConsole)
                {
                    Console.CancelKeyPress += (sender, eventArgs) =>
                    {
                        eventArgs.Cancel = true;
                        exitTcs.TrySetResult(null);
                    };
                }

                AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) =>
                {
                    exitTcs.TrySetResult(null);
                };

                await exitTcs.Task;

                Console.WriteLine("Shutting down...");
                await bot.StopAsync();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Failed to start bot:");
                Console.Error.WriteLine(exception.ToString());
                Environment.Exit(-1);
            }
        }
    }
}