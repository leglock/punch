---
name: verify
description: Build and drive the punch TUI through a raw-mode pty to verify changes at the real surface.
---

# Verifying punch changes

Build first: `dotnet build` (dotnet may live at `/root/.dotnet/dotnet` on remote runners), then drive the built dll directly — not `dotnet run` — through a **raw-mode** pseudo-terminal. Piped stdin does not work (`Console.ReadKey`), and cooked mode swallows Ctrl+Q (XON flow control).

## Recipe that works

Python: `pty.openpty()`, `tty.setraw()` on **both** fds, set the window size via `TIOCSWINSZ` ioctl (e.g. 40x120), then `subprocess.Popen([dotnet, "bin/Debug/net10.0/punch.dll", "--date", "2026-07-14"], stdin=slave, stdout=slave, stderr=slave)` with `HOME` pointed at a temp dir to isolate `~/.punch`, and `TERM=xterm-256color`.

- Send keys one byte at a time with ~50ms gaps; drain the master fd with `select` between actions (allow ~4s for startup).
- Booking: type a label, press Enter (`\r`) — books one 15-min slot at the cursor and advances.
- Ctrl+T (`\x14`) toggles the ticket summary; Ctrl+P (`\x10`) the ticket picker (F-keys don't survive the pty reliably).
- **Quit is two-step**: Ctrl+Q (`\x11`) then `q`. Sending Ctrl+Q alone leaves the app running.
- Fix the date with `--date` so the data file path is predictable; assert on `~/.punch/data/<date>.json` for persisted state.

## Reading the output

No pyte on the runner (pip has no network route). Strip ANSI with a regex and grep the tail for stable strings: the status bar (`N% of 8h`, `?=help F3=summary`), summary rows (`Billable`, `Unbillable`, `Total`). For colors, grep the raw bytes: log-panel squares render as `\x1b[38;5;<n>m■` — 244 = grey50 (non-billable), 202/172 = orange (billable).
