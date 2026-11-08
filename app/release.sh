#!/bin/zsh
# 배포: Apple Silicon + Intel 앱을 빌드해 aADHD.zip 으로 묶고, 깃허브 릴리스 v<VERSION> 으로 올린다.
# FocusGuard.zip 은 같은 앱을 예전 이름(FocusGuard.app)으로 담은 것 — 1.7.x 이하의 업데이트 버튼이 이 파일을 받는다.
# 릴리스 설명은 CHANGELOG.md 의 해당 버전 부분. (커밋 · 태그 · 푸시를 먼저 해 둔다)
set -euo pipefail
cd "$(dirname "$0")"

VERSION=$(tr -d '[:space:]' < VERSION)
git -C .. rev-parse "v$VERSION" >/dev/null 2>&1 || { echo "태그 v$VERSION 이 없어요. 커밋 · 태그 · 푸시를 먼저 하세요."; exit 1; }

UNIVERSAL=1 ./build.sh

OUT=$(mktemp -d)
trap 'rm -rf "$OUT"' EXIT
ditto -c -k --keepParent aADHD.app "$OUT/aADHD.zip"
mkdir "$OUT/legacy" && ditto aADHD.app "$OUT/legacy/FocusGuard.app"
ditto -c -k --keepParent "$OUT/legacy/FocusGuard.app" "$OUT/FocusGuard.zip"
# "## 1.6.0 — …" 다음 줄부터 다음 "## " 전까지
awk -v v="$VERSION" '/^## /{ if (on) exit; if ($2 == v) { on = 1; next } } on' ../CHANGELOG.md > "$OUT/notes.md"

gh release create "v$VERSION" "$OUT/aADHD.zip" "$OUT/FocusGuard.zip" --title "aADHD $VERSION" --notes-file "$OUT/notes.md"
echo "released v$VERSION"
