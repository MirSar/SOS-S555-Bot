# SOSS555Bot
A Discord bot for State 555 in State of Survival

# Planned features: as discussed in https://discordapp.com/channels/1136616286307229737/1448615839887523881/1475899222149566596
Discord bot, and I think I could make all the points specified in this message possible quite easily - And customise it to whatever needs people want.

**Configuration**

- Place your Discord token in a file outside the project, for example:

	D:\Data\Dropbox\Software\GIT\SOS-S555-Bot\Discord-token.txt

- Create an external configuration file `botsettings.json` next to the token file to configure shared paths. You can copy the example provided in the repository:

	- Copy the example file to the expected location:

		```powershell
		Copy-Item .\botsettings.json.example D:\Data\Dropbox\Software\GIT\SOS-S555-Bot\botsettings.json
		```

	- Edit `botsettings.json` to adjust `Paths.BaseDir`, `Paths.DataDir`, `Paths.LogsDir`, or `DiscordTokenFile` as needed.

- The bot will create/read these items from the configured `DataDir` and `LogsDir` (defaults to the `D:\Data\Dropbox\Software\GIT\SOS-S555-Bot` folder):
	- registrations: `data/registrations.csv`
	- votes: `data/votes.csv`
	- command logs: `logs/commands.log` (JSON lines)

	**Roles and Permissions**

	- **SOS-S555-Access**: Required to use the `!gov` command group (subcommands: `register`, `unregister`, `list`, `raffle`, `vote`). If a user does not have this role, the bot will reject `!gov` requests.
	- **R5**: Required to start a vote. Only R5 users can initiate `!gov vote` commands.
	- **R4 / R5**: Administrative roles that are allowed to register or unregister *other* users. Example usages:
		- `!gov register week21 @SomeUser` — registers the mentioned user (caller must have `R4` or `R5`).
		- `!gov unregister week21 123456789012345678` — unregisters the user by id (caller must have `R4` or `R5`).
	- **Send Messages** (Discord permission): Required to use generic commands like `!echo`; the bot enforces `RequireUserPermission(GuildPermission.SendMessages)` on several command modules.

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

Once started, further text commands against that message are ignored. Only R5 users can initiate a vote.

