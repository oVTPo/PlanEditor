#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_FILE="$ROOT_DIR/PlanEditor.App/PlanEditor.App.csproj"
CHANGELOG_FILE="$ROOT_DIR/CHANGELOG.md"
MODE="${1:-}"

usage() {
  echo "Dùng: ./release.sh patch | minor | major | beta | X.Y.Z[-beta.N]"
}

[[ -f "$PROJECT_FILE" ]] || { echo "Không tìm thấy $PROJECT_FILE" >&2; exit 1; }
[[ -n "$MODE" ]] || { usage; exit 1; }
[[ -z "$(git -C "$ROOT_DIR" status --porcelain)" ]] || {
  echo "Hãy commit các thay đổi hiện tại trước khi phát hành." >&2
  exit 1
}

CURRENT_VERSION="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$PROJECT_FILE" | head -n 1)"
[[ "$CURRENT_VERSION" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)(-beta\.([0-9]+))?$ ]] || {
  echo "Version không hợp lệ: $CURRENT_VERSION" >&2
  exit 1
}

MAJOR="${BASH_REMATCH[1]}"
MINOR="${BASH_REMATCH[2]}"
PATCH="${BASH_REMATCH[3]}"
BETA_NUMBER="${BASH_REMATCH[5]:-}"

case "$MODE" in
  patch) NEW_VERSION="$MAJOR.$MINOR.$((PATCH + 1))" ;;
  minor) NEW_VERSION="$MAJOR.$((MINOR + 1)).0" ;;
  major) NEW_VERSION="$((MAJOR + 1)).0.0" ;;
  beta)
    if [[ -n "$BETA_NUMBER" ]]; then
      NEW_VERSION="$MAJOR.$MINOR.$PATCH-beta.$((BETA_NUMBER + 1))"
    else
      NEW_VERSION="$MAJOR.$((MINOR + 1)).0-beta.1"
    fi
    ;;
  *)
    [[ "$MODE" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-beta\.[0-9]+)?$ ]] || {
      usage
      exit 1
    }
    NEW_VERSION="$MODE"
    ;;
esac

TAG="v$NEW_VERSION"
git -C "$ROOT_DIR" rev-parse "$TAG" >/dev/null 2>&1 && {
  echo "Tag $TAG đã tồn tại." >&2
  exit 1
}

BASE_VERSION="${NEW_VERSION%%-*}"
IFS='.' read -r NEW_MAJOR NEW_MINOR NEW_PATCH <<< "$BASE_VERSION"
ASSEMBLY_VERSION="$NEW_MAJOR.$NEW_MINOR.$NEW_PATCH.0"

perl -0pi -e "s|<Version>[^<]+</Version>|<Version>$NEW_VERSION</Version>|" "$PROJECT_FILE"
perl -0pi -e "s|<AssemblyVersion>[^<]+</AssemblyVersion>|<AssemblyVersion>$ASSEMBLY_VERSION</AssemblyVersion>|" "$PROJECT_FILE"
perl -0pi -e "s|<FileVersion>[^<]+</FileVersion>|<FileVersion>$ASSEMBLY_VERSION</FileVersion>|" "$PROJECT_FILE"
perl -0pi -e "s|<InformationalVersion>[^<]+</InformationalVersion>|<InformationalVersion>$NEW_VERSION</InformationalVersion>|" "$PROJECT_FILE"

LAST_TAG="$(git -C "$ROOT_DIR" describe --tags --abbrev=0 2>/dev/null || true)"
if [[ -n "$LAST_TAG" ]]; then
  NOTES="$(git -C "$ROOT_DIR" log "$LAST_TAG..HEAD" --pretty='- %s' --no-merges)"
else
  NOTES="$(git -C "$ROOT_DIR" log --pretty='- %s' --no-merges)"
fi
[[ -n "$NOTES" ]] || NOTES="- Cập nhật và ổn định ứng dụng."

TMP_CHANGELOG="$(mktemp)"
{
  head -n 3 "$CHANGELOG_FILE"
  echo
  echo "## [$NEW_VERSION] - $(date +%Y-%m-%d)"
  echo
  echo "$NOTES"
  echo
  tail -n +4 "$CHANGELOG_FILE"
} > "$TMP_CHANGELOG"
mv "$TMP_CHANGELOG" "$CHANGELOG_FILE"

echo "Đang build PA-S $NEW_VERSION..."
if ! dotnet build "$PROJECT_FILE" -c Release; then
  git -C "$ROOT_DIR" checkout -- "$PROJECT_FILE" "$CHANGELOG_FILE"
  echo "Build lỗi; đã hoàn tác số phiên bản." >&2
  exit 1
fi

git -C "$ROOT_DIR" add "$PROJECT_FILE" "$CHANGELOG_FILE"
git -C "$ROOT_DIR" commit -m "chore(release): $TAG"
git -C "$ROOT_DIR" tag -a "$TAG" -m "PA-S $NEW_VERSION"
BRANCH="$(git -C "$ROOT_DIR" branch --show-current)"
git -C "$ROOT_DIR" push origin "$BRANCH"
git -C "$ROOT_DIR" push origin "$TAG"
echo "Đã đẩy $TAG. GitHub Actions đang tạo Release."
