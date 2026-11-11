# Session F (WP8): the release system

Done 2026-11-11 on `dev/2026-11`, from [plan-next.md](../plan-next.md) section 2, WP8. Guide for users and maintainers: [releasing.md](../../releasing.md).

## What was built

| file | role |
|---|---|
| `install.py` (repository root) | selective installer / uninstaller, standard library only; also attached to each release |
| `build/release_components.py` | component registry, `git ls-files` selection, reproducible `.tar.gz` writer, manifest build and merge, ZOS-API gate |
| `build/package.py` | `--components` / `--exclude`; `build_release` slices the output of the existing `Builder` steps into assets |
| `.github/workflows/release.yml` | `managed`, `native` (matrix) and `publish` jobs; draft release |
| `python/tests/test_release.py` | 23 tests |

## Decisions (and what differs from the plan text)

* **One archive format.** Every asset is a `.tar.gz` (Python reads it on Windows too; keeps executable bits). The wheel is inside `python-<ver>.tar.gz` and the installer runs `pip --find-links` on the unpacked folder.
* **Where `--include` resolution lives.** `install.py` holds `expand` / `resolve`; the packager imports it, so the selection rules cannot drift between what is built and what is installed.
* **Source components use repository paths** (`docs/examples/...`), so a release install and a git sparse checkout give the same tree. `examples.shared` (the files directly in `docs/examples/`) is a dependency of every group.
* **Parts of a group** (`examples.Branched.pump-probe`) are not separate assets (about 60 examples would be 60 downloads): the group archive is downloaded and only the matching members are written, plus the files directly in the group folder. Uninstall of such a part works the same way from the record.
* **`python-native` is not in `all`** and conflicts with `python`; it counts as `python` for dependencies (`provides`). `media` is not in `all`.
* **Plugins are separate components** (`plugins.<id>`, installed to `bin/plugins/<id>`, needing `cli`); the hosted wheel still carries every plugin it was built with.
* **Git mode** uses cone-mode sparse checkout: it also brings the files directly in parent folders, which is how an example gets its shared helpers, but it cannot separate figures (`media`) from folders. Uninstall shrinks the sparse set; a checkout with local changes is kept.
* **Source of truth for source components is `git ls-files`**, so results folders, untracked files and the licensed paper PDFs cannot enter an asset (a `.pdf` under `docs/papers` is also refused by name). `CLAUDE.md` agent notes are left out of every component except `source`, which is the repository as it is.
* **The ZOS-API gate matches `ZOSAPI*.dll/.xml/.pdb`**, not the name `ZosApiStubs` (those two tracked files are our own stub sources).
* **macOS NativeAOT** (`cpp`, `python-native` for osx-*) is not in the workflow: the macOS runner is not available to this repository (same reason as `ci.yml`). The owner builds those on the Mac and uploads them; the CLI for macOS is cross-published on Linux.

## What was run, and what was not

Run on macOS arm64 (2026-11-11):

* `python3 -m pytest python/tests/test_release.py` (23 passed) and the neighbouring packaging tests.
* A real build `--components schemas examples.Branched media papers tests plugins.fitting cli python` (CLI self-contained for osx-arm64): 9 assets, ZOS-API check clean; `--list`, an install of `cli plugins tests examples.Branched.pump-probe python`, the refusal to remove `cli` / `python` while something needs them, and a full uninstall that left the destination empty.
* The `publish` job's merge, gate and installer steps by hand on that output (`ultrafast --version` from the installed CLI gives 2.2.0).

**Not run:** the workflow on GitHub (YAML parses; action versions follow `ci.yml`), the Linux and Windows NativeAOT builds and the CMake smoke test, the wheel installation into a fresh virtual environment (`pip` step; its command line is tested with `--dry-run`), the quickstart from the installed wheel, `--source git` against GitHub (tested against a temporary local repository, where git warns that the filter is ignored), a Windows run of `install.py`, and the cross-published CLI for the other four platforms (needs nuget.org runtime packs). Treat the first tag build as a rehearsal: it makes a **draft** release.

## Open

* The `release.yml` smoke test installs `examples.Branched` but does not run the example (needs the wheel); a later step can.
* No `--verify` command for an existing install (re-hash every recorded file); the record has what is needed.
* `install.py` does not choose between several releases offline; it needs the manifest.
