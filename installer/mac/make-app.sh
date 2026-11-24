#!/bin/sh
# Turns the output of `dotnet publish src/DS4Handler.Mac -r osx-...` into "DS4 Handler.app" and zips it.
# Usage: make-app.sh <publish-dir> <numeric version, e.g. 2.4.0> <output.zip>
# Runs on macOS (codesign, ditto).
set -eu

publish=$1
version=$2
zip=$3
here=$(cd "$(dirname "$0")" && pwd)
work=$(mktemp -d)
app="$work/DS4 Handler.app"

mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$publish/." "$app/Contents/MacOS/"
chmod +x "$app/Contents/MacOS/DS4Handler"
sed "s/@VERSION@/$version/g" "$here/Info.plist" > "$app/Contents/Info.plist"
cp "$here/../../src/DS4Handler.App/Assets/Logo/AppIcon.icns" "$app/Contents/Resources/AppIcon.icns"

# Ad-hoc signature: no Apple account needed, but without any signature Apple-chip Macs
# call the app "damaged" instead of offering to open it anyway.
codesign --force --deep -s - "$app"

mkdir -p "$(dirname "$zip")"
# ditto keeps the signature and the executable bit, which plain zip tools can lose.
ditto -c -k --keepParent "$app" "$zip"
rm -rf "$work"
