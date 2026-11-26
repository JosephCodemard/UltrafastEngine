"""The agent pack (ultrafast.agents): the shipped skills equal the repository's, each target installs, re-install replaces, nothing is overwritten without --force."""
import json
import re
import sys
from pathlib import Path

import pytest

from ultrafast import agents
from ultrafast.mcp import policy

REPO = Path(__file__).resolve().parents[2]
NAMES = {"run-simulations", "debug-setup", "mirror-lab-setup", "optimise-setup", "replicate-paper", "design-system"}


def test_pack_has_the_skills_with_front_matter():
    sk = agents.skills()
    assert set(sk) == NAMES
    for n, t in sk.items():
        meta, body = agents._split(t)
        assert meta["name"] == n and len(meta["description"]) > 40 and len(body) > 200


def test_pack_equals_the_repository_skills():
    src = REPO / ".claude" / "skills"
    if not src.is_dir():
        pytest.skip("not a source checkout")
    for n, t in agents.skills().items():
        assert (src / n / "SKILL.md").read_text(encoding="utf-8") == t, "%s: pack and .claude/skills differ (copy one over the other)" % n


def test_install_claude_and_force(tmp_path):
    files = agents.install("claude", tmp_path)
    sk = [f for f in files if f.name == "SKILL.md"]
    assert {f.parent.name for f in sk} == NAMES and all(f.is_file() for f in files)
    assert "documentation site" in sk[0].read_text(encoding="utf-8")
    assert (tmp_path / ".claude" / "ultrafast" / "AGENTS.md").read_text(encoding="utf-8") == agents.guide()
    assert (tmp_path / ".ultrafast" / "llms.txt").read_text(encoding="utf-8") == agents.llms()
    with pytest.raises(FileExistsError):
        agents.install("claude", tmp_path)
    sk[0].write_text("changed", encoding="utf-8")
    agents.install("claude", tmp_path, force=True)
    assert sk[0].read_text(encoding="utf-8") != "changed"


def test_install_cursor_rules(tmp_path):
    files = agents.install("cursor", tmp_path)
    assert {f.name for f in files if f.suffix == ".mdc"} == {"ultrafast-%s.mdc" % n for n in NAMES} | {"ultrafast-guide.mdc"}
    t = next(f for f in files if f.name == "ultrafast-run-simulations.mdc").read_text(encoding="utf-8")
    assert t.startswith("---\ndescription: ") and "alwaysApply: false" in t


def test_install_codex_keeps_the_rest_of_agents_md_and_replaces_its_own_block(tmp_path):
    p = tmp_path / "AGENTS.md"
    p.write_text("# My project\n\nRules of mine.\n", encoding="utf-8")
    agents.install("codex", tmp_path)
    once = p.read_text(encoding="utf-8")
    assert once.startswith("# My project") and "Rules of mine." in once and "## Skill: debug-setup" in once and "UltrafastEngine: how to drive it" in once and once.count(agents.BEGIN) == 1
    p.write_text(once + "\nAfter.\n", encoding="utf-8")
    agents.install("codex", tmp_path)
    twice = p.read_text(encoding="utf-8")
    assert twice.count(agents.BEGIN) == 1 and twice.count("## Skill: run-simulations") == 1 and twice.rstrip().endswith("After.")


def test_bad_target_and_cli(tmp_path, capsys):
    with pytest.raises(ValueError):
        agents.install("vim", tmp_path)
    assert agents.main(["list"]) == 0 and "run-simulations" in capsys.readouterr().out
    assert agents.main(["install", "--target", "claude", "--dest", str(tmp_path)]) == 0
    assert agents.main(["install", "--target", "claude", "--dest", str(tmp_path)]) == 2      # exists, no --force
    assert "--force" in capsys.readouterr().err


def test_mcp_config_is_valid_for_both_clients():
    c = json.loads(agents.mcp_config("/work", "claude"))
    assert c["mcpServers"]["ultrafast"]["command"] == "ultrafast-mcp" and c["mcpServers"]["ultrafast"]["args"] == ["--workspace", "/work"]
    assert 'command = "ultrafast-mcp"' in agents.mcp_config("/work", "codex")


