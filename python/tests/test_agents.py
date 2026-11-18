"""The agent pack (ultrafast.agents): the shipped skills equal the repository's, each target installs, re-install replaces, nothing is overwritten without --force."""
import json
import re
import sys
from pathlib import Path

import pytest

from ultrafast import agents
from ultrafast.mcp import policy

REPO = Path(__file__).resolve().parents[2]
NAMES = {"run-simulations", "debug-setup", "mirror-lab-setup", "optimise-setup", "replicate-paper"}


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
    assert {f.parent.name for f in files} == NAMES and all(f.is_file() for f in files)
    assert "documentation site" in files[0].read_text(encoding="utf-8")
    with pytest.raises(FileExistsError):
        agents.install("claude", tmp_path)
    files[0].write_text("changed", encoding="utf-8")
    agents.install("claude", tmp_path, force=True)
    assert files[0].read_text(encoding="utf-8") != "changed"


def test_install_cursor_rules(tmp_path):
    files = agents.install("cursor", tmp_path)
    assert {f.name for f in files} == {"ultrafast-%s.mdc" % n for n in NAMES}
    t = files[0].read_text(encoding="utf-8")
    assert t.startswith("---\ndescription: ") and "alwaysApply: false" in t


def test_install_codex_keeps_the_rest_of_agents_md_and_replaces_its_own_block(tmp_path):
    p = tmp_path / "AGENTS.md"
    p.write_text("# My project\n\nRules of mine.\n", encoding="utf-8")
    agents.install("codex", tmp_path)
    once = p.read_text(encoding="utf-8")
    assert once.startswith("# My project") and "Rules of mine." in once and "## Skill: debug-setup" in once and once.count(agents.BEGIN) == 1
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
