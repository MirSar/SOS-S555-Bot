using Discord;
using Discord.Commands;
using System;
using System.Threading.Tasks;
using System.Linq;

namespace SOSS555Bot.Commands.EasterEgg
{
    /// <summary>
    /// 🥚 THE ULTIMATE EASTER EGG COMMAND 🥚
    /// Trigger with: !egg, !easter, !sossurprise, !secret, !hidden, !findme, !whereswaldo (jk, but maybe?)
    /// Not in help. Not in docs. Not in your dreams... until now.
    /// </summary>
    public class EasterEggCommand : ModuleBase<SocketCommandContext>
    {
        private static readonly string[] Replies = new[]
        {
            // 🎮 Classic Easter Egg Vibes
            "You found a secret egg! 🥚",
            "Shh... the egg is sleeping. 🤫",
            "Little bird says: hello from State 555! 🐦",
            "Surprise! Have a cookie. 🍪",
            "Easter egg unlocked — do a little dance! 💃🎉",

            // 🤪 Silly & Random (Because Why Not?)
            "🥚 The egg has been found. The egg is pleased.",
            "Congratulations! You win... absolutely nothing. 🏆",
            "404: Reward not found. But the egg was! 🥚",
            "You poked the egg. It poked back. 👉🥚👈",
            "The ancient egg prophecy has been fulfilled. 🔮",
            "Egg acquired. Please handle with care. 📦",
            "Behold! The forbidden egg! 🥚✨",
            "You found the egg before the egg found you. 🕵️",
            "The egg has been watching you this whole time. 👀",
            "This egg is rated E for Egg. 🥚",

            // 🎬 Pop Culture / Nerdy (For the True Geeks)
            "It's dangerous to go alone. Take this egg. 🥚⚔️",
            "All your eggs are belong to us. 🥚🥚🥚",
            "Do you want to build an egg-man? ⛄🥚",
            "One egg to rule them all. 💍🥚",
            "Eggscellent discovery, my friend. 🧐",
            "You unlocked: The Egg Zone. Population: you. 🗺️",
            "Achievement unlocked: Egg Whisperer 🏅",
            "Press F to pay respects to the egg. 🥚",
            "This is the way. The egg is the way. 🪖🥚",
            "Egg.exe has entered the chat. 💻",

            // 🎭 Dramatic (Because Eggs Deserve Drama)
            "The stars aligned. The egg was found. History is made. 🌟",
            "Somewhere, an egg is smiling. 🥚😊",
            "A lone wanderer stumbles upon... THE EGG. 🎬",
            "The prophecy spoke of this moment. You are the Chosen One. 🥚👑",
            "Drums in the deep... the egg has awoken. 🥁🥚",

            // 🌈 Wholesome (For When You Need a Hug)
            "You found the egg! Here's a virtual high five! 🙌",
            "The egg says: you're doing great today! 🥚💛",
            "Egg found! Treat yourself to something nice. 🍫",
            "You made the egg happy. The egg loves you. 🥚❤️",
            "Bonus egg unlocked! No idea what it does, but it's yours. 🎁",

            // 🤖 Meta / Self-Aware (Because We’re All a Little Crazy)
            "This message was written by a very bored developer. Hi! 👋",
            "You weren't supposed to find this. Yet here we are. 😅",
            "The developer who hid this egg is very proud of you. 🧑‍💻",
            "If you're reading this, you have too much free time. Same. 🥚",
            "Congratulations on finding the thing that does nothing! 🎊",

            // 🎉 NEW: Rave Mode (Because Why Not Add More Chaos?)
            "🎉 YOU DISCOVERED THE RAVE EGG! 🎉",
            "The egg is now DJing. 🎧",
            "The egg dropped the beat. 🥚🥁",
            "The egg is now a rave legend. 🕺",
            "The egg is now a meme. 📸",
        };

