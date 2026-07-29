#!/usr/bin/env bash
#
# Installs the CI/CD workflows from docs/ci/ into .github/workflows/.
#
# The automation account that produced this branch does not hold the GitHub App
# "workflows" permission, so it cannot write under .github/workflows/. Run this
# once from an account or token that can.
#
#   ./scripts/install-workflows.sh          # stage the files
#   ./scripts/install-workflows.sh --commit # stage, commit and push
#
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_dir="$repo_root/docs/ci"
target_dir="$repo_root/.github/workflows"

if [ ! -d "$source_dir" ]; then
  echo "error: $source_dir not found" >&2
  exit 1
fi

mkdir -p "$target_dir"

installed=0
for workflow in "$source_dir"/*.yml; do
  [ -e "$workflow" ] || continue
  cp "$workflow" "$target_dir/"
  echo "installed $(basename "$workflow")"
  installed=$((installed + 1))
done

if [ "$installed" -eq 0 ]; then
  echo "error: no workflow files found in $source_dir" >&2
  exit 1
fi

echo
echo "Installed $installed workflow(s) into .github/workflows/"

if [ "${1:-}" = "--commit" ]; then
  cd "$repo_root"
  git add .github/workflows
  if git diff --cached --quiet; then
    echo "Nothing to commit: workflows are already up to date."
    exit 0
  fi
  git commit -m "ci: install rebuilt workflows"
  git push
  echo "Pushed."
else
  echo
  echo "Next:"
  echo "  git add .github/workflows && git commit -m 'ci: install rebuilt workflows' && git push"
fi
