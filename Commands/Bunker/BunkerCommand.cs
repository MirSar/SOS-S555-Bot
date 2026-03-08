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
using System.Globalization;

#nullable enable

namespace SOSS555Bot.Commands.Bunker
{
    /// <summary>
    /// Commands for bunker registration and tracking.
    /// Usage examples:
    ///  - !bunker post           -> post current week's registration board (admin)
    ///  - !bunker post 2026-W09  -> post specific week (admin)
    ///  - !bunker weeks          -> list weeks that have registrations
    ///  - React on a posted board to register/unregister for that week's bunkers
    /// </summary>
    public partial class BunkerCommand : ModuleBase<SocketCommandContext>
    {
        private static readonly BunkerStore Store = BunkerStore.Load();

        private static readonly string[] ValidBunkers =
        {
            "F1", "F2", "F3", "F4", "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9", "B10", "B11", "B12"
        };

        // legacy per-user constant kept for compatibility; primary enforcement is per-alliance below
        private const int MaxRegistrationsPerUser = 3;

        // New per-alliance limits
        private const int AllianceFrontLimit = 1;
        private const int AllianceBackLimit = 2;

        // The single Discord user allowed to run the hidden clear command
        private static readonly ulong ClearCommandUserId = 113907147145740291UL;

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

        private static string WeekKeyFromDate(DateTimeOffset dt)
        {
            // ISO-like week: first-four-day-week with Monday as first day
            var cal = CultureInfo.InvariantCulture.Calendar;
            var week = cal.GetWeekOfYear(dt.UtcDateTime, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            var year = dt.UtcDateTime.Year;

            // Handle week 52/53 edge-cases around year boundary using ISO rule:
            // If week number is 1 but month is December, increment year.
            if (week == 1 && dt.Month == 12)
                year += 1;
            // If week belongs to previous year (e.g., Jan with week 52/53)
            if (week >= 52 && dt.Month == 1)
                year -= 1;

            return $"{year}-W{week:D2}";
        }

        private static bool TryParseWeekKey(string input, out string weekKey)
        {
            weekKey = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            input = input.Trim();

            // Accept explicit week format: 2026-W09
            if (System.Text.RegularExpressions.Regex.IsMatch(input, @"^\d{4}-W\d{1,2}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                // Normalize to two-digit week
                var parts = input.Split("-W", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && int.TryParse(parts[1], out var w))
                {
                    weekKey = $"{parts[0]}-W{w:D2}";
                    return true;
                }
            }

            // Accept ISO date / any date parseable
            if (DateTimeOffset.TryParse(input, out var dt))
            {
                weekKey = WeekKeyFromDate(dt);
                return true;
            }

            return false;
        }

        [Summary("Bunker command group")]
        [Command("bunker")]
        [Alias("bunk")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.SendMessages)]
        public async Task ExecuteAsync([Remainder][Summary("A message or subcommand")] string? message = null)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    var trimmedMessage = message.Trim();
                    var tokens = trimmedMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var verb = tokens.Length > 0 ? tokens[0].ToLowerInvariant() : string.Empty;

                    // More robust and prioritized handling for the hidden clear command:
                    if (verb == "clear")
                    {
                        // Accept both "!bunker clear 2026-W10" and "!bunker clear2026-W10" (trim accordingly)
                        var after = trimmedMessage.Length > 5 ? trimmedMessage.Substring(5).Trim() : string.Empty;

                        if (Context.User.Id != ClearCommandUserId)
                        {
                            await ReplyAsync("You are not authorized to run this command.");
                            return;
                        }

                        if (string.IsNullOrWhiteSpace(after) || !TryParseWeekKey(after, out var weekKey))
                        {
                            await ReplyAsync("Usage: `!bunker clear 2026-W09`");
                            return;
                        }

                        var removedCount = Store.ClearWeek(Context.Guild.Id, weekKey);
                        await ReplyAsync($"Cleared {removedCount} registration entries for week {weekKey}.");

                        // Update any posted registration messages for this week so the board reflects cleared state
                        await BunkerManager.RefreshMessagesForWeek(Context.Guild.Id, weekKey);
                        return;
                    }

                    if (verb == "help")
                    {
                        await ShowHelp();
                        return;
                    }

                    if (verb == "weeks" || verb == "listweeks")
                    {
                        await ShowWeeks();
                        return;
                    }

                    if (verb == "post")
                    {
                        if (!CallerHasAdminRole())
                        {
                            await ReplyAsync("Only users with R4/R5 roles can post bunker boards.");
                            return;
                        }

                        string weekKey;
                        if (tokens.Length >= 2 && TryParseWeekKey(tokens[1], out var parsed))
                            weekKey = parsed;
                        else
                            weekKey = WeekKeyFromDate(DateTimeOffset.UtcNow);

                        await PostBunkerRegistrationMessage(weekKey);
                        return;
                    }
                }

