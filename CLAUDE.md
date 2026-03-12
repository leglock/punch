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

Single-project .NET 10 console app using Spectre.Console.Cli for command parsing and TUI rendering.

- **`Punch.CLI`** — the main executable (assembles as `punch`). Uses `CommandApp<PunchCommand>` with a default command that renders a full-screen alternate buffer layout.
- All types currently live in `Program.cs`: `Program`, `PunchCommandSettings`, and `PunchCommand`.
- `--version` / `-v` is a `PunchCommandSettings` option handled in `PunchCommand.Execute` (reads `AssemblyInformationalVersionAttribute`).

## Conventions

- File-scoped namespaces (`namespace Punch.CLI;`)
- No top-level statements — always use `Program.Main`
- `internal sealed` for non-public command classes
- Version set in csproj `<Version>` property; `IncludeSourceRevisionInInformationalVersion` is disabled

## CI/CD

GitHub Actions workflow (`.github/workflows/dotnet.yml`) runs on push to main and PRs:
- **`build` job** — restore, build, test (runs on all triggers)
- **`release` job** — publishes a self-contained Windows binary and creates a GitHub release (push to main only). Release tags follow `v{csproj-version}-build.{run_number}` format. The build number is stamped into `AssemblyInformationalVersion` via `-p:Version=` so `punch --version` reports the full version.
