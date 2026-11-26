#!/bin/bash
# Merges a finished work package branch dev/wp-<id> into dev/2026-11 (roadmap-internal.md section 0), then pushes it.
# Usage (from the main worktree): build/wp/merge.sh <id> "<one-line summary>"
# Append-only files (CHANGELOG, history, roadmap, testing, status, llms.txt, mkdocs.yml) are resolved by keeping both sides;
# generated files take the WP side and leave a marker so build/wp/regen.sh runs next; history headings "Stage XX" are numbered.
# The WP's worktree and branch are left in place; remove them with build/wp/cleanup.sh <id> after checking the merge.
set -e
cd "$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
id=$1; msg=$2; ID=$(echo "$id" | tr a-z A-Z); dev=dev/2026-11
[ "$(git branch --show-current)" = "$dev" ] || { echo "the main worktree must be on $dev"; exit 1; }
if git cat-file -e "dev/wp-$id:HANDOFF.md" 2>/dev/null; then
  echo "dev/wp-$id still contains HANDOFF.md: the WP is not finished (its final commit deletes HANDOFF.md)"; exit 1
fi
git merge --no-ff "dev/wp-$id" -m "Merge WP $ID: $msg

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" >/dev/null 2>&1 || true
union=" CHANGELOG.md docs/design/history.md docs/design/roadmap-internal.md docs/testing.md PROJECT_STATUS.md llms.txt mkdocs.yml "
for f in $(git diff --name-only --diff-filter=U); do
  if echo "$union" | grep -q " $f "; then
    python3 - "$f" <<'PY'
import sys, re
p = sys.argv[1]; s = open(p).read()
s = re.sub(r'<<<<<<< [^\n]*\n(.*?)=======\n(.*?)>>>>>>> [^\n]*\n',
           lambda m: m.group(1) + ('\n' if p.endswith('history.md') else '') + m.group(2), s, flags=re.S)
open(p, 'w').write(s)
PY
    git add "$f"
  elif echo "$f" | grep -qE "validation-matrix.md|options.md|python-api.md|materials-catalogue.md|schemas/|types.py"; then
    git checkout --theirs -- "$f" && git add "$f" && touch .git/UF_REGEN_NEEDED
    echo "GENERATED $f: took the WP side; run build/wp/regen.sh"
  else
    echo "UNRESOLVED $f"
  fi
done
python3 - <<'PY'
import re
p = 'docs/design/history.md'; s = open(p).read(); n = [17]
def f(m):
    if m.group(1) != 'XX' and int(m.group(1)) <= 17: return m.group(0)
    n[0] += 1; return '## Stage %d --' % n[0]
open(p, 'w').write(re.sub(r'^## Stage (\d+|XX) --', f, s, flags=re.M))
PY
git add docs/design/history.md
if git diff --name-only --diff-filter=U | grep -q .; then
  echo "CONFLICTS REMAIN (resolve by hand, then git commit):"; git diff --name-only --diff-filter=U; exit 1
fi
if [ -f .git/MERGE_HEAD ]; then git commit -q --no-edit; else git diff --cached --quiet || git commit -q --amend --no-edit; fi
git log --oneline -1
if [ -f .git/UF_REGEN_NEEDED ]; then
  echo "REGEN NEEDED: run build/wp/regen.sh, commit the generated files, then git push origin $dev"
else
  git push -q origin "$dev" && echo "pushed $dev"
fi
