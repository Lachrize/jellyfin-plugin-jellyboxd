#!/usr/bin/env bash
#
# Build + package the plugin into dist/ for the GitHub plugin repository.
# Prints the MD5 to paste into manifest.json (checksum).
#
set -euo pipefail

DOTNET="${DOTNET:-/opt/homebrew/Cellar/dotnet@9/9.0.115/bin/dotnet}"
HERE="$(cd "$(dirname "$0")" && pwd)"
PROJ="$HERE/Jellyfin.Plugin.Jellyboxd"
VERSION="1.0.2.0"

echo "==> Building (net9.0, Release)…"
"$DOTNET" build -c Release "$PROJ/Jellyfin.Plugin.Jellyboxd.csproj" >/dev/null

mkdir -p "$HERE/dist"
ZIP="$HERE/dist/jellyboxd_${VERSION}.zip"
rm -f "$ZIP"
( cd "$PROJ" && zip -j "$ZIP" "bin/Release/net9.0/Jellyfin.Plugin.Jellyboxd.dll" meta.json >/dev/null )

echo "zip: $ZIP"
echo "md5: $(md5 -q "$ZIP")"
echo "==> Put that md5 in manifest.json -> versions[].checksum"
