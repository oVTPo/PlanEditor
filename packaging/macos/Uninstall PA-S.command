#!/bin/bash
set -u
APP_NAME="PA-S"
BUNDLE_ID="vn.pas.planeditor"
SYSTEM_APP="/Applications/${APP_NAME}.app"
USER_APP="$HOME/Applications/${APP_NAME}.app"
LSREGISTER="/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister"

echo "PA-S Beta Uninstaller"
echo "Các file dự án .pas của bạn sẽ KHÔNG bị xóa."

remove_app() {
  local app="$1"
  [ -e "$app" ] || return 0
  if [ -x "$LSREGISTER" ]; then "$LSREGISTER" -u "$app" >/dev/null 2>&1 || true; fi
  if [ -w "$(dirname "$app")" ]; then rm -rf "$app"; else sudo rm -rf "$app"; fi
}

remove_app "$SYSTEM_APP"
remove_app "$USER_APP"
rm -rf "$HOME/Library/Caches/$BUNDLE_ID" 2>/dev/null || true
rm -rf "$HOME/Library/Logs/$APP_NAME" 2>/dev/null || true
rm -rf "$HOME/Library/Saved Application State/${BUNDLE_ID}.savedState" 2>/dev/null || true

echo
read -r -p "Xóa luôn cài đặt PA-S/Recent/User settings? [y/N]: " PURGE
if [[ "$PURGE" =~ ^[Yy]$ ]]; then
  rm -rf "$HOME/Library/Application Support/PA-S"
  defaults delete "$BUNDLE_ID" >/dev/null 2>&1 || true
  echo "Đã xóa dữ liệu cài đặt của PA-S."
else
  echo "Giữ lại dữ liệu cài đặt/Recent."
fi

if [ -x "$LSREGISTER" ]; then "$LSREGISTER" -kill -r -domain local -domain system -domain user >/dev/null 2>&1 || true; fi
killall Finder >/dev/null 2>&1 || true

echo "Đã gỡ PA-S. Các file .pas vẫn được giữ nguyên."
read -r -p "Nhấn Enter để đóng..."
