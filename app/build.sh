#!/bin/zsh
# Focus Guard 맥 앱을 빌드해서 app/FocusGuard.app 을 만든다.
# 기록은 앱 밖(data.json)에 저장되므로 다시 빌드해도 지워지지 않는다.
set -euo pipefail
cd "$(dirname "$0")"

APP=FocusGuard.app
VERSION=$(tr -d '[:space:]' < VERSION) # 버전은 app/VERSION 한 곳에서 관리 (변경 내역: CHANGELOG.md)
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# 기본: 이 맥의 칩에 맞게 빌드. UNIVERSAL=1 이면 Apple Silicon + Intel 둘 다 (배포용, release.sh 가 사용)
if [[ "${UNIVERSAL:-0}" == 1 ]]; then
  BIN=$(mktemp -d)
  for arch in arm64 x86_64; do
    swiftc -O -target "$arch-apple-macos14.0" -o "$BIN/FocusGuard-$arch" Sources/*.swift
  done
  lipo -create -output "$APP/Contents/MacOS/FocusGuard" "$BIN/FocusGuard-arm64" "$BIN/FocusGuard-x86_64"
  rm -rf "$BIN"
else
  swiftc -O -target "$(uname -m)-apple-macos14.0" -o "$APP/Contents/MacOS/FocusGuard" Sources/*.swift
fi

# 🍅 앱 아이콘 (Finder · Dock · ⌘Tab 에 쓰인다)
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
swiftc -o "$TMP/MakeIcon" MakeIcon.swift
"$TMP/MakeIcon" "$TMP/AppIcon.iconset"
iconutil -c icns "$TMP/AppIcon.iconset" -o "$APP/Contents/Resources/AppIcon.icns"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key><string>local.focusguard.app</string>
  <key>CFBundleName</key><string>FocusGuard</string>
  <key>CFBundleDisplayName</key><string>Focus Guard</string>
  <key>CFBundleExecutable</key><string>FocusGuard</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>LSUIElement</key><true/>
  <key>NSRemindersFullAccessUsageDescription</key><string>설정에서 아이폰·아이패드 동기화를 켜면, 할 일과 나중에 할 것을 미리 알림 목록에 넣고 체크한 내용을 가져와요.</string>
  <key>NSRemindersUsageDescription</key><string>설정에서 아이폰·아이패드 동기화를 켜면, 할 일과 나중에 할 것을 미리 알림 목록에 넣고 체크한 내용을 가져와요.</string>
  <key>NSAppleEventsUsageDescription</key><string>집중 시간에 분산 사이트 탭을 차단 페이지로 바꾸고, 딴짓 시간을 기록하려고 브라우저의 현재 탭 주소를 확인해요.</string>
</dict>
</plist>
PLIST

plutil -insert CFBundleShortVersionString -string "$VERSION" "$APP/Contents/Info.plist"
plutil -insert CFBundleVersion -string "$VERSION" "$APP/Contents/Info.plist"

# 서명: ../.signing 에 인증서가 있으면 그걸로 (배포용 — 앱 정체가 업데이트해도 같아서 브라우저 권한을 한 번만 묻는다).
# 없으면 ad-hoc 서명 (소스로 직접 빌드하는 경우).
SIGN_DIR=../.signing
if [[ -f "$SIGN_DIR/FocusGuard.keychain-db" ]]; then
  KC="$(cd "$SIGN_DIR" && pwd)/FocusGuard.keychain-db"
  ORIG=(${(f)"$(security list-keychains -d user | tr -d '" ')"})
  restore_keychains() { security list-keychains -d user -s "${ORIG[@]}" }
  trap 'restore_keychains; rm -rf "$TMP"' EXIT
  security list-keychains -d user -s "${ORIG[@]}" "$KC" # 서명하는 동안만 검색 목록에 추가
  security unlock-keychain -p "$(cat "$SIGN_DIR/keychain-password")" "$KC"
  IDENTITY=$(security find-identity -p codesigning "$KC" | awk '/"Focus Guard Signing"/ {print $2; exit}')
  codesign --force --keychain "$KC" --sign "$IDENTITY" "$APP" >/dev/null
  restore_keychains
else
  codesign --force --sign - "$APP" >/dev/null
fi
# 바뀐 아이콘이 바로 보이도록 시스템에 다시 등록
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$APP" || true
echo "built $(pwd)/$APP (v$VERSION)"
