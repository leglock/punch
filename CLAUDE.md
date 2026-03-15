# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Punch is a TUI time tracker for your workday. Log, label, and export your hours without leaving the terminal. Built with .NET 10 and Spectre.Console.

## Build & Run Commands

```bash
dotnet build                                    # Build the solution
dotnet test                                     # Run all tests (no test project yet)
dotnet run --project src/Punch.CLI              # Run the app
dotnet run --project src/Punch.CLI -- --version # Show version
```

## Architecture

Single-project .NET 10 console app using Spectre.Console.Cli for command parsing and TUI rendering. All types live in `Program.cs`.

- **`PunchCommand`** — default command; renders a full-screen alternate buffer TUI with timeline, entry list, input, and status bar via `AnsiConsole.Live`.
- **`PunchCommandSettings`** — CLI options: `--version`/`-v`, `--date`/`-d` (yyyy-MM-dd override).
- **`TimeBlock`** — immutable record representing a booked time slot (StartSlot, Length, Label). Slots are 0–95 (96 quarter-hours in a day).
- **`PunchStorage`** — static helper for JSON persistence. One file per day at `~/.punch/data/yyyy-MM-dd.json`. Auto-saves on every add, edit, and delete. Validates blocks on load (skips invalid/overlapping).
- **`PunchData` / `TimeBlockDto`** — JSON serialization DTOs.

## Conventions

- File-scoped namespaces (`namespace Punch.CLI;`)
- No top-level statements — always use `Program.Main`
- `internal sealed` for non-public command classes
- Version set in csproj `<Version>` property; `IncludeSourceRevisionInInformationalVersion` is disabled

## CI/CD

GitHub Actions workflow (`.github/workflows/dotnet.yml`) runs on push to main and PRs:
- **`build` job** — restore, build, test (runs on all triggers)
- **`release` job** — publishes a self-contained Windows binary and creates a GitHub release (push to main only). Release tags follow `v{csproj-version}-build.{run_number}` format. The build number is stamped into `AssemblyInformationalVersion` via `-p:Version=` so `punch --version` reports the full version.
