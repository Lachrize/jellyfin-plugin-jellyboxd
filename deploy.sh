#!/usr/bin/env bash
#
# Build the Jellyboxd plugin and deploy it into the local Jellyfin plugins folder.
# Stops Jellyfin before copying (never overwrite a loaded .dll), then relaunches it
# so the rating widget gets re-injected.
#
# Usage: ./deploy.sh
#
# Env overrides:
#   DOTNET               path to a .NET 9 SDK `dotnet` (Jellyfin 10.11 = net9.0)
#   JELLYFIN_PLUGINS_DIR Jellyfin plugins directory
#
set -euo pipefail

DOTNET="${DOTNET:-/opt/homebrew/Cellar/dotnet@9/9.0.115/bin/dotnet}"
HERE="$(cd "$(dirname "$0")" && pwd)"
PROJ="$HERE/Jellyfin.Plugin.Jellyboxd"
PLUGINS_DIR="${JELLYFIN_PLUGINS_DIR:-$HOME/Library/Application Support/jellyfin/plugins}"
DEST="$PLUGINS_DIR/Jellyboxd Sync_1.0.1.0"

echo "==> Building (net9.0, Release)…"
"$DOTNET" build -c Release "$PROJ/Jellyfin.Plugin.Jellyboxd.csproj" >/dev/null

echo "==> Stopping Jellyfin…"
pkill -i -f "Jellyfin.app/Contents/MacOS" 2>/dev/null || true
for _ in $(seq 1 20); do
  pgrep -f "Jellyfin.app/Contents/MacOS" >/dev/null 2>&1 || break
  curl -s -o /dev/null -m 1 http://localhost:8096 2>/dev/null || true
done

echo "==> Deploying to: $DEST"
# Remove older version folders (same plugin GUID) to avoid duplicates.
find "$PLUGINS_DIR" -maxdepth 1 -type d -name "Jellyboxd Sync_*" ! -path "$DEST" -exec rm -rf {} + 2>/dev/null || true
mkdir -p "$DEST"
cp "$PROJ/bin/Release/net9.0/Jellyfin.Plugin.Jellyboxd.dll" "$DEST/"
cp "$PROJ/meta.json" "$DEST/"

echo "==> Relaunching Jellyfin…"
for _ in $(seq 1 15); do
  open -a Jellyfin 2>/dev/null && break
  curl -s -o /dev/null -m 2 http://localhost:8096 2>/dev/null || true
done

echo "Done. Hard-refresh the Jellyfin web page (Cmd+Shift+R) to pick up the widget."
