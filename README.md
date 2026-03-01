# SOSS555Bot
A Discord bot for State 555 in State of Survival

# Planned features: as discussed in https://discordapp.com/channels/1136616286307229737/1448615839887523881/1475899222149566596
Discord bot, and I think I could make all the points specified in this message possible quite easily - And customise it to whatever needs people want.

**Configuration**

The bot uses `Microsoft.Extensions.Configuration` and supports multiple layers of settings:

1. `appsettings.json` in the working directory (checked into this repo as a template)
2. user secrets (during development)
3. an *optional* external JSON file called `botsettings.json` located alongside the Discord token file
4. an in‑memory override if the token file contains a value

By default the token file path is hard‑coded to `D:\Data\Dropbox\Software\GIT\SOS-S555-Bot\Discord-token.txt` but you can override it in any of the configuration sources using the `DiscordTokenFile` key.

The configuration schema now contains these sections:

```json
{
  "Paths": {
    "BaseDir": "D:\\Data\\Dropbox\\Software\\GIT\\SOS-S555-Bot",
    "DataDir": "${Paths:BaseDir}\\data",
    "LogsDir": "${Paths:BaseDir}\\logs",
    "ConfigDir": "${Paths:BaseDir}\\config",        // optional
    "StoresDir": "${Paths:BaseDir}\\stores"       // optional
  },
  "DiscordTokenFile": "D:\\Data\\Dropbox\\Software\\GIT\\SOS-S555-Bot\\Discord-token.txt",
  "Servers": {
    "GuildIds": [123456789012345678, 987654321098765432]
  }
}
```

- **Paths.BaseDir** – root for other paths (default shown above).
- **Paths.DataDir** / **Paths.LogsDir** – where CSV and log files are placed (the bot creates the directories if missing).
- **Paths.ConfigDir** and **Paths.StoresDir** – additional locations you can use for custom configuration or to split storage; currently unused but reserved for future features.
- **Servers:GuildIds** – an *optional* array of guild (server) IDs the bot is expected to serve. When present, bukker/`gov` stores are scoped by guild (see below). This property is read at startup but not enforced at runtime.

To create the external config file you can still use the provided example:

```powershell
Copy-Item .\botsettings.json.example D:\Data\Dropbox\Software\GIT\SOS-S555-Bot\botsettings.json
# then edit the JSON above as needed
```

### File layout
The bot persists state under `DataDir` (defaults to `BaseDir/data`):

| File | Description |
|------|-------------|
| `registrations.csv` | government registrations (guild-scoped: key is `guildId:week`) |
| `votes.csv` | vote records (poll keys prefixed with guild ID) |
| `bunker_registrations.csv` | bunker registrations (columns: `guildId|bunker|userId|allianceTag|ts`) |

When the bot is installed on multiple servers it keeps a single set of CSVs but includes the guild ID in every row. This allows running the same bot binary across servers without collision.

	**Roles and Permissions**

	- **SOS-S555-Access**: Required to use the `!gov` command group (register/unregister/list/raffle/vote/help).
	- **R5**: Required to start a vote or perform bunker registration message updates. Only R5 users can initiate `!gov vote` or issue `!bunker help`.
	- **R4 / R5**: Administrative roles for overriding other users; necessary when registering/unregistering on behalf of somebody else in both gov and bunker flows.
	- **Send Messages**: Bot commands use `[RequireUserPermission(GuildPermission.SendMessages)]` to ensure non‑spammers can use them.
	- **Manage Messages**: The bot may remove invalid reactions (e.g. old vote choices) and requires this permission in order to edit messages it has posted.

	*Bunker-specific*:
	- Bunker registrations are performed by reacting to the message posted by `!bunker`.
	- A user may register up to three different bunkers; the fourth reaction swaps the oldest registration automatically.
	- The alliance tag used for the bunker list is taken from any 3-letter role the user has; if none is found the tag `UNKNOWN` is shown.

	**Username Display**

	All bot outputs consistently display usernames in `@username` format. The bot resolves user IDs through a three-tier system:
	1. Current guild members (fastest)
	2. Bot's global user cache
	3. Discord API (ensures accuracy even for users not currently visible)

	Note: Role checks are currently done by role name. If you prefer using role IDs (recommended for stability), I can update the code to check role IDs instead.


For signing up / keeping track:
a command to sign yourself up  (EX: !gov register  week 21)
a command to remove yourself (EX: !gov unregister week 21)
parameters for specific weeks
a command to list the registered weeks (EX: !gov list) (@player or Week 21 <-- to view a specific user / week, otherwise show all)
some "admin" commands to override, but only should be used if the player themselves is unable to do it themselves for some reason. I can probably link permissions with the alliance tag + R4/5 or something - or just make it even more simple that you just ask me to do it if it's at all necessary

For selecting if multiple people have signed up (completely randomly):
Select a random gov (EX: !gov raffle week 21)

Voting is now reaction‑based and automated. An administrator (role **R5**) starts a vote by providing a week; the bot automatically pulls registered candidates from the registrations file.

```
!gov vote week21
```

The bot will:
1. List all users registered for that week, numbered 1–9 (up to 9 candidates).
2. Add emoji reactions (1️⃣–9️⃣) to the message.
3. Record reactions as votes automatically.

Rules and behaviour:
- Each user may cast exactly one vote; changing the reaction removes the previous one.
- Invalid reactions are removed from the message automatically (you must grant the bot *Manage Messages* for this to work).
- Votes are stored per-guild, so the same poll name on different servers will not interfere.

In addition to the existing gov commands you can now run `!gov help` for a short summary of usage.

---

### Bunker registration

A new `!bunker` command group provides an interface for alliance members to sign up for bunkers using reactions.

```
!bunker        # posts registration message
!bunker help   # shows command syntax and rules
```

When the bot posts the registration message you register by reacting with the number emoji corresponding to the bunker (F1=1️⃣, F2=2️⃣, …, B12=🔟 etc.).

- You may register at most three bunkers; the fourth reaction will automatically remove your oldest registration and replace it with the new one.
- Each registration includes an **alliance tag** derived from any 3‑letter role you possess; the tag appears next to your name in the list.
- Reacting again to a bunker you already have will unregister you from that bunker.
- The registration list is updated in real time whenever someone reacts or removes a reaction.
- Registrations are also stored per-guild, so multiple servers can use the same bot instance.

Use `!bunker help` for a summary of this behaviour anytime.

