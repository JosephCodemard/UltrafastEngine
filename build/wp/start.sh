#!/bin/bash
# Creates the worktree ../uf-wp-<id> on a new branch dev/wp-<id> from dev/2026-11, or reports the existing one.
# Usage: build/wp/start.sh <id>
cd "$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
id=$1; wt=../uf-wp-$id
if git show-ref --quiet "refs/heads/dev/wp-$id"; then
  echo "exists: $wt (branch dev/wp-$id); read its HANDOFF.md before continuing"
else
  git worktree add -q "$wt" -b "dev/wp-$id" dev/2026-11 && echo "created $wt (branch dev/wp-$id)"
fi