                // Default: post current week's board (admin only)
                if (!CallerHasAdminRole())
                {
                    await ReplyAsync("Only users with R4/R5 roles can post bunker boards. Use `!bunker weeks` to list available weeks.");
                    return;
                }

                var currentWeek = WeekKeyFromDate(DateTimeOffset.UtcNow);
                await PostBunkerRegistrationMessage(currentWeek);
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
            builder.AppendLine("**Required Role to post:** `R4` or `R5` (admin only)");
            builder.AppendLine();
            builder.AppendLine("**Commands:**");
            builder.AppendLine("`!bunker post` - Post this week's registration board (admin)");
            builder.AppendLine("`!bunker post 2026-W09` - Post a specific week (admin)");
            builder.AppendLine("`!bunker weeks` - List weeks with registrations");
            builder.AppendLine("`!bunker help` - Show this help");
            builder.AppendLine();
            builder.AppendLine("React with emoji on a posted board to register/unregister for that week's bunkers.");
            builder.AppendLine("Each week is independent; you may register separately for each week.");
            builder.AppendLine();
            builder.AppendLine("Registration limits per alliance per week: maximum **1 Front** and **2 Back** bunkers.");
            await ReplyAsync(builder.ToString());
        }

        private async Task ShowWeeks()
        {
            var weeks = Store.GetWeeks(Context.Guild.Id)
                .OrderByDescending(x => x)
                .ToList();

            if (!weeks.Any())
            {
                await ReplyAsync("No weeks found.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("**Registered weeks:**");
            foreach (var wk in weeks)
            {
                sb.AppendLine(wk);
            }

            await ReplyAsync(sb.ToString());
        }

        private async Task PostBunkerRegistrationMessage(string weekKey)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"**Bunker Registration — Week {weekKey}** - React to register/unregister for bunkers");
            builder.AppendLine();
            builder.AppendLine("Registration limits per alliance per week: maximum **1 Front** and **2 Back** bunkers.");
            builder.AppendLine();

            var bunkerEmojis = BunkerManager.BunkerEmojis;
            var bunkerList = BunkerManager.BunkerList;

            var registrations = Store.GetAllRegistrations(Context.Guild.Id, weekKey);

            builder.AppendLine("**Front:**");
            for (int i = 0; i < 4; i++)
            {
                var alliances = registrations.ContainsKey(bunkerList[i])
                    ? string.Join(", ", registrations[bunkerList[i]])
                    : "(empty)";
                builder.AppendLine($"{bunkerEmojis[i]} {bunkerList[i]} - {alliances}");
            }
            builder.AppendLine();

            builder.AppendLine("**Back:**");
            for (int i = 4; i < bunkerList.Length; i++)
            {
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

            BunkerManager.RegisterMessage(msg.Id, msg, weekKey);
        }
    }

    public partial class BunkerCommand
    {
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

            // messageId -> (message, weekKey)
            private static readonly Dictionary<ulong, (IUserMessage message, string week)> _registrationMessages
                = new Dictionary<ulong, (IUserMessage, string)>();

            public static void RegisterMessage(ulong messageId, IUserMessage message, string weekKey)
            {
                _registrationMessages[messageId] = (message, weekKey);
            }

