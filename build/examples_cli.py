"""The ``ultrafast-examples`` command (this file is installed as ``ultrafast_examples/cli.py`` by ``build/package.py --examples-wheel``).

    ultrafast-examples list                          # groups and examples
    ultrafast-examples copy Computing DEST           # a whole group (and its shared files) into DEST/Computing/...
    ultrafast-examples copy shg-lbo DEST             # one example by name or Group/name (with what it depends on)
    ultrafast-examples run 4f-correlator             # copy to a temporary folder and run the commands of its README

An installed package should not be written into, so ``copy`` and ``run`` work on copies. The examples need the ``ultrafast`` package;
the ones that call the command line also need the ``ultrafast`` program on the PATH (a published copy of the CLI).
"""
from __future__ import annotations

import argparse
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

# examples that read files of another example, and files every example of a group shares
DEPENDS = {"LaserSystems/nopcpa-bbo": ["LaserSystems/shg-lbo"]}
SKIP = shutil.ignore_patterns("results", "results-*", "figures", "__pycache__", "*.pyc", "*-results", "retrieved")


def data_dir() -> Path:
    """The examples folder: ``$ULTRAFAST_EXAMPLES_DIR``, else the ``data`` folder next to this file, else ``docs/examples`` of a checkout."""
    env = os.environ.get("ULTRAFAST_EXAMPLES_DIR")
    if env:
        return Path(env)
    here = Path(__file__).resolve().parent
    for cand in (here / "data", here.parent / "docs" / "examples"):
        if (cand / "README.md").exists() or any(cand.glob("*/*/README.md")):
            return cand
    raise SystemExit("no examples found next to %s" % here)


def catalogue(root: Path) -> dict:
    """{"Group/name": title} for every example folder (a README.md two levels down)."""
    out = {}
    for readme in sorted(root.glob("*/*/README.md")):
        title = next((l[2:].strip() for l in readme.read_text(encoding="utf-8").splitlines() if l.startswith("# ")), readme.parent.name)
        out[readme.parent.relative_to(root).as_posix()] = title
    return out


def resolve(root: Path, what: str) -> list:
    """The examples a selector names: a group, ``Group/name`` or a bare name."""
    cat = catalogue(root)
    sel = [k for k in cat if k == what or k.split("/")[0] == what or k.split("/")[1] == what]
    if not sel:
        raise SystemExit("no example or group named '%s' (try: ultrafast-examples list)" % what)
    out = []
    for k in sel:                                        # an example brings what it reads
        for d in DEPENDS.get(k, []) + [k]:
            if d not in out:
                out.append(d)
    return out


def copy_examples(root: Path, names: list, dest: Path) -> list:
    done = []
    for name in names:
        group = name.split("/")[0]
        shutil.copytree(root / name, dest / name, ignore=SKIP, dirs_exist_ok=True)
        for f in (root / group).glob("_*.py"):            # support modules shared by the group (Computing/_optics.py)
            shutil.copy2(f, dest / group / f.name)
        done.append(dest / name)
    return done


def readme_commands(readme: Path) -> list:
    out = []
    for lang, body in re.findall(r"```([\w-]*)[^\n]*\n(.*?)```", readme.read_text(encoding="utf-8"), re.S):
        if lang not in ("bash", "sh", "shell"):
            continue
        for line in body.splitlines():
            s = line.strip()
            if s and not s.startswith("#") and not s.startswith("ultrafast-plot") and "PYNLO_SRC" not in s:
                out.append(s)
    return out


SHELL_SYNTAX = re.compile(r"[|&;<>$`()*?]")


def run_command(line: str, folder: Path, env: dict) -> int:
    """Run one README line. A plain command (``python script.py args``, ``ultrafast run a.ufs b.json -o out``) runs directly, so it needs no
    shell and works on Windows; a line with shell syntax needs ``bash``. ``python`` means the interpreter running this command."""
    import shlex
    line = re.sub(r"\s+#.*$", "", line)                 # a trailing comment of the README line
    if SHELL_SYNTAX.search(line):
        bash = shutil.which("bash")
        if bash is None or (os.name == "nt" and "system32" in bash.lower()):      # no bash, or the WSL stub of Windows
            print("skipped (needs a bash shell): " + line)
            return 0
        prelude = 'python() { "%s" "$@"; }\npython3() { "%s" "$@"; }\n' % (Path(sys.executable).as_posix(), Path(sys.executable).as_posix())
        return subprocess.call([bash, "-c", prelude + line], cwd=folder, env=env)
    parts = shlex.split(line)
    if parts[0] in ("python", "python3"):
        parts[0] = sys.executable
    elif shutil.which(parts[0]) is None:
        print("skipped (%s is not on the PATH): %s" % (parts[0], line))
        return 0
    return subprocess.call(parts, cwd=folder, env=env)


def run_example(root: Path, name: str, dest: Path) -> int:
    copy_examples(root, [name], dest)
    folder = dest / name
    env = dict(os.environ, ULTRAFAST_STRICT_OPTIONS="1")
    status = 0
    for line in readme_commands(folder / "README.md"):
        print("$ " + line, flush=True)
        status |= 1 if run_command(line, folder, env) else 0
    return status


def main(argv=None) -> int:
    for stream in (sys.stdout, sys.stderr):              # titles hold non-ASCII characters; a Windows console may be cp1252
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")
    ap = argparse.ArgumentParser(prog="ultrafast-examples", description=__doc__.split("\n\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("list", help="groups and examples")
    c = sub.add_parser("copy", help="copy a group or an example to a writable folder")
    c.add_argument("what")
    c.add_argument("dest")
    r = sub.add_parser("run", help="copy an example to a temporary folder (or --dest) and run its README commands")
    r.add_argument("what")
    r.add_argument("--dest")
    a = ap.parse_args(argv)
    root = data_dir()
    if a.cmd == "list":
        last = None
        for k, title in catalogue(root).items():
            g, n = k.split("/")
            if g != last:
                print(g)
                last = g
            print("  %-34s %s" % (n, title))
        return 0
    names = resolve(root, a.what)
    if a.cmd == "copy":
        for p in copy_examples(root, names, Path(a.dest)):
            print("copied " + str(p))
        return 0
    if len(names) > 2 or (len(names) == 2 and names[-1].split("/")[1] != a.what and names[-1] != a.what):
        raise SystemExit("run takes one example, not a group")
    dest = Path(a.dest) if a.dest else Path(tempfile.mkdtemp(prefix="ultrafast-example-"))
    return run_example(root, names[-1], dest)


if __name__ == "__main__":
    sys.exit(main())
