#!/usr/bin/env bash
# One-click rebuild for HandwriteInput installer.
# Steps: publish self-contained -> clean publish dir -> regenerate files.wxi -> build MSI -> validate.
# Usage:   bash build-installer.sh [version]
# Example: bash build-installer.sh 1.0.1

set -euo pipefail

# ---- resolve paths ----
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$ROOT/src/HandwriteInput.PoC"
CSPROJ="$SRC/HandwriteInput.PoC.csproj"
PUBLISH="$ROOT/publish/win-x64"
INSTALLER="$ROOT/installer"
ICON="$ROOT/assets/app.ico"
WXS="$INSTALLER/HandwriteInput.wxs"
WXI="$INSTALLER/files.wxi"
OUT_MSI="$INSTALLER/HandwriteInput.msi"

DOTNET="${LOCALAPPDATA:-$HOME/AppData/Local}/Microsoft/dotnet/dotnet.exe"
WIX="$HOME/.dotnet/tools/wix.exe"

VERSION="${1:-1.0.0}"

# ---- guards ----
[ -f "$DOTNET" ] || { echo "ERROR: dotnet not found at $DOTNET" >&2; exit 1; }
[ -f "$WIX" ]    || { echo "ERROR: wix not found at $WIX (run: dotnet tool install --global wix && wix extension add WixToolset.UI.wixext)" >&2; exit 1; }
[ -f "$ICON" ]   || { echo "ERROR: icon not found at $ICON" >&2; exit 1; }

echo "=== [1/5] dotnet publish (self-contained win-x64) ==="
"$DOTNET" publish "$CSPROJ" -c Release -r win-x64 --self-contained true \
    -o "$PUBLISH" -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=true

echo "=== [2/5] clean publish dir (pdb / DirectML.Debug / testimg) ==="
find "$PUBLISH" -name "*.pdb" -delete
rm -f "$PUBLISH/DirectML.Debug.dll"
rm -rf "$PUBLISH/models/testimg"

echo "=== [3/5] regenerate files.wxi (component list) ==="
rm -f "$WXI"
{
  echo '<Include xmlns="http://wixtoolset.org/schemas/v4/wxs">'
  echo '  <Fragment>'
  echo '  <ComponentGroup Id="AppFiles" Directory="INSTALLFOLDER">'
  for f in $(find "$PUBLISH" -type f | sort); do
    rel="${f#"$PUBLISH"/}"
    if [ "$rel" = "HandwriteInput.PoC.exe" ]; then continue; fi
    dirpart="$(dirname "$rel")"
    dirid="INSTALLFOLDER"
    [ "$dirpart" = "models/det" ] && dirid="d_models_det"
    [ "$dirpart" = "models/rec" ] && dirid="d_models_rec"
    key="${rel//\//_}"; key="${key//-/_}"
    echo "    <Component Id=\"c_$key\" Guid=\"*\" Directory=\"$dirid\">"
    echo "      <File Id=\"f_$key\" Source=\"$rel\" KeyPath=\"yes\" />"
    echo "    </Component>"
  done
  echo '  </ComponentGroup>'
  echo '  </Fragment>'
  echo '</Include>'
} > "$WXI"
echo "  components: $(grep -c '<Component Id=' "$WXI")"

echo "=== [4/5] build MSI ==="
"$WIX" build "$WXS" \
    -b "$PUBLISH" \
    -i "$INSTALLER" \
    -d IconFile="$ICON" \
    -d AppVersion="$VERSION" \
    -arch x64 -culture zh-CN \
    -out "$OUT_MSI" \
    -ext WixToolset.UI.wixext

echo "=== [5/5] validate MSI ==="
"$WIX" msi validate "$OUT_MSI"

echo ""
echo "DONE: $OUT_MSI"
ls -la "$OUT_MSI"
