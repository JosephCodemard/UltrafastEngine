"""Removes duplicate work-package rows in section 0 of docs/design/roadmap-internal.md (left by union merges): for each WP id,
keeps one row, preferring a row whose status is not "open". Run by build/wp/merge.sh after every merge."""
import re, sys
p = sys.argv[1] if len(sys.argv) > 1 else 'docs/design/roadmap-internal.md'
lines = open(p).read().split('\n')
start = next(i for i, l in enumerate(lines) if l.startswith('## 0. '))
end = next(i for i, l in enumerate(lines) if l.startswith('## 0b'))
rowid = lambda l: (m.group(1) if (m := re.match(r'^\| ([A-Za-z0-9*-]+)(?: ⚛)? \|', l)) and m.group(1) != 'WP' else None)
status = lambda l: l.rstrip().rstrip('|').rsplit('|', 1)[-1].strip()
best = {}
for i in range(start, end):
    k = rowid(lines[i])
    if k is None: continue
    if k not in best or (status(lines[best[k]]).startswith('open') and not status(lines[i]).startswith('open')):
        best[k] = i
keep = [l for i, l in enumerate(lines) if not (start <= i < end and rowid(l) and best[rowid(l)] != i)]
open(p, 'w').write('\n'.join(keep))
print(f'dedupe_roadmap: removed {len(lines) - len(keep)} duplicate rows')
