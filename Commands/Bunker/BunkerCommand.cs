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
        public async Task ExecuteAsync([Remainder][Summary("A message or subcommand")] string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    await ReplyAsync("Usage: !bunker <register|unregister|list> [bunker]");
                    return;
                }

                var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var subcommand = parts[0].ToLowerInvariant();

                switch (subcommand)
                {
                    case "register":
                        await HandleRegister(parts);
                        break;
                    case "unregister":
                    case "unreg":
                        await HandleUnregister(parts);
                        break;
                    case "list":
                        await HandleList();
                        break;
                    default:
                        await ReplyAsync("Unknown subcommand. Usage: !bunker <register|unregister|list> [bunker]");
                        break;
                }
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error : {ex.Message}");
            }
        }

        private async Task HandleRegister(string[] parts)
        {
            if (!CallerHasAdminRole())
            {
                await ReplyAsync("You don't have permission to register for bunkers. Only R4/R5 users can register.");
                return;
            }

            if (parts.Length < 2)
            {
                await ReplyAsync($"Usage: !bunker register <bunker> (Valid bunkers: {string.Join(", ", ValidBunkers)})");
                return;
            }

            var bunker = parts[1].ToUpper();
            if (!ValidBunkers.Contains(bunker))
            {
                await ReplyAsync($"Invalid bunker. Valid options: {string.Join(", ", ValidBunkers)}");
                return;
            }

            var allianceTag = GetAllianceTagFromUser();
            var userId = Context.User.Id;

            // Check if user already has 3 registrations
            var userRegistrations = Store.GetUserRegistrationCount(userId);
            if (userRegistrations >= MaxRegistrationsPerUser)
            {
                await ReplyAsync($"You can only register for a maximum of {MaxRegistrationsPerUser} bunkers. You currently have {userRegistrations}.");
                return;
            }

            // Check if user is already registered for this bunker
            if (Store.IsUserRegisteredForBunker(userId, bunker))
            {
                await ReplyAsync($"You are already registered for {bunker}.");
                return;
            }

            Store.Register(bunker, userId, allianceTag);
            await ReplyAsync($"{Context.User.Username} ({allianceTag}) registered for bunker {bunker}.");
        }

        private async Task HandleUnregister(string[] parts)
        {
            if (!CallerHasAdminRole())
            {
                await ReplyAsync("You don't have permission to unregister from bunkers. Only R4/R5 users can unregister.");
                return;
            }

            if (parts.Length < 2)
            {
                await ReplyAsync($"Usage: !bunker unregister <bunker> (Valid bunkers: {string.Join(", ", ValidBunkers)})");
                return;
            }

            var bunker = parts[1].ToUpper();
            if (!ValidBunkers.Contains(bunker))
            {
                await ReplyAsync($"Invalid bunker. Valid options: {string.Join(", ", ValidBunkers)}");
                return;
            }

            var userId = Context.User.Id;
            var removed = Store.Unregister(bunker, userId);
            
            if (removed)
            {
                await ReplyAsync($"{Context.User.Username} unregistered from bunker {bunker}.");
            }
            else
            {
                await ReplyAsync($"You were not registered for bunker {bunker}.");
            }
        }

        private async Task HandleList()
        {
            var registrations = Store.GetAllRegistrations();
            if (registrations == null || registrations.Count == 0)
            {
                await ReplyAsync("No bunker registrations found.");
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine("**Bunker Registrations:**");
            builder.AppendLine();

            var bunkerGroups = new[] 
            { 
                ("**Front:**", new[] { "F1", "F2", "F3", "F4" }),
                ("**Back:**", new[] { "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9", "B10", "B11", "B12" })
            };

            foreach (var (groupName, bunkers) in bunkerGroups)
            {
                builder.AppendLine(groupName);
                foreach (var bunker in bunkers)
                {
                    var alliances = registrations.ContainsKey(bunker) 
                        ? string.Join(", ", registrations[bunker]) 
                        : "(empty)";
                    builder.AppendLine($"{bunker} - {alliances}");
                }
                builder.AppendLine();
            }

            await ReplyAsync(builder.ToString());
        }

        private void UpdateBunkerDisplay()
        {
            // This could be extended to update a pinned message or embed
            // For now, just a placeholder
        }

        // Simple CSV-backed store
        private class BunkerStore
        {
            private static readonly string DataDir;
            private static readonly string RegistrationsFile;
            private static readonly object Sync = new object();
            private const string Delim = "|";

            // In-memory cache: bunker -> List<(userId, allianceTag)>
            private Dictionary<string, List<(ulong userId, string allianceTag)>> Registrations
                = new Dictionary<string, List<(ulong, string)>>(StringComparer.OrdinalIgnoreCase);

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
                                if (parts.Length < 3)
                                    continue;

                                var bunker = parts[0].Trim();
                                if (!ulong.TryParse(parts[1].Trim(), out var userId))
                                    continue;

                                var allianceTag = parts[2].Trim();

                                if (!Registrations.ContainsKey(bunker))
                                    Registrations[bunker] = new List<(ulong, string)>();

                                Registrations[bunker].Add((userId, allianceTag));
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
                                var bunker = kvp.Key;
                                foreach (var (userId, allianceTag) in kvp.Value)
                                {
                                    writer.WriteLine($"{bunker}{Delim}{userId}{Delim}{allianceTag}");
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

            public void Register(string bunker, ulong userId, string allianceTag)
            {
                lock (Sync)
                {
                    if (!Registrations.ContainsKey(bunker))
                        Registrations[bunker] = new List<(ulong, string)>();

                    // Remove user from all other bunkers first (should not happen normally)
                    foreach (var kv in Registrations.Values)
                        kv.RemoveAll(x => x.userId == userId);

                    // Add to new bunker (if not already there)
                    if (!Registrations[bunker].Any(x => x.userId == userId))
                        Registrations[bunker].Add((userId, allianceTag));

                    Persist();
                }
            }

            public bool Unregister(string bunker, ulong userId)
            {
                lock (Sync)
                {
                    if (!Registrations.ContainsKey(bunker))
                        return false;

                    var removed = Registrations[bunker].RemoveAll(x => x.userId == userId) > 0;
                    if (removed)
                        Persist();

                    return removed;
                }
            }

            public int GetUserRegistrationCount(ulong userId)
            {
                lock (Sync)
                {
                    return Registrations.Values.Sum(list => list.Count(x => x.userId == userId));
                }
            }

            public bool IsUserRegisteredForBunker(ulong userId, string bunker)
            {
                lock (Sync)
                {
                    return Registrations.ContainsKey(bunker) 
                        && Registrations[bunker].Any(x => x.userId == userId);
                }
            }

            public Dictionary<string, List<string>> GetAllRegistrations()
            {
                lock (Sync)
                {
                    var result = new Dictionary<string, List<string>>();
                    foreach (var kvp in Registrations)
                    {
                        var alliances = kvp.Value
                            .Select(x => x.allianceTag)
                            .Distinct()
                            .OrderBy(x => x)
                            .ToList();
                        result[kvp.Key] = alliances;
                    }
                    return result;
                }
            }
        }
    }
}
