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
                    default:
                        await ReplyAsync("Unknown subcommand. Usage: !gov <register|unregister|list|raffle|vote> [args]");
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

            Store.Register(normalized, Context.User.Id);
            await ReplyAsync($"{Context.User.Username} registered for '{normalized}'.");
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

            var removed = Store.Unregister(normalized, Context.User.Id);
            if (removed)
                await ReplyAsync($"{Context.User.Username} removed from '{normalized}'.");
            else
                await ReplyAsync($"{Context.User.Username} was not registered in '{normalized}'.");
        }

        private async Task HandleList(string[] parts)
        {
            // list all groups with usernames
            if (parts.Length == 1)
            {
                var keys = Store.GetAllRegistrationGroups();
                if (!keys.Any())
                {
                    await ReplyAsync("No registration groups found.");
                    return;
                }

                var lines = new List<string>();
                foreach (var key in keys)
                {
                    var members = Store.GetRegistrations(key);
                    var names = members.Select(id => GetUsernameForGuild(id)).ToList();
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

                var members = Store.GetRegistrations(normalized);
                if (members == null || members.Count == 0)
                {
                    await ReplyAsync($"No members registered in '{normalized}'.");
                    return;
                }

                var names = members.Select(id => GetUsernameForGuild(id));
                await ReplyAsync($"Registered in '{normalized}':\n" + string.Join("\n", names));
            }
        }

        private string GetUsernameForGuild(ulong userId)
        {
            try
            {
                var guild = Context.Guild;
                if (guild != null)
                {
                    var user = guild.GetUser(userId);
                    if (user != null)
                        return user.Username; // not mentioning, just username
                }
            }
            catch
            {
                // fall through to fallback
            }

            return userId.ToString();
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

            var members = Store.GetRegistrations(group);
            if (members == null || members.Count == 0)
            {
                await ReplyAsync($"No members to raffle in '{group}'.");
                return;
            }

            winnersCount = Math.Max(1, Math.Min(winnersCount, members.Count));
            var rnd = new Random();
            var winners = members.OrderBy(_ => rnd.Next()).Take(winnersCount).ToList();
            var mentions = winners.Select(id => $"<@{id}>");
            await ReplyAsync($"Raffle winners for '{group}': {string.Join(", ", mentions)}");
        }

        private async Task HandleVote(string[] parts)
        {
            if (parts.Length < 3)
            {
                // support subcommands: list/result
                if (parts.Length >= 2)
                {
                    var sub = parts[1].ToLowerInvariant();
                    if (sub == "list")
                    {
                        await ReplyAsync("Usage: !gov vote list <poll>");
                        return;
                    }
                    if (sub == "result")
                    {
                        await ReplyAsync("Usage: !gov vote result <poll>");
                        return;
                    }
                }

                await ReplyAsync("Usage: !gov vote <poll> <option> | !gov vote list <poll> | !gov vote result <poll>");
                return;
            }

            // list / result special handlers (support both styles: '!gov vote list poll' and '!gov vote result poll')
            var subOrPoll = parts[1].ToLowerInvariant();
            if (subOrPoll == "list" && parts.Length >= 3)
            {
                var pollName = parts[2];
                var counts = Store.GetVoteCounts(pollName);
                if (counts == null || counts.Count == 0)
                {
                    await ReplyAsync($"No votes for poll '{pollName}'.");
                    return;
                }

                var lines = counts.Select(kv => $"{kv.Key}: {kv.Value}");
                await ReplyAsync($"Vote counts for '{pollName}':\n" + string.Join("\n", lines));
                return;
            }

            if (subOrPoll == "result" && parts.Length >= 3)
            {
                var pollName = parts[2];
                var counts = Store.GetVoteCounts(pollName);
                if (counts == null || counts.Count == 0)
                {
                    await ReplyAsync($"No votes for poll '{pollName}'.");
                    return;
                }

                var winner = counts.OrderByDescending(kv => kv.Value).First();
                await ReplyAsync($"Poll '{pollName}' winner: {winner.Key} ({winner.Value} votes)");
                return;
            }

            // standard vote: !gov vote poll option...
            var poll = parts[1];
            var option = string.Join(' ', parts.Skip(2));
            Store.CastVote(poll, option, Context.User.Id);
            var currentCounts = Store.GetVoteCounts(poll);
            var formatted = currentCounts.Select(kv => $"{kv.Key}: {kv.Value}");
            await ReplyAsync($"Vote recorded for '{poll}' -> '{option}'. Current counts:\n" + string.Join("\n", formatted));
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
                DataDir = Path.Combine(AppContext.BaseDirectory, "data");
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

            public void Register(string group, ulong userId)
            {
                lock (Sync)
                {
                    if (!Registrations.TryGetValue(group, out var set))
                    {
                        set = new HashSet<ulong>();
                        Registrations[group] = set;
                    }
                    set.Add(userId);
                    Persist();
                }
            }

            public bool Unregister(string group, ulong userId)
            {
                lock (Sync)
                {
                    if (!Registrations.TryGetValue(group, out var set)) return false;
                    var removed = set.Remove(userId);
                    if (set.Count == 0) Registrations.Remove(group);
                    Persist();
                    return removed;
                }
            }

            public List<ulong> GetRegistrations(string group)
            {
                lock (Sync)
                {
                    if (!Registrations.TryGetValue(group, out var set)) return new List<ulong>();
                    return set.ToList();
                }
            }

            public IEnumerable<string> GetAllRegistrationGroups()
            {
                lock (Sync)
                {
                    return Registrations.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
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