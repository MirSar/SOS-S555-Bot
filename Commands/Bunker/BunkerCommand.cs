using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SOSS555Bot.Commands.Bunker
{
    /// <summary>
    /// Commands for bunker registration and tracking.
    /// Usage examples:
    ///  - !bunker register F1
    ///  - !bunker unregister F1
    ///  - !bunker list
    /// </summary>
    public class BunkerCommand : ModuleBase<SocketCommandContext>
    {
        private static readonly BunkerStore Store = BunkerStore.Load();
        
        private static readonly string[] ValidBunkers = 
        {
            "F1", "F2", "F3", "F4", "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9", "B10", "B11", "B12"
        };

        private const int MaxRegistrationsPerUser = 3;

        private bool CallerHasAdminRole()
        {
            if (Context.User is SocketGuildUser gu)
            {
                return gu.Roles.Any(r => string.Equals(r.Name, "R4", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(r.Name, "R5", StringComparison.OrdinalIgnoreCase));
            }
            return false;
        }

        private string GetAllianceTagFromUser()
        {
            if (Context.User is SocketGuildUser gu)
            {
                // Find a 3-letter role (case-insensitive, uppercase output)
                var allianceRole = gu.Roles.FirstOrDefault(r => r.Name.Length == 3 && r.Name.All(char.IsLetter));
                if (allianceRole != null)
                    return allianceRole.Name.ToUpper();
            }
            return "UNKNOWN";
        }

        [Summary("Bunker command group")]
        [Command("bunker")]
        [Alias("bunk")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.SendMessages)]
        public async Task ExecuteAsync([Remainder][Summary("A message or subcommand")] string message = null)
        {
            try
            {
                // Check for help command
                if (!string.IsNullOrWhiteSpace(message) && message.Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    await ShowHelp();
                    return;
                }

                // Otherwise post the bunker registration message with reactions
                await PostBunkerRegistrationMessage();
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error: {ex.Message}");
            }
        }

        private async Task ShowHelp()
        {
            var builder = new StringBuilder();
            builder.AppendLine("**Bunker Registration Help**");
            builder.AppendLine("**Required Role:** `R4` or `R5` (admin only)");
            builder.AppendLine();
            builder.AppendLine("**Command:**");
            builder.AppendLine("`!bunker` or `!bunk` - Post a bunker registration message");
            builder.AppendLine();
            builder.AppendLine("**How It Works:**");
            builder.AppendLine("1. Use `!bunker` to post the registration board");
            builder.AppendLine("2. React with emoji to register/unregister for bunkers");
            builder.AppendLine("3. Each user can register for up to 3 bunkers");
            builder.AppendLine("4. If you try to register for a 4th, your oldest registration is removed");
            builder.AppendLine("5. Alliance tags come from your Discord role (3-letter role name)");
            builder.AppendLine();
            builder.AppendLine("**Available Bunkers:**");
            builder.AppendLine("**Front:** F1 F2 F3 F4");
            builder.AppendLine("**Back:** B1 B2 B3 B4 B5 B6 B7 B8 B9 B10 B11 B12");
            builder.AppendLine();
            builder.AppendLine("**Reaction Emojis:**");
            builder.AppendLine("Front: 1️⃣ 2️⃣ 3️⃣ 4️⃣ (for F1-F4)");
            builder.AppendLine("Back: 5️⃣ 6️⃣ 7️⃣ 8️⃣ 9️⃣ 🔟 🇦 🇧 🇨 🇩 🇪 🇫 (for B1-B12)");
            builder.AppendLine();
            builder.AppendLine("**Rules:**");
            builder.AppendLine("- Only `R4` and `R5` roles can use this command");
            builder.AppendLine("- Multiple users can register for the same bunker (competing alliances allowed)");
            builder.AppendLine("- Max 3 registrations per user (oldest is removed if you register for a 4th)");
            builder.AppendLine("- Alliance tag is determined by your 3-letter Discord role");
            await ReplyAsync(builder.ToString());
        }

        private async Task PostBunkerRegistrationMessage()
        {
            var builder = new StringBuilder();
            builder.AppendLine("**Bunker Registration** - React to register/unregister for bunkers (max 3 per user)");
            builder.AppendLine();
            
            var bunkerEmojis = BunkerManager.BunkerEmojis;
            var bunkerList = BunkerManager.BunkerList;

            builder.AppendLine("**Front:**");
            for (int i = 0; i < 4; i++)
            {
                var registrations = Store.GetAllRegistrations();
                var alliances = registrations.ContainsKey(bunkerList[i])
                    ? string.Join(", ", registrations[bunkerList[i]])
                    : "(empty)";
                builder.AppendLine($"{bunkerEmojis[i]} {bunkerList[i]} - {alliances}");
            }
            builder.AppendLine();

            builder.AppendLine("**Back:**");
            for (int i = 4; i < bunkerList.Length; i++)
            {
                var registrations = Store.GetAllRegistrations();
                var alliances = registrations.ContainsKey(bunkerList[i])
                    ? string.Join(", ", registrations[bunkerList[i]])
                    : "(empty)";
                builder.AppendLine($"{bunkerEmojis[i]} {bunkerList[i]} - {alliances}");
            }

            var msg = await ReplyAsync(builder.ToString());

            // Add all reaction options
            foreach (var emoji in bunkerEmojis)
            {
                await msg.AddReactionAsync(new Emoji(emoji));
            }

            BunkerManager.RegisterMessage(msg.Id, msg);
        }

        // Bunker registration manager for handling reactions
        public static class BunkerManager
        {
            public static readonly string[] BunkerEmojis = 
            {
                "1️⃣", "2️⃣", "3️⃣", "4️⃣",           // F1-F4
                "5️⃣", "6️⃣", "7️⃣", "8️⃣", "9️⃣", "🔟",  // B1-B6
                "🇦", "🇧", "🇨", "🇩", "🇪", "🇫"   // B7-B12
            };

            public static readonly string[] BunkerList =
            {
                "F1", "F2", "F3", "F4", "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9", "B10", "B11", "B12"
            };

            private static readonly Dictionary<ulong, IUserMessage> _registrationMessages
                = new Dictionary<ulong, IUserMessage>();

            public static void RegisterMessage(ulong messageId, IUserMessage message)
            {
                _registrationMessages[messageId] = message;
            }

            public static Task<(bool Success, string BunkerRemoved)> TryHandleReactionAsync(SocketReaction reaction, IUserMessage message, string allianceTag)
            {
                if (!_registrationMessages.ContainsKey(reaction.MessageId))
                    return Task.FromResult((false, (string)null));

                // map to guild id from the message's channel
                var guild = (message.Channel as SocketGuildChannel)?.Guild;
                if (guild == null) return Task.FromResult((false, (string)null));
                var guildId = guild.Id;

                // Map emoji to bunker index
                var emoji = reaction.Emote.Name;
                int bunkerIndex = Array.IndexOf(BunkerEmojis, emoji);
                if (bunkerIndex < 0)
                    return Task.FromResult((false, (string)null));

                var bunker = BunkerList[bunkerIndex];
                var userId = reaction.UserId;

                // Toggle registration: if already registered, unregister; otherwise register
                if (Store.IsUserRegisteredForBunker(guildId, userId, bunker))
                {
                    Store.Unregister(guildId, bunker, userId);
                    return Task.FromResult((true, (string)null)); // Successfully unregistered
                }
                else
                {
                    // If user at limit, remove oldest registration first and return which one was removed
                    if (Store.GetUserRegistrationCount(guildId, userId) >= MaxRegistrationsPerUser)
                    {
                        var removed = Store.RemoveOldestRegistrationForUser(guildId, userId);
                        // After removal, proceed to register the new bunker
                        Store.Register(guildId, bunker, userId, allianceTag);
                        return Task.FromResult((true, removed));
                    }

                    Store.Register(guildId, bunker, userId, allianceTag);
                    return Task.FromResult((true, (string)null)); // Successfully registered
                }
            }

            

            public static async Task UpdateMessageDisplayAsync(ulong messageId)
            {
                if (!_registrationMessages.TryGetValue(messageId, out var message))
                    return;
                var guild = (message.Channel as SocketGuildChannel)?.Guild;
                if (guild == null) return;
                var content = BuildRegistrationContent(guild.Id);
                try
                {
                    await message.ModifyAsync(m => m.Content = content);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Bunker] Failed to update message: {ex.Message}");
                }
            }

            private static string BuildRegistrationContent(ulong guildId)
            {
                var builder = new StringBuilder();
                builder.AppendLine("**Bunker Registration** - React to register/unregister for bunkers (max 3 per user)");
                builder.AppendLine();
                
                var registrations = Store.GetAllRegistrations(guildId);

                builder.AppendLine("**Front:**");
                for (int i = 0; i < 4; i++)
                {
                    var alliances = registrations.ContainsKey(BunkerList[i])
                        ? string.Join(", ", registrations[BunkerList[i]])
                        : "(empty)";
                    builder.AppendLine($"{BunkerEmojis[i]} {BunkerList[i]} - {alliances}");
                }
                builder.AppendLine();

                builder.AppendLine("**Back:**");
                for (int i = 4; i < BunkerList.Length; i++)
                {
                    var alliances = registrations.ContainsKey(BunkerList[i])
                        ? string.Join(", ", registrations[BunkerList[i]])
                        : "(empty)";
                    builder.AppendLine($"{BunkerEmojis[i]} {BunkerList[i]} - {alliances}");
                }

                return builder.ToString();
            }
        }

        // Simple CSV-backed store
        private class BunkerStore
        {
            private static readonly string DataDir;
            private static readonly string RegistrationsFile;
            private static readonly object Sync = new object();
            private const string Delim = "|";

            // In-memory cache: key = "{guildId}:{bunker}" -> List<(userId, allianceTag, timestampMs)>
            private Dictionary<string, List<(ulong userId, string allianceTag, long ts)>> Registrations
                = new Dictionary<string, List<(ulong, string, long)>>(StringComparer.OrdinalIgnoreCase);

            static BunkerStore()
            {
                DataDir = AppConfig.DataDir ?? Path.Combine(AppContext.BaseDirectory, "data");
                RegistrationsFile = Path.Combine(DataDir, "bunker_registrations.csv");
            }

            public static BunkerStore Load()
            {
                var store = new BunkerStore();
                store.Load_();
                return store;
            }

            private void Load_()
            {
                lock (Sync)
                {
                    Registrations.Clear();
                    if (!File.Exists(RegistrationsFile))
                        return;

                    try
                    {
                        using (var reader = new StreamReader(RegistrationsFile, Encoding.UTF8))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                if (string.IsNullOrWhiteSpace(line))
                                    continue;

                                var parts = line.Split(Delim);
                                if (parts.Length < 4)
                                    continue;

                                // expected format: guildId|bunker|userId|allianceTag|ts
                                if (!ulong.TryParse(parts[0].Trim(), out var guildId))
                                    continue;
                                var bunker = parts[1].Trim();
                                if (!ulong.TryParse(parts[2].Trim(), out var userId))
                                    continue;

                                var allianceTag = parts[3].Trim();
                                long ts = 0;
                                if (parts.Length >= 5 && long.TryParse(parts[4].Trim(), out var parsedTs))
                                    ts = parsedTs;

                                var key = $"{guildId}:{bunker}";
                                if (!Registrations.ContainsKey(key))
                                    Registrations[key] = new List<(ulong, string, long)>();

                                Registrations[key].Add((userId, allianceTag, ts));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[BunkerStore] Failed to load: {ex.Message}");
                    }
                }
            }

            private void Persist()
            {
                try
                {
                    lock (Sync)
                    {
                        Directory.CreateDirectory(DataDir);
                        using (var writer = new StreamWriter(RegistrationsFile, false, Encoding.UTF8))
                        {
                            foreach (var kvp in Registrations)
                            {
                                var key = kvp.Key; // format: guildId:bunker
                                var idx = key.IndexOf(':');
                                if (idx <= 0) continue;
                                var guildStr = key.Substring(0, idx);
                                var bunker = key.Substring(idx + 1);
                                foreach (var (userId, allianceTag, ts) in kvp.Value)
                                {
                                    writer.WriteLine($"{guildStr}{Delim}{bunker}{Delim}{userId}{Delim}{allianceTag}{Delim}{ts}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[BunkerStore] Failed to persist: {ex.Message}");
                }
            }

            public void Register(ulong guildId, string bunker, ulong userId, string allianceTag)
            {
                lock (Sync)
                {
                    var key = $"{guildId}:{bunker}";
                    if (!Registrations.ContainsKey(key))
                        Registrations[key] = new List<(ulong, string, long)>();

                    // Only add if not already registered for this bunker.
                    if (Registrations[key].Any(x => x.userId == userId))
                        return;

                    Registrations[key].Add((userId, allianceTag, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                    Persist();
                }
            }

            /// <summary>
            /// Removes the oldest registration (by timestamp) for the given user across all bunkers.
            /// Returns the bunker name that was removed, or null if none removed.
            /// </summary>
            public string RemoveOldestRegistrationForUser(ulong guildId, ulong userId)
            {
                lock (Sync)
                {
                    string foundKey = null;
                    string foundBunker = null;
                    long oldestTs = long.MaxValue;
                    var prefix = $"{guildId}:";
                    foreach (var kvp in Registrations.Where(k => k.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (var entry in kvp.Value.Where(x => x.userId == userId))
                        {
                            if (entry.ts < oldestTs)
                            {
                                oldestTs = entry.ts;
                                foundKey = kvp.Key;
                                var idx = kvp.Key.IndexOf(':');
                                foundBunker = idx >= 0 ? kvp.Key.Substring(idx + 1) : kvp.Key;
                            }
                        }
                    }

                    if (foundKey != null)
                    {
                        Registrations[foundKey].RemoveAll(x => x.userId == userId);
                        Persist();
                    }

                    return foundBunker;
                }
            }

            public bool Unregister(ulong guildId, string bunker, ulong userId)
            {
                lock (Sync)
                {
                    var key = $"{guildId}:{bunker}";
                    if (!Registrations.ContainsKey(key))
                        return false;

                    var removed = Registrations[key].RemoveAll(x => x.userId == userId) > 0;
                    if (removed)
                        Persist();

                    return removed;
                }
            }

            public int GetUserRegistrationCount(ulong guildId, ulong userId)
            {
                lock (Sync)
                {
                    var prefix = $"{guildId}:";
                    return Registrations.Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .Sum(kv => kv.Value.Count(x => x.userId == userId));
                }
            }

            public bool IsUserRegisteredForBunker(ulong guildId, ulong userId, string bunker)
            {
                lock (Sync)
                {
                    var key = $"{guildId}:{bunker}";
                    return Registrations.ContainsKey(key)
                        && Registrations[key].Any(x => x.userId == userId);
                }
            }

            public Dictionary<string, List<string>> GetAllRegistrations(ulong guildId)
            {
                lock (Sync)
                {
                    var result = new Dictionary<string, List<string>>();
                    var prefix = $"{guildId}:";
                    foreach (var kvp in Registrations.Where(k => k.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    {
                        var bunker = kvp.Key.Substring(prefix.Length);
                        var alliances = kvp.Value
                            .Select(x => x.allianceTag)
                            .Distinct()
                            .OrderBy(x => x)
                            .ToList();
                        result[bunker] = alliances;
                    }
                    return result;
                }
            }
        }
    }
}
