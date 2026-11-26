"""Find the runnable Python blocks ("snippets") of a Markdown page: the Python tab of an example's "Run it" section (WP D1).

A snippet is a ```python fence directly preceded by a marker line, at the same indentation (inside a content tab, four spaces)::

    === "Python"

        <!-- snippet -->
        ```python
        import ultrafast as uf
        ...
        ```

The marker is an HTML comment, so it does not show on the site. ``<!-- snippet slow -->`` marks a block that takes more than about a
minute at quick fidelity: ``python/tests/test_readme_snippets.py`` runs it only with ``ULTRAFAST_SLOW=1``. Blank lines between the
marker and the fence are allowed; anything else is an error (a marker that does not mark a block). A ```python block without a marker
is not run (use that for fragments that are not complete programs).

    find_snippets(text) -> list[Snippet]     # raises SnippetError on a marker without its block or an unknown option
    Snippet(line, code, slow)                # line: 1-based line of the opening fence; code: dedented source

Used by ``python/tests/test_readme_snippets.py`` (runs every snippet at quick fidelity), ``build/example_lint.py`` (an example whose
``example.json`` has ``"python": true`` must have one) and ``build/example_check.py`` (runs them at the page's own fidelity and checks
the output the README quotes).
"""
from __future__ import annotations

import re
import textwrap
from dataclasses import dataclass
from typing import List

MARKER = re.compile(r"^(?P<indent>[ \t]*)<!--\s*snippet(?P<opts>(?:\s+[\w-]+)*)\s*-->\s*$")
OPTIONS = {"slow"}


class SnippetError(ValueError):
    pass


@dataclass
class Snippet:
    line: int        # 1-based line number of the opening ```python fence
    code: str        # the block's source, dedented
    slow: bool = False


def find_snippets(text: str) -> List[Snippet]:
    lines = text.splitlines()
    out: List[Snippet] = []
    i = 0
    while i < len(lines):
        m = MARKER.match(lines[i])
        if not m:
            i += 1
            continue
        opts = m.group("opts").split()
        bad = [o for o in opts if o not in OPTIONS]
        if bad:
            raise SnippetError("line %d: unknown snippet option(s) %s (known: %s)" % (i + 1, ", ".join(bad), ", ".join(sorted(OPTIONS))))
        indent = m.group("indent")
        j = i + 1
        while j < len(lines) and not lines[j].strip():
            j += 1
        if j >= len(lines) or lines[j].rstrip() not in (indent + "```python", indent + "```py"):
            raise SnippetError("line %d: <!-- snippet --> must be followed by a ```python fence at the same indentation" % (i + 1))
        k = j + 1
        while k < len(lines) and lines[k].rstrip() != indent + "```":
            k += 1
        if k >= len(lines):
            raise SnippetError("line %d: the ```python block is not closed" % (j + 1))
        body = "\n".join(lines[j + 1:k])
        out.append(Snippet(line=j + 1, code=textwrap.dedent(body) + "\n", slow="slow" in opts))
        i = k + 1
    return out
