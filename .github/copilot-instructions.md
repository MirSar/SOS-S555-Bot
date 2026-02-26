# AI Agent Instructions for SOS-S555-Bot

This project is a Discord bot written in C# using Discord.Net.  Its structure is simple:*

- **Program.cs** boots the application, reads configuration (appsettings + user secrets) and supports an external `botsettings.json` placed alongside the Discord token file.  `AppConfig.Initialize` centralises paths (`BaseDir`, `DataDir`, `LogsDir`, `DiscordTokenFile`).
- **Bot.cs** encapsulates the `IBot` implementation: client setup, logging handlers, command service hookup.
- **Commands/** contains modular command groups.  Each command type lives in its own sub‑namespace and folder (e.g. `Gov`, `Echo`, etc.) and inherits from `ModuleBase<SocketCommandContext>`.
- **Commands/Gov/gov.cs** is the most complex command module.  It uses a private `GovStore` inner class that persists registrations and votes to CSV files under `AppConfig.DataDir`.  Access control is enforced via role names (`SOS-S555-Access`, `R4`, `R5`) and `RequireUserPermission` attributes.
- **AppConfig.cs** is a lightweight helper that reads path configuration and ensures directories exist.  Many other files refer to `AppConfig.DataDir` or `AppConfig.LogsDir` for storage.

## Key Patterns & Conventions

- **Command prefixes**: messages starting with `'!'` are treated as commands.  `Bot.HandleCommandAsync` strips the prefix and logs every invocation to console and to a JSON‑line log in `LogsDir`.
- **Permissions**: use Discord.Net attributes for context and permissions; additional role logic in code (e.g. `CallerHasAdminRole()` in `gov.cs`).  Admin roles are currently by name (`R4`/`R5`).
- **Data storage**: CSV files in a `data` directory.  `GovStore` implements thread‑safe persistence with a simple `lock (Sync)`.
- **Configuration hierarchy**: `appsettings.json` ➜ user secrets ➜ optional external JSON next to token file ➜ in‑memory override from token file contents.  The token file location itself is configurable via `DiscordTokenFile`.
- **Logging**: console output and file append via `AppendLogAsync` (thread‑safe).  No external logging library is used.

## Build & Run

- Build with `dotnet build SoS-S555-Bot.csproj` or via the provided VS Code task `build`.
- Run with `dotnet run --project SoS-S555-Bot.csproj`, or use the `watch run` task for live development.
- The project targets `.NET 9.0` and uses implicit usings.
- No tests are included; focus is on manual runtime verification.

## Configuration & Runtime Data

- Discord token is read from an external file (default `D:\Data\Dropbox\Software\GIT\SOS-S555-Bot\Discord-token.txt`) or via configuration key `DiscordTokenFile`.
- A sample configuration file `botsettings.json.example` shows how to override base, data, and logs directories.
- Logs are written to `LogsDir/commands.log` in JSON lines.
- Persistent data (registrations, votes) lives in CSV files under `DataDir`.

## Developer Workflows

- All code changes live on feature branches (e.g. `feature/gov-fix`).  Commits are pushed and a PR is opened via GitHub.
- There is no automated CI described; building locally is the primary workflow.
- The `.vscode` folder contains tasks for building/publishing.

## Integration Points

- The bot communicates with Discord via the Discord.Net client (see `DiscordSocketClient` configuration).  Gateway intents include `MessageContent`.
- External commands (Translate, UrbanDictionary) existed as `.Nah` files but are not compiled; they may be resurrected or removed.
- No database or third‑party services are currently integrated except Discord.

## Tips for AI Agents

- Focus modifications on `Commands/*`: each new feature should add a new Module or extend an existing one.
- Use `AppConfig` for any path-dependent behavior; avoid hardcoding directories.
- Follow the permission pattern used in `gov.cs` when adding role‑based access.
- When modifying `Bot.cs`, keep the logging instrumentation (console + file) consistent.
- Keep token handling in `Program.cs`; external JSON config is read very early.
- Avoid editing generated/build artefacts (bin/, obj/); they are gitignored.

Once you've made changes, rebuild and, if necessary, restart the bot process to observe behavior.  For small modifications, the `watch run` task can auto-rebuild.

---

*This document is intended to orient AI coding agents (e.g. Copilot) working in this repository.  It should be updated if major architectural shifts occur.*
