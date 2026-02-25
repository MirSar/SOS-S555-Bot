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

            // Determine token file location:
            //  - First check config key "DiscordTokenFile"
            //  - If not set, default to the path you requested
            var tokenFilePath = baseConfig["DiscordTokenFile"] ??
                                @"D:\Data\Dropbox\Software\GIT\SOS-S555-Bot\Discord-token.txt";

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

            // Compose final configuration: baseConfig overridden by tokenFromFile (if present)
            IConfiguration configuration;
            if (!string.IsNullOrWhiteSpace(tokenFromFile))
            {
                var dict = new Dictionary<string, string> { { "DiscordToken", tokenFromFile } };
                configuration = new ConfigurationBuilder()
                    .AddConfiguration(baseConfig)
                    .AddInMemoryCollection(dict)
                    .Build();
            }
            else if (!string.IsNullOrWhiteSpace(baseConfig["DiscordToken"]))
            {
                // token present in appsettings/user-secrets — accept it but warn
                Console.Error.WriteLine("Warning: Discord token found in configuration sources (appsettings/user-secrets). " +
                                        "Consider moving it to an external file and set 'DiscordTokenFile' in configuration.");
                configuration = baseConfig;
            }
            else
            {
                Console.Error.WriteLine("No Discord token found. Please place the token in a file and/or set 'DiscordTokenFile' in appsettings or user-secrets.");
                Console.Error.WriteLine($"Expected token file (example): {tokenFilePath}");
                Environment.Exit(-1);
                return;
            }

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