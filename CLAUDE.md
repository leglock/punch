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
dotnet run --project src/Punch.CLI -- --yesterday         # Open yesterday's timesheet
```

The TUI reads keys via `Console.ReadKey`, so it can't be driven by piped stdin or unit tests. To smoke-test interactively, drive the built dll through a **raw-mode** pseudo-terminal (Python `pty` + `tty.setraw` on both fds) — in cooked mode Ctrl+Q (XON) is swallowed by flow control. Run the dll directly to skip `dotnet run` chatter, and set `HOME` to a temp dir to isolate `~/.punch`.

## Architecture

Two-project .NET 10 solution (`Punch.slnx`): the `Punch.CLI` console app (`src/Punch.CLI/`, Spectre.Console.Cli for command parsing and TUI rendering) and `Punch.CLI.Tests` (`tests/Punch.CLI.Tests/`, xUnit). One type per file. The CLI project exposes its internals to the test project via `<InternalsVisibleTo Include="Punch.CLI.Tests" />` — tests reference `PunchStorage`, `TimeBlock`, `DaySchedule`, etc. directly.

Test suites that mutate the shared static `PunchStorage.DataDirectoryOverride` must be tagged `[Collection(StorageCollection.Name)]` so xUnit serializes them (they'd otherwise clobber each other's temp data dir in parallel).

- **`PunchCommand`** — thin default command (~80 lines): parses args, loads the day, sets up the `AnsiConsole.Live` full-screen TUI, then hands off to `PunchController`.
- **`PunchController`** — owns the `while(true)` keyboard loop (`Console.ReadKey`); `Run` dispatches each key to a named handler (`HandleLeftArrow`, `HandleGrow`, `HandleEditStart`, `HandleEnter`, `HandleTextInput`, …) that mutates the `PunchSession`, then persists and re-renders.
- **`PunchSession`** — mutable editor state (input fields, cursor/selection, view toggles) plus the `DaySchedule`. `ActiveBuffer`/`ActiveCursor` route input to the focused field; `ResetInput` clears it.
- **`DaySchedule`** — aggregate owning the block list **and** the 96-slot `occupied[]` mask as an invariant. The ONLY place occupancy is mutated — never touch a raw `occupied[]` outside it. Exposes `Add/Remove/Replace/FindAt/IsFree/IsOverlapping/CanGrow/AdvanceAfterAdd`, plus `FreeSlots`/`FillSlots` for the Ctrl+E edit lift.
- **`PunchView`** — all Spectre rendering, split into `RenderTimeline/Messages/Help/TicketSummary/TicketPicker/Input/StatusBar`, driven by the `PunchSession`. The ticket picker renders beside the time log.
- **`SlotTime` / `Duration`** — slot↔clock-time conversion and human-readable duration formatting (`Humanize` for entries, `HumanizeTotal` for totals).
- **`PunchCommandSettings`** — CLI options: `--version`/`-v`, `--date`/`-d` (yyyy-MM-dd override), `--yesterday`/`-y` (previous day; mutually exclusive with `--date`).
- **`TimeBlock`** — immutable record representing a booked time slot (StartSlot, Length, Label, Ticket). Slots are 0–95 (96 quarter-hours in a day). Ticket is optional (defaults to `""`). The `IsUnpaid` computed property (case-insensitive "lunch" or "break" substring match on Label) is used to exclude lunch and break blocks from the workday total.
- **`PunchStorage`** — static helper for JSON persistence. One file per day at `~/.punch/data/yyyy-MM-dd.json` (override via `DataDirectoryOverride` — used by tests to isolate to a temp dir). Auto-saves on every add, edit, and delete. Validates blocks on load (skips invalid ranges and overlaps).
- **`PunchStorage.LoadTickets`** — reads the manually-maintained `~/.punch/tickets.txt` (tab- or comma-separated `ticket,title` rows; `#` comments and blank lines skipped). Returns `TicketEntry` rows for the picker; missing file yields an empty list.
- **`TicketEntry`** — immutable record (`Ticket`, `Title`) for one row of `tickets.txt`, used by the ticket picker overlay.
- **`PunchStorage.LoadSettings`** — reads the manually-maintained `~/.punch/settings.json` (sits alongside the data dir). Returns a `PunchSettings`; a missing or unparseable file yields defaults. The only key today is `targetHours` (whole-hour daily goal, default 8, clamped to ≥1) used for the status-bar percentage.
- **`PunchSettings`** — JSON DTO for `settings.json`; `TargetHours` is threaded through `PunchSession` into `PunchView.RenderStatusBar`.
- **`PunchData` / `TimeBlockDto`** — JSON serialization DTOs.

### TUI State Model

The TUI has three modes driven by `PunchSession` state, handled in `PunchController`:
1. **Free cursor** (`selectedBlock == null, !editing`) — arrow keys move cursor/resize selection, typing fills the input buffer, Enter books a new block.
2. **Block selected** (`selectedBlock != null, !editing`) — navigating onto a booked block selects it. Ctrl+E enters edit mode, Ctrl+D deletes.
3. **Editing** (`selectedBlock != null, editing`) — input buffer is pre-filled with the block's label and ticket; Enter saves the edit (description must be non-empty).

Two modal overlays layer over these modes (state on `PunchSession`):
- **Ticket picker** (`ShowTicketPicker`, F4 or Ctrl+P) — opens for the selected block when not editing; reloads `Tickets` from disk on open, arrows move `TicketPickerCursor`, Enter assigns the ticket to the block, Esc/F4/Ctrl+P cancel. All other keys are swallowed while open.
- **Ticket summary** (`ShowTicketSummary`, F3 or Ctrl+T) — right-panel view summing hours per ticket with Billable/Unbillable subtotals (split on `TimeBlock.IsUnpaid`); F3/Ctrl+T/Esc closes.

The Ctrl+P/Ctrl+T aliases mirror F4/F3 so terminal recorders (VHS, used by `scripts/record-demo.sh` to refresh the README GIF) that can't send function keys can still drive the picker and summary.

The input panel has two stacked fields (Description and Ticket) with Tab to switch focus. The active field shows a bold label and inverted cursor; the inactive field is dimmed. Ticket input is auto-uppercased. `PunchSession.ActiveField` (0=Description, 1=Ticket) tracks focus; `ActiveBuffer`/`ActiveCursor` route input to it.

`PunchView.RenderTimeline` maps 96 quarter-hour slots to pixel positions using a fixed `pixelsPerSlot` ratio, with alternating orange colors for adjacent blocks and yellow/white for the current selection.

## Conventions

- File-scoped namespaces (`namespace Punch.CLI;`)
- One type per file
- No top-level statements — always use `Program.Main`
- `internal sealed` for non-public command classes
- Version set in csproj `<Version>` property; `IncludeSourceRevisionInInformationalVersion` is disabled

## CI/CD

GitHub Actions workflow (`.github/workflows/dotnet.yml`) runs on push to main and PRs:
- **`build` job** — restore, build, test (runs on all triggers)
- **`coverage` job** — collects coverage with Coverlet (`coverlet.collector` in the test project, `--collect:"XPlat Code Coverage"`), builds an HTML report + shields.io badge JSON via ReportGenerator (runs on all triggers)
- **`deploy-pages` job** — publishes the coverage report to GitHub Pages (push to main only)
- **`release` job** — publishes self-contained Windows and Linux binaries and creates a GitHub release (push to main only). Release tags follow `v{csproj-version}-build.{run_number}` format. The build number is stamped into `AssemblyInformationalVersion` via `-p:Version=` so `punch --version` reports the full version.
