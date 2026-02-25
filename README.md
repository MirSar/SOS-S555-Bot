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

For signing up / keeping track:
a command to sign yourself up  (EX: !gov register  week 21)
a command to remove yourself (EX: !gov unregister week 21)
parameters for specific weeks
a command to list the registered weeks (EX: !gov list) (@player or Week 21 <-- to view a specific user / week, otherwise show all)
some "admin" commands to override, but only should be used if the player themselves is unable to do it themselves for some reason. I can probably link permissions with the alliance tag + R4/5 or something - or just make it even more simple that you just ask me to do it if it's at all necessary

For selecting if multiple people have signed up (completely randomly):
Select a random gov (EX: !gov raffle week 21)
Or a voting system (EX: !gov vote week 21)
