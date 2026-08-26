#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJECT="$ROOT/PlanEditor.App/PlanEditor.App.csproj"
VERSION="$(tr -d '[:space:]' < "$ROOT/packaging/common/version.txt")"
SHORT_VERSION="${VERSION%%-*}"
BUNDLE_VERSION="${SHORT_VERSION}.1"
RID="${PAS_MAC_RID:-osx-arm64}"
BUILD_ROOT="$ROOT/build/beta"
PUBLISH="$BUILD_ROOT/macos/$RID/publish"
STAGE="$BUILD_ROOT/macos/$RID/stage"
DIST="$ROOT/dist/beta"
APP="$STAGE/PA-S.app"
DMG="$DIST/PA-S-${VERSION}-macOS-${RID}.dmg"
ZIP="$DIST/PA-S-${VERSION}-macOS-${RID}.zip"
APP_ICON_SOURCE="$ROOT/PlanEditor.App/Assets/AppIcon/app.icns"
DOC_ICON_SOURCE="$ROOT/PlanEditor.App/Assets/AppIcon/pas-project.icns"

[ -f "$PROJECT" ] || { echo "Không tìm thấy $PROJECT"; exit 1; }
[ -f "$APP_ICON_SOURCE" ] || { echo "Thiếu $APP_ICON_SOURCE"; exit 1; }
if [ ! -f "$DOC_ICON_SOURCE" ]; then echo "Chưa có pas-project.icns -> dùng app.icns tạm thời."; DOC_ICON_SOURCE="$APP_ICON_SOURCE"; fi

rm -rf "$PUBLISH" "$STAGE"
mkdir -p "$PUBLISH" "$APP/Contents/MacOS" "$APP/Contents/Resources" "$DIST"

dotnet publish "$PROJECT" -c Release -r "$RID" --self-contained true \
  -p:UseAppHost=true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false \
  -p:Version="$SHORT_VERSION" -o "$PUBLISH"

EXECUTABLE=""
if [ -f "$PUBLISH/PA-S" ]; then EXECUTABLE="PA-S"; elif [ -f "$PUBLISH/PlanEditor.App" ]; then EXECUTABLE="PlanEditor.App"; fi
[ -n "$EXECUTABLE" ] || { echo "Không tìm được executable sau publish."; exit 1; }

cp -R "$PUBLISH/." "$APP/Contents/MacOS/"
if [ "$EXECUTABLE" != "PA-S" ]; then mv "$APP/Contents/MacOS/$EXECUTABLE" "$APP/Contents/MacOS/PA-S"; fi
chmod +x "$APP/Contents/MacOS/PA-S"

sed -e "s/__BUNDLE_VERSION__/$BUNDLE_VERSION/g" -e "s/__SHORT_VERSION__/$SHORT_VERSION/g" \
  "$ROOT/packaging/macos/Info.plist.template" > "$APP/Contents/Info.plist"
cp "$APP_ICON_SOURCE" "$APP/Contents/Resources/app.icns"
cp "$DOC_ICON_SOURCE" "$APP/Contents/Resources/pas-project.icns"
plutil -lint "$APP/Contents/Info.plist"

SIGN_IDENTITY="${PAS_MAC_SIGN_IDENTITY:--}"
codesign --force --deep --sign "$SIGN_IDENTITY" "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"

rm -f "$ZIP"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$ZIP"

DMG_ROOT="$STAGE/dmg"
rm -rf "$DMG_ROOT" && mkdir -p "$DMG_ROOT"
cp -R "$APP" "$DMG_ROOT/PA-S.app"
ln -s /Applications "$DMG_ROOT/Applications"
cp "$ROOT/packaging/macos/Uninstall PA-S.command" "$DMG_ROOT/Uninstall PA-S.command"
chmod +x "$DMG_ROOT/Uninstall PA-S.command"
rm -f "$DMG"
hdiutil create -volname "PA-S Beta ${VERSION}" -srcfolder "$DMG_ROOT" -ov -format UDZO "$DMG"

echo "DONE"
echo "ZIP: $ZIP"
echo "DMG: $DMG"