        [Command("egg")] [Alias("easter", "sossurprise", "secret", "hidden", "findme", "whereswaldo")] [RequireContext(ContextType.Guild)]
        public async Task Egg([Remainder] string arg = null)
        {
            try
            {
                var rnd = new Random((int)DateTime.UtcNow.Ticks);

                // 🎛️ Rave Mode: If user says "rave", trigger a mini party!
                if (!string.IsNullOrWhiteSpace(arg) && arg.Equals("rave", StringComparison.OrdinalIgnoreCase))
                {
                    var m = await ReplyAsync("(a tiny party is forming...) 🎛️");
                    for (int i = 0; i < 6; i++)
                    {
                        await Task.Delay(250);
                        await m.ModifyAsync(x => x.Content = new string('•', i + 1) + " 🎶");
                    }
                    await m.ModifyAsync(x => x.Content = "🎉 YOU DISCOVERED THE RAVE EGG! 🎉");
                    return;
                }

                // 🎁 Bonus: If user says "gift", give them a random egg-themed gift!
                if (!string.IsNullOrWhiteSpace(arg) && arg.Equals("gift", StringComparison.OrdinalIgnoreCase))
                {
                    var gifts = new[]
                    {
                        "A virtual egg-shaped pillow 🥚",
                        "A lifetime supply of egg memes 🍳",
                        "A golden egg (worth 0 gold, but shiny!) 🥚✨",
                        "A ticket to EggCon 2026 🎟️",
                        "A hug from the egg 🥚🤗"
                    };
                    var gift = gifts[rnd.Next(gifts.Length)];
                    await ReplyAsync($"🎁 You found a gift! {gift}");
                    return;
                }

                // 🎭 Drama Mode: If user says "drama", trigger a dramatic egg monologue!
                if (!string.IsNullOrWhiteSpace(arg) && arg.Equals("drama", StringComparison.OrdinalIgnoreCase))
                {
                    var drama = new[]
                    {
                        "The egg has been waiting... for you. 🥚",
                        "The egg knows your secrets. 🥚",
                        "The egg is your destiny. 🥚",
                        "The egg is the key to the universe. 🥚",
                        "The egg is... you. 🥚"
                    };
                    var monologue = drama[rnd.Next(drama.Length)];
                    await ReplyAsync($"🎭 {monologue}");
                    return;
                }

                // 🍫 Wholesome Mode: If user says "wholesome", trigger a wholesome egg message!
                if (!string.IsNullOrWhiteSpace(arg) && arg.Equals("wholesome", StringComparison.OrdinalIgnoreCase))
                {
                    var wholesome = new[]
                    {
                        "The egg says: you're doing great today! 🥚💛",
                        "You made the egg happy. The egg loves you. 🥚❤️",
                        "Egg found! Treat yourself to something nice. 🍫",
                        "Here's a virtual high five! 🙌",
                        "Bonus egg unlocked! No idea what it does, but it's yours. 🎁"
                    };
                    var message = wholesome[rnd.Next(wholesome.Length)];
                    await ReplyAsync($"🌈 {message}");
                    return;
                }

                // 🤖 Meta Mode: If user says "meta", trigger a meta egg message!
                if (!string.IsNullOrWhiteSpace(arg) && arg.Equals("meta", StringComparison.OrdinalIgnoreCase))
                {
                    var meta = new[]
                    {
                        "This message was written by a very bored developer. Hi! 👋",
                        "You weren't supposed to find this. Yet here we are. 😅",
                        "The developer who hid this egg is very proud of you. 🧑‍💻",
                        "If you're reading this, you have too much free time. Same. 🥚",
                        "Congratulations on finding the thing that does nothing! 🎊"
                    };
                    var message = meta[rnd.Next(meta.Length)];
                    await ReplyAsync($"🤖 {message}");
                    return;
                }

                // 🎮 Default: Pick a random egg message!
                var text = Replies[rnd.Next(Replies.Length)];
                var msg = await ReplyAsync(text);
                await msg.AddReactionAsync(new Emoji("🥚"));
                await msg.AddReactionAsync(new Emoji("🎉"));
            }
            catch
            {
                // Swallow any exceptions in this playful command to avoid interrupting normal bot flow
            }
        }
    }
}