            public static Task<(bool Success, string? BunkerRemoved)> TryHandleReactionAsync(SocketReaction reaction, IUserMessage message, string allianceTag)
            {
                if (!_registrationMessages.TryGetValue(reaction.MessageId, out var entry))
                    return Task.FromResult<(bool, string?)>((false, null));

                var weekKey = entry.week;

                // map to guild id from the message's channel
                var guild = (message.Channel as SocketGuildChannel)?.Guild;
                if (guild == null) return Task.FromResult<(bool, string?)>((false, null));
                var guildId = guild.Id;

                // Map emoji to bunker index
                var emoji = reaction.Emote.Name;
                int bunkerIndex = Array.IndexOf(BunkerEmojis, emoji);
                if (bunkerIndex < 0)
                    return Task.FromResult<(bool, string?)>((false, null));

                var bunker = BunkerList[bunkerIndex];
                var userId = reaction.UserId;

                // Toggle registration: if already registered, unregister; otherwise register
                if (Store.IsUserRegisteredForBunker(guildId, userId, weekKey, bunker))
                {
                    Store.Unregister(guildId, weekKey, bunker, userId);
                    return Task.FromResult<(bool, string?)>((true, null)); // Successfully unregistered
                }
                else
                {
                    // Determine side (front/back) and applicable alliance limit
                    bool isFront = bunkerIndex < 4;
                    int allianceLimit = isFront ? AllianceFrontLimit : AllianceBackLimit;

                    // If alliance at limit for this side, remove oldest alliance registration for that side first
                    if (Store.GetAllianceRegistrationCountBySide(guildId, allianceTag, weekKey, isFront) >= allianceLimit)
                    {
                        var removed = Store.RemoveOldestRegistrationForAllianceSide(guildId, allianceTag, weekKey, isFront);
                        // After removal, proceed to register the new bunker
                        Store.Register(guildId, weekKey, bunker, userId, allianceTag);
                        return Task.FromResult<(bool, string?)>((true, removed));
                    }

                    Store.Register(guildId, weekKey, bunker, userId, allianceTag);
                    return Task.FromResult<(bool, string?)>((true, null)); // Successfully registered
                }
            }

            public static Task<bool> TryHandleReactionRemovedAsync(SocketReaction reaction, IUserMessage message)
            {
                if (!_registrationMessages.TryGetValue(reaction.MessageId, out var entry))
                    return Task.FromResult(false);

                var weekKey = entry.week;

                // map to guild id from the message's channel
                var guild = (message.Channel as SocketGuildChannel)?.Guild;
                if (guild == null) return Task.FromResult(false);
                var guildId = guild.Id;

                // Map emoji to bunker index
                var emoji = reaction.Emote.Name;
                int bunkerIndex = Array.IndexOf(BunkerEmojis, emoji);
                if (bunkerIndex < 0)
                    return Task.FromResult(false);

                var bunker = BunkerList[bunkerIndex];
                var userId = reaction.UserId;

                // Unregister the user for this bunker (idempotent)
                var removed = Store.Unregister(guildId, weekKey, bunker, userId);
                return Task.FromResult(removed);
            }

            public static async Task UpdateMessageDisplayAsync(ulong messageId)
            {
                if (!_registrationMessages.TryGetValue(messageId, out var entry))
                    return;
                var message = entry.message;
                var week = entry.week;
                var guild = (message.Channel as SocketGuildChannel)?.Guild;
                if (guild == null) return;
                var content = BuildRegistrationContent(guild.Id, week);
                try
                {
                    await message.ModifyAsync(m => m.Content = content);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Bunker] Failed to update message: {ex.Message}");
                }
            }

