#!/bin/zsh
# 배포: Apple Silicon + Intel 앱을 빌드해 FocusGuard.zip 으로 묶고, 깃허브 릴리스 v<VERSION> 으로 올린다.
# 릴리스 설명은 CHANGELOG.md 의 해당 버전 부분. (커밋 · 태그 · 푸시를 먼저 해 둔다)
set -euo pipefail
cd "$(dirname "$0")"

VERSION=$(tr -d '[:space:]' < VERSION)
git -C .. rev-parse "v$VERSION" >/dev/null 2>&1 || { echo "태그 v$VERSION 이 없어요. 커밋 · 태그 · 푸시를 먼저 하세요."; exit 1; }

UNIVERSAL=1 ./build.sh

OUT=$(mktemp -d)
trap 'rm -rf "$OUT"' EXIT
ditto -c -k --keepParent FocusGuard.app "$OUT/FocusGuard.zip"
# "## 1.6.0 — …" 다음 줄부터 다음 "## " 전까지
awk -v v="$VERSION" '/^## /{ if (on) exit; if ($2 == v) { on = 1; next } } on' ../CHANGELOG.md > "$OUT/notes.md"

gh release create "v$VERSION" "$OUT/FocusGuard.zip" --title "Focus Guard $VERSION" --notes-file "$OUT/notes.md"
echo "released v$VERSION"
