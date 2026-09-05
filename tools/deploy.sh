#!/usr/bin/env bash
# Builds/WebGL を gh-pages ブランチの単一コミットとして force push し、GitHub Pages を有効化する。
# 履歴は持たない。前提: tools/unity.sh build 済み、gh にログイン済み。
set -eu
cd "$(dirname "$0")/.."

fail() { echo "FAIL: $*" >&2; exit 1; }

[ -f Builds/WebGL/index.html ] || fail "Builds/WebGL/index.html missing (run tools/unity.sh build)"
repo=$(gh repo view --json nameWithOwner -q .nameWithOwner) || fail "gh repo view failed (not logged in?)"
remote=$(git remote get-url origin)

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
git -C "$work" init -q -b gh-pages
rsync -a --exclude '*_DoNotShip' Builds/WebGL/ "$work"/
touch "$work/.nojekyll"
git -C "$work" add -A
git -C "$work" -c user.name="$(git config user.name)" -c user.email="$(git config user.email)" \
  commit -q -m "deploy $(git rev-parse --short HEAD)"
git -C "$work" push -f "$remote" gh-pages

# Pages が未設定なら gh-pages ブランチのルートを配信元として作る。設定済みなら何もしない。
if ! gh api "repos/$repo/pages" >/dev/null 2>&1; then
  # gh-pages ブランチの初回 push で GitHub 側が自動有効化することがあり、その場合 409 が返る。
  # 成否は直後の GET で判定する。
  gh api -X POST "repos/$repo/pages" -f 'source[branch]=gh-pages' -f 'source[path]=/' >/dev/null 2>&1 || true
fi
url=$(gh api "repos/$repo/pages" -q .html_url) || fail "Pages status unreadable after push"
echo "OK deploy: $url"