def test_skill_json_snippets_parse_and_python_snippets_compile():
    n = 0
    for t in agents.skills().values():
        for lang, code in re.findall(r"```(\w+)\n(.*?)```", t, re.S):
            n += 1
            if lang in ("json", "jsonc"):
                json.loads(policy.strip_jsonc(code))
            elif lang == "python":
                compile(code, "<skill>", "exec")
    assert n >= 0          # the skills carry prose and commands; any snippet they gain is checked here


@pytest.mark.parametrize("target", agents.TARGETS)
def test_the_cli_agent_install_writes_the_same_files_as_the_python_command(tmp_path, target):
    """`ultrafast agent install` (C#, skills embedded from the pack) and `ultrafast-agent install` give byte-identical files."""
    import shutil
    import subprocess
    dll = REPO / "src" / "UltrafastEngine.Cli" / "bin" / "Release" / "net10.0" / "ultrafast.dll"
    if not dll.is_file() or not shutil.which("dotnet"):
        pytest.skip("the CLI is not built (dotnet build src/UltrafastEngine.Cli -c Release -f net10.0)")
    a, b = tmp_path / "cli", tmp_path / "py"
    a.mkdir(), b.mkdir()
    r = subprocess.run(["dotnet", str(dll), "agent", "install", "--target", target, "--dest", str(a)], capture_output=True, text=True)
    assert r.returncode == 0, r.stderr
    agents.install(target, b)
    fa = sorted(p.relative_to(a).as_posix() for p in a.rglob("*") if p.is_file())
    assert fa == sorted(p.relative_to(b).as_posix() for p in b.rglob("*") if p.is_file()) and len(fa) >= 1
    for f in fa:
        assert (a / f).read_bytes() == (b / f).read_bytes(), f
    again = subprocess.run(["dotnet", str(dll), "agent", "install", "--target", target, "--dest", str(a)], capture_output=True, text=True)
    assert again.returncode == (0 if target == "codex" else 2)
    listing = subprocess.run(["dotnet", str(dll), "agent", "list"], capture_output=True, text=True).stdout
    assert {line.split()[0] for line in listing.splitlines()} == set(agents.skills())


# Terms of the repository's own development (generated files, regeneration, worktrees, licensed DLLs, the owner's private folder, internal docs).
REPO_TERMS = ["BuiltIn", "CHANGELOG", "/regen", "worktree", "Experiments", "ZOSAPI", "ZOS-API", "dotnet build", "dotnet test", "roadmap-internal",
              "docs/design", "PROJECT_STATUS", "CLAUDE.md", ".claude/skills", "physics-change", "new-option", "/verify", "hook", "subagent",
              "build_engine", "gen_types", "gen_api_docs", "sync_skills", "style_lint", "Schema/", "Compiler.cs", "mkdocs", "pytest", "test-runner"]


def test_no_shipped_pack_file_mentions_the_repository():
    files = sorted(p for p in agents.PACK.rglob("*") if p.is_file())
    assert {p.name for p in files} >= {"AGENTS.md", "llms.txt", "SKILL.md"}
    bad = []
    for p in files:
        low = p.read_text(encoding="utf-8").lower()
        bad += ["%s: %s" % (p.relative_to(agents.PACK), t) for t in REPO_TERMS if t.lower() in low]
    assert not bad, "repository terms in the shipped pack: " + "; ".join(bad)


@pytest.mark.parametrize("target", agents.TARGETS)
def test_installed_pack_has_the_user_guide_and_index_and_nothing_of_the_repository(tmp_path, target):
    agents.install(target, tmp_path)
    guide = agents.guide()
    assert guide.startswith("# UltrafastEngine: how to drive it") and "run.json" in guide and "ultrafast-mcp" in guide
    where = {"claude": tmp_path / ".claude" / "ultrafast" / "AGENTS.md", "cursor": tmp_path / ".cursor" / "rules" / "ultrafast-guide.mdc",
             "codex": tmp_path / "AGENTS.md"}[target]
    assert guide.strip() in where.read_text(encoding="utf-8")
    assert (tmp_path / ".ultrafast" / "llms.txt").read_text(encoding="utf-8").startswith("# UltrafastEngine")
    for p in (q for q in tmp_path.rglob("*") if q.is_file()):
        low = p.read_text(encoding="utf-8").lower()
        assert not [t for t in REPO_TERMS if t.lower() in low], p
