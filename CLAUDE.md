# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Punch is a TUI time tracker for your workday. Log, label, and export your hours without leaving the terminal. Built with .NET 10 and Spectre.Console.

## Build & Run Commands

```bash
dotnet build                                    # Build the solution
dotnet run --project src/Punch.CLI              # Run the app
dotnet run --project src/Punch.CLI -- --version # Show version
```

## Architecture

Single-project .NET 10 console app using Spectre.Console.Cli for command parsing and TUI rendering.

- **`Punch.CLI`** — the main executable (assembles as `punch`). Uses `CommandApp<PunchCommand>` with a default command that renders a full-screen alternate buffer layout.
- `--version` / `-v` is handled manually before Spectre.Console.Cli since `CommandApp<T>` with a default command doesn't support it natively.

## Conventions

- File-scoped namespaces (`namespace Punch.CLI;`)
- No top-level statements — always use `Program.Main`
- `internal sealed` for non-public command classes
- Version set in csproj `<Version>` property; `IncludeSourceRevisionInInformationalVersion` is disabled
