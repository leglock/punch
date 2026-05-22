# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Punch is a TUI time tracker for your workday. Log, label, and export your hours without leaving the terminal. Built with .NET 10 and Spectre.Console.

## Build & Run Commands

```bash
dotnet build                                              # Build the solution
dotnet test                                               # Run all tests
dotnet test --filter "FullyQualifiedName~PunchStorage"    # Run tests matching a name
dotnet test --collect:"XPlat Code Coverage"               # Run tests with coverage (Coverlet)
dotnet run --project src/Punch.CLI                        # Run the app
dotnet run --project src/Punch.CLI -- --version           # Show version
dotnet run --project src/Punch.CLI -- --date 2026-05-04   # Open a specific date
```

## Architecture

Two-project .NET 10 solution (`Punch.slnx`): the `Punch.CLI` console app (`src/Punch.CLI/`, Spectre.Console.Cli for command parsing and TUI rendering) and `Punch.CLI.Tests` (`tests/Punch.CLI.Tests/`, xUnit). All app types live in a single `Program.cs`. The CLI project exposes its internals to the test project via `<InternalsVisibleTo Include="Punch.CLI.Tests" />` — tests reference `PunchStorage`, `TimeBlock`, etc. directly.

- **`PunchCommand`** — default command; renders a full-screen alternate buffer TUI with timeline, entry list, input, and status bar via `AnsiConsole.Live`. The `Execute` method contains the entire TUI event loop — keyboard input is processed in a `while(true)` loop with `Console.ReadKey`, mutating local state variables and calling `UpdateLayout` after each keypress.
- **`PunchCommandSettings`** — CLI options: `--version`/`-v`, `--date`/`-d` (yyyy-MM-dd override).
- **`TimeBlock`** — immutable record representing a booked time slot (StartSlot, Length, Label, Ticket). Slots are 0–95 (96 quarter-hours in a day). Ticket is optional (defaults to `""`). The `IsUnpaid` computed property (case-insensitive "lunch" or "break" substring match on Label) is used to exclude lunch and break blocks from the workday total.
- **`PunchStorage`** — static helper for JSON persistence. One file per day at `~/.punch/data/yyyy-MM-dd.json` (override via `DataDirectoryOverride` — used by tests to isolate to a temp dir). Auto-saves on every add, edit, and delete. Validates blocks on load (skips invalid ranges and overlaps).
- **`PunchData` / `TimeBlockDto`** — JSON serialization DTOs.

### TUI State Model

The TUI has three modes driven by local variables in the event loop:
1. **Free cursor** (`selectedBlock == null, !editing`) — arrow keys move cursor/resize selection, typing fills the input buffer, Enter books a new block.
2. **Block selected** (`selectedBlock != null, !editing`) — navigating onto a booked block selects it. Ctrl+E enters edit mode, Ctrl+D deletes.
3. **Editing** (`selectedBlock != null, editing`) — input buffer is pre-filled with the block's label and ticket; Enter saves the edit (description must be non-empty).

The input panel has two stacked fields (Description and Ticket) with Tab to switch focus. The active field shows a bold label and inverted cursor; the inactive field is dimmed. Ticket input is auto-uppercased. The `activeField` variable (0=Description, 1=Ticket) tracks focus, and `currentBuffer`/`currentCursor` aliases route keyboard input to the correct field.

The timeline bar in `UpdateLayout` maps 96 quarter-hour slots to pixel positions using a fixed `pixelsPerSlot` ratio, with alternating orange colors for adjacent blocks and yellow/white for the current selection.

## Conventions

- File-scoped namespaces (`namespace Punch.CLI;`)
- No top-level statements — always use `Program.Main`
- `internal sealed` for non-public command classes
- Version set in csproj `<Version>` property; `IncludeSourceRevisionInInformationalVersion` is disabled

## CI/CD

GitHub Actions workflow (`.github/workflows/dotnet.yml`) runs on push to main and PRs:
- **`build` job** — restore, build, test (runs on all triggers)
- **`coverage` job** — collects coverage with Coverlet (`coverlet.collector` in the test project, `--collect:"XPlat Code Coverage"`), builds an HTML report + shields.io badge JSON via ReportGenerator (runs on all triggers)
- **`deploy-pages` job** — publishes the coverage report to GitHub Pages (push to main only)
- **`release` job** — publishes self-contained Windows and Linux binaries and creates a GitHub release (push to main only). Release tags follow `v{csproj-version}-build.{run_number}` format. The build number is stamped into `AssemblyInformationalVersion` via `-p:Version=` so `punch --version` reports the full version.
