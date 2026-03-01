using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SOSS555Bot.Commands.Gov
{
    /// <summary>
    /// Commands for government/raffle/registration flows.
    /// Usage examples:
    ///  - !gov register week21
    ///  - !gov unregister week21
    ///  - !gov list
    ///  - !gov raffle week21
    ///  - !gov vote poll1 optionA
    ///  - !gov vote list poll1
    ///  - !gov vote result poll1
    /// </summary>
    public class Gov : ModuleBase<SocketCommandContext>
    {
        private static readonly GovStore Store = GovStore.Load();

        private bool CallerHasAdminRole()
        {
            if (Context.User is SocketGuildUser gu)
            {
                return gu.Roles.Any(r => string.Equals(r.Name, "R4", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(r.Name, "R5", StringComparison.OrdinalIgnoreCase));
            }
            return false;
        }

        // helper for external code (Bot) to record votes
        public static void CastVoteStatic(string poll, string option, ulong userId)
        {
            Store.CastVote(poll, option, userId);
        }

        /// <summary>
        /// Manages active, in‑progress reaction-based votes.
        /// </summary>
        public static class VoteManager
        {
            // messageId -> (poll, candidateIds)
            private static readonly Dictionary<ulong, (string poll, List<ulong> candidateIds)> _active
                = new Dictionary<ulong, (string, List<ulong>)>();
            // messageId -> (userId -> emoji) to track user votes for removal
            private static readonly Dictionary<ulong, Dictionary<ulong, IEmote>> _userReactions
                = new Dictionary<ulong, Dictionary<ulong, IEmote>>();

            public static void RegisterVote(ulong messageId, string poll, List<ulong> candidateIds)
            {
                _active[messageId] = (poll, candidateIds);
                if (!_userReactions.ContainsKey(messageId))
                    _userReactions[messageId] = new Dictionary<ulong, IEmote>();
            }

            public static async Task<bool> TryHandleReactionAsync(SocketReaction reaction, IUserMessage message)
            {
                if (!_active.TryGetValue(reaction.MessageId, out var data))
                    return false;

                // map emoji to index
                var emoji = reaction.Emote.Name;
                int idx = Array.IndexOf(NumberEmojis, emoji);
                if (idx <= 0 || idx > data.candidateIds.Count)
                    return false;

                var candidateId = data.candidateIds[idx - 1];
                CastVoteStatic(data.poll, candidateId.ToString(), reaction.UserId);

                // Track this reaction and remove old ones from the user
                if (_userReactions.TryGetValue(reaction.MessageId, out var userEmotes))
                {
                    if (userEmotes.TryGetValue(reaction.UserId, out var oldEmote))
                    {
                        // Remove old reaction
                        try
                        {
                            await message.RemoveReactionAsync(oldEmote, reaction.UserId);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[Vote] Failed to remove old reaction: {ex.Message}");
                        }
                    }
                    // Update to new reaction
                    userEmotes[reaction.UserId] = reaction.Emote;
                }

                return true;
            }

            public static readonly string[] NumberEmojis =
            {
                null, "1️⃣", "2️⃣", "3️⃣", "4️⃣", "5️⃣", "6️⃣", "7️⃣", "8️⃣", "9️⃣"
            };
        }

        private ulong? ResolveUserIdFromArg(string arg)
        {
            if (string.IsNullOrWhiteSpace(arg)) return null;
            // strip non-digits to support mentions like <@12345> or <@!12345>
            var digits = new string(arg.Where(char.IsDigit).ToArray());
            if (ulong.TryParse(digits, out var id)) return id;

            // try to resolve by username or username#discriminator within the guild
            try
            {
                if (Context.Guild != null)
                {
                    var user = Context.Guild.Users.FirstOrDefault(u => string.Equals(u.Username, arg, StringComparison.OrdinalIgnoreCase)
                                                                       || string.Equals($"{u.Username}#{u.Discriminator}", arg, StringComparison.OrdinalIgnoreCase));
                    if (user != null) return user.Id;
                }
            }
            catch { }

            return null;
        }

        private const int MinWeek = 1;
        private const int MaxWeek = 53;

        [Summary("Government command group")]
        [Command("gov")]
        [Alias("government")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.SendMessages)]
        public async Task ExecuteAsync([Remainder][Summary("A message or subcommand")] string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    await ReplyAsync("Usage: !gov <register|unregister|list|raffle|vote> [args]");
                    return;
                }

                // role check (keep existing role name if intentional)
                if (Context.User is SocketGuildUser user && !user.Roles.Any(role => role.Name == "SOS-S555-Access"))
                {
                    await Context.Message.AddReactionAsync(new Emoji("❌"));
                    return;
                }

                var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var cmd = parts[0].ToLowerInvariant();

                switch (cmd)
                {
                    case "register":
                        await HandleRegister(parts);
                        break;
                    case "unregister":
                        await HandleUnregister(parts);
                        break;
                    case "list":
                        await HandleList(parts);
                        break;
                    case "raffle":
                        await HandleRaffle(parts);
                        break;
                    case "vote":
                        await HandleVote(parts);
                        break;
                    case "help":
                        await HandleHelp(parts);
                        break;
                    default:
                        await ReplyAsync("Unknown subcommand. Usage: !gov <register|unregister|list|raffle|vote|help> [args]");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error : " + ex.Message);
                await ReplyAsync($"Error : {ex.Message}");
            }
        }

        // Normalize and validate week group. Returns normalized "week{N}" or null if invalid.
        private string NormalizeWeekGroup(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var m = Regex.Match(raw, @"\d+");
            if (!m.Success) return null;
            if (!int.TryParse(m.Value, out var week)) return null;
            if (week < MinWeek || week > MaxWeek) return null;
            return $"week{week}";
        }

        private async Task HandleRegister(string[] parts)
        {
            if (parts.Length < 2)
            {
                await ReplyAsync("Usage: !gov register <week#> (e.g. !gov register week21 or !gov register 21)");
                return;
            }

            var normalized = NormalizeWeekGroup(parts[1]);
            if (normalized == null)
            {
                await ReplyAsync($"Invalid week. Provide a week number between {MinWeek} and {MaxWeek} (e.g. week21 or 21).");
                return;
            }

            // Default to self-registration
            ulong targetUserId = Context.User.Id;
            string targetName = Context.User.Username;

            // If caller provided a target and has admin role, resolve and use it
            if (parts.Length >= 3)
            {
                if (!CallerHasAdminRole())
                {
                    await ReplyAsync("You don't have permission to register other users.");
                    return;
                }

                var resolved = ResolveUserIdFromArg(parts[2]);
                if (resolved == null)
                {
                    await ReplyAsync("Could not resolve the target user. Use a mention or user id.");
                    return;
                }
                targetUserId = resolved.Value;
                targetName = await GetUsernameForGuildAsync(targetUserId);
            }

            Store.Register(Context.Guild.Id, normalized, targetUserId);
            if (targetUserId == Context.User.Id)
                await ReplyAsync($"{Context.User.Username} registered for '{normalized}'.");
            else
                await ReplyAsync($"{targetName} was registered for '{normalized}' by {Context.User.Username}.");
        }

        private async Task HandleUnregister(string[] parts)
        {
            if (parts.Length < 2)
            {
                await ReplyAsync("Usage: !gov unregister <week#> (e.g. !gov unregister week21 or !gov unregister 21)");
                return;
            }

            var normalized = NormalizeWeekGroup(parts[1]);
            if (normalized == null)
            {
                await ReplyAsync($"Invalid week. Provide a week number between {MinWeek} and {MaxWeek}.");
                return;
            }

            // Default to self-unregister
            ulong targetUserId = Context.User.Id;
            string targetName = Context.User.Username;

            // If caller provided a target and has admin role, resolve and use it
            if (parts.Length >= 3)
            {
                if (!CallerHasAdminRole())
                {
                    await ReplyAsync("You don't have permission to unregister other users.");
                    return;
                }

                var resolved = ResolveUserIdFromArg(parts[2]);
                if (resolved == null)
                {
                    await ReplyAsync("Could not resolve the target user. Use a mention or user id.");
                    return;
                }
                targetUserId = resolved.Value;
                targetName = await GetUsernameForGuildAsync(targetUserId);
            }

            var removed = Store.Unregister(Context.Guild.Id, normalized, targetUserId);
            if (removed)
            {
                if (targetUserId == Context.User.Id)
                    await ReplyAsync($"{Context.User.Username} removed from '{normalized}'.");
                else
                    await ReplyAsync($"{targetName} was removed from '{normalized}' by {Context.User.Username}.");
            }
            else
            {
                if (targetUserId == Context.User.Id)
                    await ReplyAsync($"{Context.User.Username} was not registered in '{normalized}'.");
                else
                    await ReplyAsync($"{targetName} was not registered in '{normalized}'.");
            }
        }

        private async Task HandleList(string[] parts)
        {
            // list all groups with usernames
            if (parts.Length == 1)
            {
                var keys = Store.GetAllRegistrationGroups(Context.Guild.Id);
                if (!keys.Any())
                {
                    await ReplyAsync("No registration groups found.");
                    return;
                }

                var lines = new List<string>();
                foreach (var key in keys)
                {
                    var members = Store.GetRegistrations(Context.Guild.Id, key);
                    var names = new List<string>();
                    foreach (var id in members)
                    {
                        names.Add(await GetUsernameForGuildAsync(id));
                    }
                    lines.Add($"{key}: {string.Join(", ", names)}");
                }

                await ReplyAsync("Groups:\n" + string.Join("\n", lines));
            }
            else
            {
                var normalized = NormalizeWeekGroup(parts[1]);
                if (normalized == null)
                {
                    await ReplyAsync($"Invalid week. Provide a week number between {MinWeek} and {MaxWeek}.");
                    return;
                }

                var members = Store.GetRegistrations(Context.Guild.Id, normalized);
                if (members == null || members.Count == 0)
                {
                    await ReplyAsync($"No members registered in '{normalized}'.");
                    return;
                }

                var names = new List<string>();
                foreach (var id in members)
                {
                    names.Add(await GetUsernameForGuildAsync(id));
                }
                await ReplyAsync($"Registered in '{normalized}':\n" + string.Join("\n", names));
            }
        }

        private async Task<string> GetUsernameForGuildAsync(ulong userId)
        {
            try
            {
                // Try to get user from current guild first
                var guild = Context.Guild;
                if (guild != null)
                {
                    var user = guild.GetUser(userId);
                    if (user != null)
                        return $"@{user.Username}";
                }

                // If not in guild, try bot's global user cache
                var globalUser = Context.Client.GetUser(userId);
                if (globalUser != null)
                    return $"@{globalUser.Username}";

                // If still not found, fetch from API
                var fetchedUser = await Context.Client.GetUserAsync(userId);
                if (fetchedUser != null)
                    return $"@{fetchedUser.Username}";
            }
            catch
            {
                // fall through to fallback
            }

            return $"@{userId}"; // fallback includes @ prefix
        }

        private async Task HandleRaffle(string[] parts)
        {
            if (parts.Length < 2)
            {
                await ReplyAsync("Usage: !gov raffle <group> [winners]");
                return;
            }

            var group = parts[1];
            var winnersCount = 1;
            if (parts.Length >= 3 && !int.TryParse(parts[2], out winnersCount))
                winnersCount = 1;

            var members = Store.GetRegistrations(Context.Guild.Id, group);
            if (members == null || members.Count == 0)
            {
                await ReplyAsync($"No members to raffle in '{group}'.");
                return;
            }

            winnersCount = Math.Max(1, Math.Min(winnersCount, members.Count));
            var rnd = new Random();
            var winners = members.OrderBy(_ => rnd.Next()).Take(winnersCount).ToList();
            var names = new List<string>();
            foreach (var id in winners)
            {
                names.Add(await GetUsernameForGuildAsync(id));
            }
            await ReplyAsync($"Raffle winners for '{group}': {string.Join(", ", names)}");
        }

        private async Task HandleVote(string[] parts)
        {
            // only R5 users may start a reaction-based vote
            bool isR5 = false;
            if (Context.User is SocketGuildUser gu)
            {
                isR5 = gu.Roles.Any(r => string.Equals(r.Name, "R5", StringComparison.OrdinalIgnoreCase));
            }
            if (isR5)
            {
                if (parts.Length < 2)
                {
                    await ReplyAsync("Usage: !gov vote <week>");
                    return;
                }

                var normalized = NormalizeWeekGroup(parts[1]);
                if (normalized == null)
                {
                    await ReplyAsync($"Invalid week. Provide a week number between {MinWeek} and {MaxWeek}.");
                    return;
                }

                // load current registrations for the week
                var members = Store.GetRegistrations(Context.Guild.Id, normalized);
                if (members == null || members.Count < 2)
                {
                    await ReplyAsync($"Need at least two registered users for '{normalized}' to start a vote.");
                    return;
                }

                // Build display names list for the message
                var displayNames = new List<string>();
                foreach (var id in members)
                {
                    displayNames.Add(await GetUsernameForGuildAsync(id));
                }

                var builder = new StringBuilder();
                builder.AppendLine($"Vote started for '{normalized}':");
                for (int i = 0; i < displayNames.Count && i < 9; i++)
                {
                    builder.AppendLine($"{i+1}. {displayNames[i]}");
                }

                var msg = await ReplyAsync(builder.ToString());
                int reactCount = Math.Min(members.Count, 9);
                for (int i = 1; i <= reactCount; i++)
                {
                    await msg.AddReactionAsync(new Emoji(Gov.VoteManager.NumberEmojis[i]));
                }

                // Register vote with candidate IDs (not display names)
                Gov.VoteManager.RegisterVote(msg.Id, $"{Context.Guild.Id}:{normalized}", members.Take(9).ToList());
                return;
            }

            // non-R5 users can't initiate votes
            await ReplyAsync("Voting is now handled via reactions; only R5 users can start a vote.");
        }

        private async Task HandleHelp(string[] parts)
        {
            if (parts.Length == 1)
            {
                // General help - overview of all commands
                var builder = new StringBuilder();
                builder.AppendLine("**Government Commands Help**");
                builder.AppendLine("Required Role: `SOS-S555-Access`");
                builder.AppendLine();
                builder.AppendLine("**Available Subcommands:**");
                builder.AppendLine("`register` - Register yourself or another user for a week");
                builder.AppendLine("`unregister` - Unregister yourself or another user from a week");
                builder.AppendLine("`list` - View all registrations for a week");
                builder.AppendLine("`raffle` - Hold a raffle for a week's registrations");
                builder.AppendLine("`vote` - Start or view a reaction-based vote (R5 only)");
                builder.AppendLine();
                builder.AppendLine("Use `!gov help <subcommand>` for detailed information about a specific command.");
                await ReplyAsync(builder.ToString());
                return;
            }

            var helpCmd = parts[1].ToLowerInvariant();
            var helpBuilder = new StringBuilder();

            switch (helpCmd)
            {
                case "register":
                    helpBuilder.AppendLine("**!gov register - Register for a Week**");
                    helpBuilder.AppendLine("**Required Role:** `SOS-S555-Access`");
                    helpBuilder.AppendLine("**Admin Role Required:** `R4` or `R5` (to register other users)");
                    helpBuilder.AppendLine();
                    helpBuilder.AppendLine("**Usage:**");
                    helpBuilder.AppendLine("`!gov register <week#>` - Register yourself");
                    helpBuilder.AppendLine("`!gov register <week#> @user` - Register another user (admin only)");
                    helpBuilder.AppendLine();
                    helpBuilder.AppendLine("**Examples:**");
                    helpBuilder.AppendLine("`!gov register week21` - Register for week 21");
                    helpBuilder.AppendLine("`!gov register 21` - Alternative format (uses week 21)");
                    helpBuilder.AppendLine("`!gov register week5 @Alice` - Register Alice for week 5 (R4/R5 only)");
                    helpBuilder.AppendLine();
                    helpBuilder.AppendLine("**Notes:**");
                    helpBuilder.AppendLine("- Week numbers must be between 1 and 53");
                    break;

                case "unregister":
                    helpBuilder.AppendLine("**!gov unregister - Unregister from a Week**");
                    helpBuilder.AppendLine("**Required Role:** `SOS-S555-Access`");
                    helpBuilder.AppendLine("**Admin Role Required:** `R4` or `R5` (to unregister other users)");
                    helpBuilder.AppendLine();
                    helpBuilder.AppendLine("**Usage:**");
                    helpBuilder.AppendLine("`!gov unregister <week#>` - Unregister yourself");
                    helpBuilder.AppendLine("`!gov unregister <week#> @user` - Unregister another user (admin only)");
                    helpBuilder.AppendLine();
                    helpBuilder.AppendLine("**Examples:**");
                    helpBuilder.AppendLine("`!gov unregister week21` - Remove yourself from week 21");
                    helpBuilder.AppendLine("`!gov unregister 21 @Bob` - Remove Bob from week 21 (R4/R5 only)");
                    break;

                case "list":
                    helpBuilder.AppendLine("**!gov list - View Registrations**");
                    helpBuilder.AppendLine("**Required Role:** `SOS-S555-Access`");
                    helpBuilder.AppendLine();
                    helpBuilder.AppendLine("**Usage:**");
                    helpBuilder.AppendLine("`!gov list <week#>` - Show all registrations for a week");
                    helpBuilder.AppendLine();
                    helpBuilder.AppendLine("**Examples:**");
                    helpBuilder.AppendLine("`!gov list week21` - List all users registered for week 21");
                    helpBuilder.AppendLine("`!gov list 21` - Alternative format");
                    break;

                case "raffle":
                    helpBuilder.AppendLine("**!gov raffle - Hold a Raffle**");
                    helpBuilder.AppendLine("**Required Role:** `R4` or `R5` (admin only)");
                    helpBuilder.AppendLine();
                    helpBuilder.AppendLine("**Usage:**");
                    helpBuilder.AppendLine("`!gov raffle <week#>` - Pick a random winner from registered users");
                    helpBuilder.AppendLine();
                    helpBuilder.AppendLine("**Examples:**");
                    helpBuilder.AppendLine("`!gov raffle week21` - Randomly select a winner from week 21 registrations");
                    helpBuilder.AppendLine();
                    helpBuilder.AppendLine("**Notes:**");
                    helpBuilder.AppendLine("- Must have at least 2 registered users");
                    helpBuilder.AppendLine("- Selects one random user from all registrations");
                    break;

                case "vote":
                    helpBuilder.AppendLine("**!gov vote - Reaction-Based Voting**");
                    helpBuilder.AppendLine("**Required Role:** `R5` (admin only) to start votes");
                    helpBuilder.AppendLine();
                    helpBuilder.AppendLine("**Usage:**");
                    helpBuilder.AppendLine("`!gov vote <week#>` - Start a reaction-based vote for a week's registrations");
                    helpBuilder.AppendLine();
                    helpBuilder.AppendLine("**Examples:**");
                    helpBuilder.AppendLine("`!gov vote week21` - Start vote for week 21 candidates");
                    helpBuilder.AppendLine();
                    helpBuilder.AppendLine("**How Voting Works:**");
                    helpBuilder.AppendLine("1. R5 user starts a vote with `!gov vote <week#>`");
                    helpBuilder.AppendLine("2. Bot posts a message listing registered users (max 9)");
                    helpBuilder.AppendLine("3. React with number emojis (1️⃣, 2️⃣, 3️⃣, etc.) to vote");
                    helpBuilder.AppendLine("4. Each user can vote for only one candidate (reacting again changes vote)");
                    helpBuilder.AppendLine();
                    helpBuilder.AppendLine("**Emojis:** 1️⃣ 2️⃣ 3️⃣ 4️⃣ 5️⃣ 6️⃣ 7️⃣ 8️⃣ 9️⃣");
                    break;

                default:
                    helpBuilder.AppendLine($"Unknown subcommand: `{helpCmd}`");
                    helpBuilder.AppendLine("Available commands: `register`, `unregister`, `list`, `raffle`, `vote`");
                    break;
            }

            await ReplyAsync(helpBuilder.ToString());
        }

        // Simple CSV-backed store
        private class GovStore
        {
            private static readonly string DataDir;
            private static readonly string RegistrationsFile;
            private static readonly string VotesFile;
            private static readonly object Sync = new object();
            private const char Delim = '|'; // use pipe to avoid common commas in names/options

            static GovStore()
            {
                DataDir = AppConfig.DataDir ?? Path.Combine(AppContext.BaseDirectory, "data");
                Directory.CreateDirectory(DataDir);
                RegistrationsFile = Path.Combine(DataDir, "registrations.csv");
                VotesFile = Path.Combine(DataDir, "votes.csv");
            }

            public Dictionary<string, HashSet<ulong>> Registrations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, Dictionary<string, HashSet<ulong>>> Votes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            public static GovStore Load()
            {
                var store = new GovStore();
                try
                {
                    // Load registrations
                    if (File.Exists(RegistrationsFile))
                    {
                        foreach (var line in File.ReadAllLines(RegistrationsFile, Encoding.UTF8))
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            var parts = line.Split(Delim);
                            if (parts.Length < 2) continue;
                            var group = parts[0];
                            if (!ulong.TryParse(parts[1], out var userId)) continue;
                            if (!store.Registrations.TryGetValue(group, out var set))
                            {
                                set = new HashSet<ulong>();
                                store.Registrations[group] = set;
                            }
                            set.Add(userId);
                        }
                    }

                    // Load votes
                    if (File.Exists(VotesFile))
                    {
                        foreach (var line in File.ReadAllLines(VotesFile, Encoding.UTF8))
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            var parts = line.Split(Delim);
                            if (parts.Length < 3) continue;
                            var poll = parts[0];
                            var option = parts[1];
                            if (!ulong.TryParse(parts[2], out var userId)) continue;

                            if (!store.Votes.TryGetValue(poll, out var pollDict))
                            {
                                pollDict = new Dictionary<string, HashSet<ulong>>(StringComparer.OrdinalIgnoreCase);
                                store.Votes[poll] = pollDict;
                            }

                            if (!pollDict.TryGetValue(option, out var voters))
                            {
                                voters = new HashSet<ulong>();
                                pollDict[option] = voters;
                            }

                            // ensure user votes only once per option; duplicates in CSV are idempotent
                            voters.Add(userId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[GovStore] Failed to load store: {ex.Message}");
                }

                return store;
            }

            private void Persist()
            {
                try
                {
                    lock (Sync)
                    {
                        // Write registrations (one line per registration: group|userId)
                        using (var writer = new StreamWriter(RegistrationsFile, false, Encoding.UTF8))
                        {
                            foreach (var kv in Registrations)
                            {
                                var group = kv.Key;
                                foreach (var uid in kv.Value)
                                {
                                    writer.WriteLine($"{group}{Delim}{uid}");
                                }
                            }
                        }

                        // Write votes (one line per vote: poll|option|userId)
                        using (var writer = new StreamWriter(VotesFile, false, Encoding.UTF8))
                        {
                            foreach (var pollKv in Votes)
                            {
                                var poll = pollKv.Key;
                                foreach (var optionKv in pollKv.Value)
                                {
                                    var option = optionKv.Key;
                                    foreach (var uid in optionKv.Value)
                                    {
                                        writer.WriteLine($"{poll}{Delim}{option}{Delim}{uid}");
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[GovStore] Failed to persist store: {ex.Message}");
                }
            }

            public void Register(ulong guildId, string group, ulong userId)
            {
                lock (Sync)
                {
                    var key = $"{guildId}:{group}";
                    if (!Registrations.TryGetValue(key, out var set))
                    {
                        set = new HashSet<ulong>();
                        Registrations[key] = set;
                    }
                    set.Add(userId);
                    Persist();
                }
            }

            public bool Unregister(ulong guildId, string group, ulong userId)
            {
                lock (Sync)
                {
                    var key = $"{guildId}:{group}";
                    if (!Registrations.TryGetValue(key, out var set)) return false;
                    var removed = set.Remove(userId);
                    if (set.Count == 0) Registrations.Remove(key);
                    Persist();
                    return removed;
                }
            }

            public List<ulong> GetRegistrations(ulong guildId, string group)
            {
                lock (Sync)
                {
                    var key = $"{guildId}:{group}";
                    if (!Registrations.TryGetValue(key, out var set)) return new List<ulong>();
                    return set.ToList();
                }
            }

            public IEnumerable<string> GetAllRegistrationGroups(ulong guildId)
            {
                lock (Sync)
                {
                    var prefix = $"{guildId}:";
                    return Registrations.Keys
                        .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .Select(k => k.Substring(prefix.Length))
                        .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }

            public int GetRegistrationCount(string group)
            {
                lock (Sync)
                {
                    if (!Registrations.TryGetValue(group, out var set)) return 0;
                    return set.Count;
                }
            }

            public void CastVote(string poll, string option, ulong userId)
            {
                lock (Sync)
                {
                    if (!Votes.TryGetValue(poll, out var pollDict))
                    {
                        pollDict = new Dictionary<string, HashSet<ulong>>(StringComparer.OrdinalIgnoreCase);
                        Votes[poll] = pollDict;
                    }

                    // remove user from other options in this poll
                    foreach (var kv in pollDict)
                        kv.Value.Remove(userId);

                    if (!pollDict.TryGetValue(option, out var voters))
                    {
                        voters = new HashSet<ulong>();
                        pollDict[option] = voters;
                    }

                    voters.Add(userId);
                    Persist();
                }
            }

            public Dictionary<string, int> GetVoteCounts(string poll)
            {
                lock (Sync)
                {
                    if (!Votes.TryGetValue(poll, out var pollDict)) return new Dictionary<string, int>();
                    return pollDict.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
                }
            }
        }
    }
}