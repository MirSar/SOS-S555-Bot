using System.Reflection;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace SOSS555Bot
{
    public class Bot : IBot
    {
        private readonly IConfiguration _configuration;
        private readonly DiscordSocketClient _client;
        private readonly CommandService _commands;
        private IServiceProvider _serviceProvider;

        public Bot(IConfiguration configuration)
        {
            _configuration = configuration;

            DiscordSocketConfig config = new()
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
            };

            _client = new DiscordSocketClient(config);
            _commands = new CommandService();

            // Basic runtime diagnostic handlers
            _client.Log += async (logMsg) =>
            {
                // print discord.net internal logs to console for troubleshooting
                Console.Error.WriteLine($"[DiscordLog] {logMsg.Severity} :: {logMsg.Source} :: {logMsg.Exception?.ToString() ?? logMsg.Message}");
                await Task.CompletedTask;
            };

            _client.Disconnected += async (exception) =>
            {
                Console.Error.WriteLine("[Discord] Disconnected from Gateway" + (exception != null ? $": {exception.Message}" : string.Empty));
                await Task.CompletedTask;
            };
        }

        /// <summary>
        /// Starts the bot, waits for Ready, and sets a small presence.
        /// Throws descriptive exceptions on failure (token missing, login error, timeout).
        /// </summary>
        public async Task StartAsync(IServiceProvider services)
        {
            _serviceProvider = services;

            await _commands.AddModulesAsync(Assembly.GetExecutingAssembly(), _serviceProvider);

            var token = _configuration["DiscordToken"];
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Discord token is missing from configuration. Ensure you set DiscordToken in appsettings/user-secrets.");

            var readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Task ReadyHandler()
            {
                readyTcs.TrySetResult(true);
                return Task.CompletedTask;
            }

            _client.Ready += ReadyHandler;

            try
            {
                try
                {
                    await _client.LoginAsync(TokenType.Bot, token);
                }
                catch (Exception ex)
                {
                    // More informative failure if login fails (invalid token, network)
                    throw new InvalidOperationException("Failed to login to Discord. Check token and network.", ex);
                }

                try
                {
                    await _client.StartAsync();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Failed to start Discord client.", ex);
                }

                // Wait for Ready with a timeout
                var finished = await Task.WhenAny(readyTcs.Task, Task.Delay(TimeSpan.FromSeconds(30)));
                if (finished != readyTcs.Task)
                {
                    // Timeout - ensure client stopped and surface clear error
                    await _client.StopAsync();
                    throw new InvalidOperationException("Timed out waiting for Discord Ready event. Verify network, token validity, and gateway intents.");
                }

                // Set a simple presence so the bot is visible as online (optional)
                try
                {
                    await _client.SetGameAsync("SOSS555Bot", type: ActivityType.Playing);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[Discord] Warning: failed to set presence: " + ex.Message);
                }

                // Hook command handling after successful connect
                _client.MessageReceived += HandleCommandAsync;
            }
            finally
            {
                // Always remove temporary Ready handler
                _client.Ready -= ReadyHandler;
            }
        }

        /// <summary>
        /// Stops the bot and logs out from Discord.
        /// </summary>
        /// <remarks>
        /// This method logs out the bot from Discord and stops the client.
        /// </remarks>
        public async Task StopAsync()
        {
            if (_client != null)
            {
                await _client.LogoutAsync();
                await _client.StopAsync();
            }
        }

        /// <summary>
        /// Handles incoming messages and checks if they are commands.
        /// </summary>
        /// <param name="arg">The incoming message.</param>
        /// <remarks>
        /// This method checks if the message starts with a command prefix and if the user has the required role.
        /// If the user has the required role, it executes the command using the CommandService.
        /// </remarks>
        private async Task HandleCommandAsync(SocketMessage arg)
        {
            // Ignore messages from bots
            if (arg is not SocketUserMessage message || message.Author.IsBot)
            {
                // Console.WriteLine("Ignoring message from bot: " + arg.Author.ToString());
                return;
            }
            // Check if the message is a command
            int position = 0;
            if (message.HasCharPrefix('!', ref position))
            {
                // Execute the command if it exists in the ServiceCollection
                await _commands.ExecuteAsync(
                    new SocketCommandContext(_client, message),
                    position,
                    _serviceProvider);

                return;
            }
        }
    }
}