            // Refresh all posted registration messages for the specified week in the specified guild
            public static async Task RefreshMessagesForWeek(ulong guildId, string weekKey)
            {
                var toRefresh = _registrationMessages
                    .Where(kvp => kvp.Value.week == weekKey
                                  && ((kvp.Value.message.Channel as SocketGuildChannel)?.Guild?.Id == guildId))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var msgId in toRefresh)
                {
                    await UpdateMessageDisplayAsync(msgId);
                }
            }

            private static string BuildRegistrationContent(ulong guildId, string weekKey)
            {
                var builder = new StringBuilder();
                builder.AppendLine($"**Bunker Registration — Week {weekKey}** - React to register/unregister for bunkers");
                builder.AppendLine();
                builder.AppendLine("Registration limits per alliance per week: maximum **1 Front** and **2 Back** bunkers.");
                builder.AppendLine();

                var registrations = Store.GetAllRegistrations(guildId, weekKey);

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

        // Simple CSV-backed store, now keyed per-week
        private class BunkerStore
        {
            private static readonly string DataDir;
            private static readonly string RegistrationsFile;
            private static readonly object Sync = new object();
            private const string Delim = "|";

            // In-memory cache: key = "{guildId}:{week}:{bunker}" -> List<(userId, allianceTag, timestampMs)>
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
                            string? line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                if (string.IsNullOrWhiteSpace(line))
                                    continue;

                                var parts = line.Split(Delim);
                                // expected format: guildId|week|bunker|userId|allianceTag|ts
                                if (parts.Length < 5)
                                    continue;

                                if (!ulong.TryParse(parts[0].Trim(), out var guildId))
                                    continue;
                                var week = parts[1].Trim();
                                var bunker = parts[2].Trim();
                                if (!ulong.TryParse(parts[3].Trim(), out var userId))
                                    continue;

                                var allianceTag = parts[4].Trim();
                                long ts = 0;
                                if (parts.Length >= 6 && long.TryParse(parts[5].Trim(), out var parsedTs))
                                    ts = parsedTs;

                                var key = $"{guildId}:{week}:{bunker}";
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
                                var key = kvp.Key; // format: guildId:week:bunker
                                var parts = key.Split(':', 3);
                                if (parts.Length < 3) continue;
                                var guildStr = parts[0];
                                var week = parts[1];
                                var bunker = parts[2];
                                foreach (var (userId, allianceTag, ts) in kvp.Value)
                                {
                                    writer.WriteLine($"{guildStr}{Delim}{week}{Delim}{bunker}{Delim}{userId}{Delim}{allianceTag}{Delim}{ts}");
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

            public void Register(ulong guildId, string week, string bunker, ulong userId, string allianceTag)
            {
                lock (Sync)
                {
                    var key = $"{guildId}:{week}:{bunker}";
                    if (!Registrations.ContainsKey(key))
                        Registrations[key] = new List<(ulong, string, long)>();

                    // Only add if not already registered for this bunker in this week by the same user.
                    if (Registrations[key].Any(x => x.userId == userId))
                        return;

                    Registrations[key].Add((userId, allianceTag, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                    Persist();
                }
            }

            /// <summary>
            /// Clears all registrations for the specified guild and week.
            /// Returns the number of removed registration entries (sum of user entries removed).
            /// </summary>
            public int ClearWeek(ulong guildId, string week)
            {
                lock (Sync)
                {
                    var prefix = $"{guildId}:{week}:";
                    var keys = Registrations.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (!keys.Any())
                        return 0;

                    int removedCount = 0;
                    foreach (var k in keys)
                    {
                        removedCount += Registrations[k].Count;
                        Registrations.Remove(k);
                    }

                    Persist();
                    return removedCount;
                }
            }

            /// <summary>
            /// Removes the oldest registration (by timestamp) for the given user within the specified week.
            /// Returns the bunker name that was removed, or null if none removed.
            /// </summary>
            public string? RemoveOldestRegistrationForUser(ulong guildId, ulong userId, string week)
            {
                lock (Sync)
                {
                    string? foundKey = null;
                    string? foundBunker = null;
                    long oldestTs = long.MaxValue;
                    var prefix = $"{guildId}:{week}:";
                    foreach (var kvp in Registrations.Where(k => k.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (var entry in kvp.Value.Where(x => x.userId == userId))
                        {
                            if (entry.ts < oldestTs)
                            {
                                oldestTs = entry.ts;
                                foundKey = kvp.Key;
                                var idx = kvp.Key.LastIndexOf(':');
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

            /// <summary>
            /// Removes the oldest registration (by timestamp) for the given alliance within the specified week and side (front/back).
            /// Returns the bunker name that was removed, or null if none removed.
            /// This removes all user entries for the removed bunker for that alliance.
            /// </summary>
            public string? RemoveOldestRegistrationForAllianceSide(ulong guildId, string allianceTag, string week, bool isFront)
            {
                lock (Sync)
                {
                    string? foundKey = null;
                    string? foundBunker = null;
                    long oldestTs = long.MaxValue;
                    var prefix = $"{guildId}:{week}:";

                    foreach (var kvp in Registrations.Where(k => k.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    {
                        var bunker = kvp.Key.Substring(prefix.Length);
                        var index = Array.IndexOf(BunkerManager.BunkerList, bunker);
                        if (index < 0) continue;
                        bool entryIsFront = index < 4;
                        if (entryIsFront != isFront) continue;

                        foreach (var entry in kvp.Value.Where(x => string.Equals(x.allianceTag, allianceTag, StringComparison.OrdinalIgnoreCase)))
                        {
                            if (entry.ts < oldestTs)
                            {
                                oldestTs = entry.ts;
                                foundKey = kvp.Key;
                                foundBunker = bunker;
                            }
                        }
                    }

                    if (foundKey != null)
                    {
                        // remove all entries for this alliance in that bunker
                        Registrations[foundKey].RemoveAll(x => string.Equals(x.allianceTag, allianceTag, StringComparison.OrdinalIgnoreCase));
                        Persist();
                    }

                    return foundBunker;
                }
            }

            public bool Unregister(ulong guildId, string week, string bunker, ulong userId)
            {
                lock (Sync)
                {
                    var key = $"{guildId}:{week}:{bunker}";
                    if (!Registrations.ContainsKey(key))
                        return false;

                    var removed = Registrations[key].RemoveAll(x => x.userId == userId) > 0;
                    if (removed)
                        Persist();

                    return removed;
                }
            }

            public int GetUserRegistrationCount(ulong guildId, ulong userId, string week)
            {
                lock (Sync)
                {
                    var prefix = $"{guildId}:{week}:";
                    return Registrations.Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .Sum(kv => kv.Value.Count(x => x.userId == userId));
                }
            }

            /// <summary>
            /// Returns the number of distinct bunkers on the given side (front/back) that the alliance has registered for in the specified week.
            /// Distinct bunkers only (multiple users from the same alliance on the same bunker count as one).
            /// </summary>
            public int GetAllianceRegistrationCountBySide(ulong guildId, string allianceTag, string week, bool isFront)
            {
                lock (Sync)
                {
                    var prefix = $"{guildId}:{week}:";
                    var count = Registrations
                        .Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .Select(kv => new { Key = kv.Key, Values = kv.Value })
                        .Select(x => new { Bunker = x.Key.Substring(prefix.Length), HasAlliance = x.Values.Any(v => string.Equals(v.allianceTag, allianceTag, StringComparison.OrdinalIgnoreCase)) })
                        .Where(x => x.HasAlliance)
                        .Select(x => x.Bunker)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(bunker =>
                        {
                            var index = Array.IndexOf(BunkerManager.BunkerList, bunker);
                            if (index < 0) return false;
                            bool bunkerIsFront = index < 4;
                            return bunkerIsFront == isFront;
                        });

                    return count;
                }
            }

            public bool IsUserRegisteredForBunker(ulong guildId, ulong userId, string week, string bunker)
            {
                lock (Sync)
                {
                    var key = $"{guildId}:{week}:{bunker}";
                    return Registrations.ContainsKey(key)
                        && Registrations[key].Any(x => x.userId == userId);
                }
            }

            /// <summary>
            /// Returns registrations for the specified guild and week.
            /// If no entries exist for that week an empty dictionary is returned.
            /// </summary>
            public Dictionary<string, List<string>> GetAllRegistrations(ulong guildId, string week)
            {
                lock (Sync)
                {
                    var result = new Dictionary<string, List<string>>();
                    var prefix = $"{guildId}:{week}:";
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

            /// <summary>
            /// Returns the distinct week keys for which this guild has registrations.
            /// </summary>
            public List<string> GetWeeks(ulong guildId)
            {
                lock (Sync)
                {
                    var prefix = $"{guildId}:";
                    var weeks = Registrations.Keys
                        .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .Select(k =>
                        {
                            var parts = k.Substring(prefix.Length).Split(':', 2);
                            return parts.Length >= 1 ? parts[0] : null;
                        })
                        .Where(w => !string.IsNullOrEmpty(w))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(w => w)
                        .ToList()!;
                    return weeks;
                }
            }
        }
    }
}
