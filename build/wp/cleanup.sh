#!/bin/bash
# Removes the worktree and branch of a work package that is already merged into dev/2026-11.
# Safe: refuses if the branch is not merged (git branch -d) or the worktree has uncommitted changes (no --force).
# Usage: build/wp/cleanup.sh <id>
cd "$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
id=$1
git merge-base --is-ancestor "dev/wp-$id" dev/2026-11 || { echo "dev/wp-$id is not merged into dev/2026-11"; exit 1; }
wt=$(git worktree list | awk -v b="[dev/wp-$id]" '$3==b{print $1}')
[ -n "$wt" ] && git worktree remove "$wt"
git branch -d "dev/wp-$id"
