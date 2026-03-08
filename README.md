# SOSS555Bot
A Discord bot for State 555 in State of Survival

## Overview
This project is a Discord bot targeting .NET 9 that implements two primary feature areas: "gov" (government signups, raffles, and voting) and "bunker" (bunker registration via reactions). The bot uses `Discord.Net` (`DiscordSocketClient` and `CommandService`) and `Microsoft.Extensions.Configuration` for configuration.

## Planned Features
As discussed in [this Discord message](https://discordapp.com/channels/1136616286307229737/1448615839887523881/1475899222149566596), the bot is designed to be customizable to meet the needs of the community. 

## Configuration

The bot uses `Microsoft.Extensions.Configuration` and supports multiple layers of settings:

1. `appsettings.json` in the working directory (checked into this repo as a template)
2. User secrets (during development)
3. An *optional* external JSON file called `botsettings.json` located alongside the Discord token file
4. An in‑memory override if the token file contains a value

By default, the token file path is hard‑coded to `D:\Data\Dropbox\Software\GIT\SOS-S555-Bot\Discord-token.txt`, but you can override it in any of the configuration sources using the `DiscordTokenFile` key.

### Configuration & Startup
- Configuration hierarchy (highest precedence last): `appsettings.json`, user secrets, optional external `botsettings.json`, and an in-memory override if the token file contains a value.
- Configuration keys of interest:
  - `Paths.BaseDir`, `Paths.DataDir`, `Paths.LogsDir`, `Paths.ConfigDir`, `Paths.StoresDir` (see sample in repo).
  - `DiscordTokenFile` (path to token file can be overridden).
  - `DiscordToken` (actual token value read into configuration or via token file override).
- The bot sets a simple presence on connect (`SOSS555Bot`) and waits for the `Ready` event (30s timeout) before enabling handlers.

### Logging
- All command invocations are logged to console and appended (JSON lines) to a file `commands.log` under `Paths.LogsDir` (or `./logs` by default).
- Internal Discord.Net log messages are printed to the console stderr for troubleshooting.

### File Layout
The bot persists state under `DataDir` (defaults to `BaseDir/data`):

| File | Description |
|------|-------------|
| `registrations.csv` | Government registrations (guild-scoped: key is `guildId:week`) |
| `votes.csv` | Vote records (poll keys prefixed with guild ID) |
| `bunker_registrations.csv` | Bunker registrations (columns: `guildId|week|bunker|userId|allianceTag|ts`) |

When the bot is installed on multiple servers, it keeps a single set of CSVs but includes the guild ID in every row. This allows running the same bot binary across servers without collision.

## Commands & Interaction
- Command prefix is `!`. Message handling is performed by `Bot.HandleCommandAsync`, which strips the prefix and executes commands via `CommandService`.
- The bot exposes at least the following command groups:
  - `!gov` — Handles government registrations, raffles, and reaction-based voting.
  - `!bunker` — Posts bunker registration boards and provides helper commands.

### Bunker Command Highlights (Implemented)
- Command group: `!bunker` (aliases: `!bunk`). Requires guild context and user permission to send messages.
- Subcommands implemented:
  - `!bunker post [week]` — Posts a registration board for the specified week (or current ISO-like week if omitted). Only users with role `R4` or `R5` may post boards.
  - `!bunker weeks` — Lists week keys that currently have registrations for the guild.
  - `!bunker help` — Displays usage and rules.
  - `!bunker clear <week>` — A hidden clear command that only a single configured user may run (user id `113907147145740291`). It clears all registrations for the given week and refreshes posted boards.
- When a board is posted, the bot adds a fixed set of reaction emojis (see `BunkerManager.BunkerEmojis`) that map to bunker names `F1`–`F4` (front) and `B1`–`B12` (back).
- Reaction handling behaviour:
  - Users react to register/unregister for a bunker. Reacting again removes the user's registration for that bunker (toggle behaviour).
  - The bot enforces per-alliance limits: maximum 1 Front bunker and 2 Back bunkers per alliance per week. Alliance tag is taken from any 3-letter role on the reacting user (uppercased), otherwise `UNKNOWN`.
  - If an alliance already is at the limit for the side (front/back) and a new registration is attempted, the oldest alliance registration on that side is removed automatically and replaced with the new registration (the bot attempts to remove the corresponding reaction from the user who had the oldest registration).
  - Message content is updated in real time when registrations change (the board message is edited to show current alliance lists per bunker).
  - Registration state is persisted to `bunker_registrations.csv` under the configured data directory.

### Voting (Gov) Behaviour (Summary)
- Voting is reaction-based. An administrator (role `R5`) starts a vote with `!gov vote <week>` (or similar as implemented in `Commands/Gov`).
- The bot posts a message listing up to 9 candidates and adds numeric emoji reactions (`1️⃣`–`9️⃣`).
- Reaction additions are recorded as votes and restricted to one vote per user; changing reactions updates the stored vote. Invalid reactions are removed (requires the bot to have `Manage Messages`).
- Votes are persisted in CSV under the data directory (`votes.csv`) and include guild-scoping in keys so multiple servers can run safely with a shared data directory.

## Roles and Permissions
- Role checks are performed by role name (`SOS-S555-Access`, `R4`, `R5`) and via `RequireUserPermission(GuildPermission.SendMessages)` on command modules where appropriate.
- `R5` role is used for sensitive operations like starting votes and posting/refreshing bunker boards.
- Role checks are case-insensitive string comparisons; to improve stability, you can change these to role-id based checks.

## Username Display
All bot outputs consistently display usernames in `@username` format. The bot resolves user IDs through a three-tier system:
1. Current guild members (fastest)
2. Bot's global user cache
3. Discord API (ensures accuracy even for users not currently visible)

Note: Role checks are currently done by role name. If you prefer using role IDs (recommended for stability), I can update the code to check role IDs instead.

## Notes for Operators
- The bot requires the Message Content Gateway Intent and (for certain features) the Manage Messages permission so it can edit messages and remove invalid reactions.
- The bot logs command invocations to console and to `commands.log` in JSON lines for auditing.

## For Signing Up / Keeping Track
- A command to sign yourself up (EX: `!gov register week 21`)
- A command to remove yourself (EX: `!gov unregister week 21`)
- Parameters for specific weeks
- A command to list the registered weeks (EX: `!gov list`) (@player or Week 21 <-- to view a specific user/week, otherwise show all)
- Some "admin" commands to override, but only should be used if the player themselves is unable to do it themselves for some reason.

For selecting if multiple people have signed up (completely randomly):
- Select a random gov (EX: `!gov raffle week 21`)

In addition to the existing gov commands, you can now run `!gov help` for a short summary of usage.

### Bunker Registration
A new `!bunker` command group provides an interface for alliance members to sign up for bunkers using reactions.

```
!bunker        # posts registration message
!bunker help   # shows command syntax and rules
```

When the bot posts the registration message, you register by reacting with the number emoji corresponding to the bunker (F1=1️⃣, F2=2️⃣, …, B12=🔟 etc.).

- You may register at most three bunkers; the fourth reaction will automatically remove your oldest registration and replace it with the new one.
- Each registration includes an **alliance tag** derived from any 3‑letter role you possess; the tag appears next to your name in the list.
- Reacting again to a bunker you already have will unregister you from that bunker.
- The registration list is updated in real time whenever someone reacts or removes a reaction.
- Registrations are also stored per-guild, so multiple servers can use the same bot instance.

Use `!bunker help` for a summary of this behaviour anytime.


