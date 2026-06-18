# VHS tape for the README demo GIF — render with: scripts/record-demo.sh
# (the wrapper builds the app, seeds an isolated ~/.punch, and puts a `punch`
#  shim on PATH so the launch line below works deterministically).
#
# VHS can't send function keys, so this drives the ticket picker/summary via the
# Ctrl+P / Ctrl+T aliases (see PunchController).

Output assets/punch-demo.gif

Set Shell bash
Set FontSize 20
# Width 1216 makes the terminal's column grid fill the frame evenly, so the
# panel borders sit symmetrically (1200 left a lopsided gap on the right that
# read as the timeline bar "wrapping").
Set Width 1216
Set Height 768
Set Padding 20
Set Margin 48
Set MarginFill "#176b3a"
Set WindowBar ColorfulRight
Set BorderRadius 10
Set TypingSpeed 90ms
Env TERM xterm-256color

# --- launch (hidden so build/shell noise stays out of the frame) ---
Hide
Type "punch --date 2026-06-17"
Enter
Sleep 3s
Show
Sleep 1500ms

# --- book a short standup (30 min) ---
Up 1
Sleep 800ms
Type "Standup"
Sleep 400ms
Tab
Sleep 500ms
Type "PROJ-123"
Sleep 800ms
Enter
Sleep 1500ms

# --- book a longer block of work (2h) ---
Up 7
Sleep 800ms
Type "Code review"
Sleep 400ms
Tab
Sleep 500ms
Type "PROJ-456"
Sleep 800ms
Enter
Sleep 1800ms

# --- select the last entry and reassign its ticket via the picker (Ctrl+P) ---
Left
Sleep 1s
Ctrl+P
Sleep 1500ms
Down 2
Sleep 1s
Enter
Sleep 1800ms

# --- edit the selected entry in place (Ctrl+E), extend by 15 min ---
Ctrl+E
Sleep 1s
Up 1
Sleep 1s
Enter
Sleep 1800ms

# --- ticket summary with billable subtotals (Ctrl+T) ---
Ctrl+T
Sleep 3500ms
Ctrl+T
Sleep 1s

# --- help overlay ---
Type "?"
Sleep 3500ms
Space
Sleep 1200ms

# --- quit (two-step: Ctrl+Q then q) ---
Ctrl+Q
Type "q"
Sleep 1s
