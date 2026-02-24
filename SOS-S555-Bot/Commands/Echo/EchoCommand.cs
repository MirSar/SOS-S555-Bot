using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace SOSS555Bot.Commands.EchoCommand
{
    /// <summary>
    /// This class contains the command to echo back a message.
    /// </summary>
    /// <remarks>
    /// The command is triggered by the user typing "!echo <phrase>" in the chat.
    /// The bot will respond with the same phrase.
    /// </remarks>
    [Group("echo")]
    [Alias("e")]
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.SendMessages)]
    public class EchoCommand : ModuleBase<SocketCommandContext>
    {
        [Command("echo")]
        [Summary("Echoes back what was said")]
        public async Task ExecuteAsync([Remainder][Summary("A phrase")] string phrase)
        {
            if (string.IsNullOrEmpty(phrase))
            {
                await ReplyAsync($"Usage: !echo <phrase>");
                return;
            }
            // Check if the phrase is too long
            if (phrase.Length > 2000)
            {
                await ReplyAsync($"The phrase is too long. Please keep it under 2000 characters.");
                return;
            }

            Console.WriteLine("Echoing: " + phrase);
            // Send the message to the channel
            await ReplyAsync(phrase);
        }
    }
}