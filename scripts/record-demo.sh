#!/usr/bin/env bash
#
# Refresh assets/punch-demo.gif from the current build.
#
# Drives the live TUI through a real PTY with VHS (charmbracelet/vhs) and renders
# the result straight to a GIF. The TUI reads keys via Console.ReadKey, so it
# needs a real terminal — VHS provides one (via ttyd) and screenshots it.
#
# Requirements (install once, no Go toolchain needed):
#   - ffmpeg   (usually already present)
#   - ttyd     https://github.com/tsl0922/ttyd/releases  -> ~/.local/bin/ttyd
#   - vhs      https://github.com/charmbracelet/vhs/releases -> ~/.local/bin/vhs
#
# Usage:  scripts/record-demo.sh
#
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

# --- tool check ------------------------------------------------------------
missing=0
for tool in vhs ttyd ffmpeg; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "error: '$tool' not found on PATH." >&2
        missing=1
    fi
done
if [[ $missing -ne 0 ]]; then
    cat >&2 <<'EOF'

Install the missing tools, e.g. (Linux x86_64):

  mkdir -p ~/.local/bin
  # vhs
  curl -fsSL https://github.com/charmbracelet/vhs/releases/latest/download/vhs_Linux_x86_64.tar.gz \
    | tar -xz -C /tmp && install /tmp/vhs*/vhs ~/.local/bin/vhs
  # ttyd
  curl -fsSL -o ~/.local/bin/ttyd \
    https://github.com/tsl0922/ttyd/releases/latest/download/ttyd.x86_64 && chmod +x ~/.local/bin/ttyd

Ensure ~/.local/bin is on your PATH, then re-run this script.
EOF
    exit 1
fi

# --- build -----------------------------------------------------------------
echo ">> building (Release)..."
dotnet build -c Release -v quiet >/dev/null
dll="$repo_root/src/Punch.CLI/bin/Release/net10.0/punch.dll"
[[ -f "$dll" ]] || { echo "error: build output not found at $dll" >&2; exit 1; }

# --- isolated, deterministic environment -----------------------------------
# Temp HOME so the demo uses a clean ~/.punch (seeded tickets, no real data),
# and a temp bin dir holding a `punch` shim so the tape can type `punch ...`.
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

mkdir -p "$tmp/.punch"
cat > "$tmp/.punch/tickets.txt" <<'EOF'
# Seeded for the demo recording
PROJ-123,Fix login redirect
PROJ-456,Quarterly report export
PROJ-789,Onboarding flow polish
EOF

mkdir -p "$tmp/bin"
cat > "$tmp/bin/punch" <<EOF
#!/usr/bin/env bash
exec dotnet "$dll" "\$@"
EOF
chmod +x "$tmp/bin/punch"

# --- record ----------------------------------------------------------------
echo ">> recording with VHS..."
HOME="$tmp" PATH="$tmp/bin:$PATH" vhs "$repo_root/scripts/punch-demo.tape"

echo ">> done: assets/punch-demo.gif refreshed."
