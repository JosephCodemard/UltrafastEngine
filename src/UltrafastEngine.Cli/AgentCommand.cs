// AgentCommand.cs -- `ultrafast agent list|install|mcp-config`: the agent pack (skills that teach a coding agent to drive the engine).
//
// The skills are embedded from python/src/ultrafast/agents/pack (one source; the Python command `ultrafast-agent` reads the same files and writes
// byte-identical output, a test checks it). Main class: AgentCommand.Run(args, stdout, stderr) -> exit code.
//   ultrafast agent list
//   ultrafast agent install --target claude|codex|cursor [--dest DIR] [--force]
//   ultrafast agent mcp-config [--workspace DIR] [--client claude|codex]
// Nothing outside --dest is touched; an existing skill file is overwritten only with --force; for codex the block between the markers is replaced.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace UltrafastEngine.Cli
{
    internal static class AgentCommand
    {
        private const string Footer = "\n\n---\nThe paths `docs/...` in this skill are pages of the documentation site (https://josephcodemard.github.io/UltrafastEngine/) and, in a source checkout, "
            + "files of the repository. The MCP server (`ultrafast-mcp`, `ultrafast-agent mcp-config`) gives the same workflow as tools.\n";
        private const string Begin = "<!-- ultrafast-agent-pack -->", End = "<!-- /ultrafast-agent-pack -->";
        private static readonly string[] Targets = { "claude", "codex", "cursor" };
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public const string Usage = "  ultrafast agent list | install --target claude|codex|cursor [--dest <dir>] [--force] | mcp-config [--workspace <dir>] [--client claude|codex]";

        /// <summary>The shipped skills, name -> SKILL.md text, in name order.</summary>
        public static SortedDictionary<string, string> Skills()
        {
            var res = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var asm = typeof(AgentCommand).Assembly;
            foreach (var n in asm.GetManifestResourceNames())
            {
                string key = n.Replace('\\', '/');
                if (!key.StartsWith("agentpack/", StringComparison.Ordinal) || !key.EndsWith("/SKILL.md", StringComparison.Ordinal)) continue;
                string name = key.Substring("agentpack/".Length, key.Length - "agentpack/".Length - "/SKILL.md".Length);
                using (var r = new StreamReader(asm.GetManifestResourceStream(n), Utf8)) res[name] = r.ReadToEnd().Replace("\r\n", "\n");
            }
            return res;
        }

        private static void Split(string text, out Dictionary<string, string> meta, out string body)
        {
            var m = Regex.Match(text, "^---\n(.*?)\n---\n(.*)$", RegexOptions.Singleline);
            if (!m.Success) throw new InvalidDataException("skill without front matter");
            meta = new Dictionary<string, string>();
            foreach (Match kv in Regex.Matches(m.Groups[1].Value, @"^(\w+):\s*(.*)$", RegexOptions.Multiline)) meta[kv.Groups[1].Value] = kv.Groups[2].Value.TrimEnd('\r');
            body = m.Groups[2].Value.TrimStart('\n');
        }

        private static string Description(string text, string fallback) { Split(text, out var meta, out _); return meta.TryGetValue("description", out var d) ? d : fallback; }

        public static int Run(string[] a, TextWriter o, TextWriter e)
        {
            string sub = a.Length > 0 ? a[0].ToLowerInvariant() : "";
            string Opt(string name, string def) { int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : def; }
            switch (sub)
            {
                case "list":
                    foreach (var kv in Skills()) { string d = Description(kv.Value, ""); o.WriteLine(kv.Key.PadRight(18) + " " + (d.Length > 100 ? d.Substring(0, 100) : d)); }
                    return 0;
                case "mcp-config":
                    o.Write(McpConfig(Opt("--workspace", "."), Opt("--client", "claude")));
                    return 0;
                case "install":
                    string target = Opt("--target", null);
                    if (target == null || !Targets.Contains(target)) { e.WriteLine("usage: ultrafast agent install --target claude|codex|cursor [--dest <dir>] [--force]"); return 1; }
                    try { foreach (var f in Install(target, Opt("--dest", "."), a.Contains("--force"))) o.WriteLine(f); }
                    catch (IOException ex) { e.WriteLine("ultrafast agent: " + ex.Message); return 2; }
                    return 0;
                default:
                    e.WriteLine("usage:\n" + Usage);
                    return 1;
            }
        }

        public static string McpConfig(string workspace, string client)
        {
            string ws = workspace;
            if (client == "codex") return "[mcp_servers.ultrafast]\ncommand = \"ultrafast-mcp\"\nargs = [\"--workspace\", " + Quote(ws) + "]\n";
            return "{\n  \"mcpServers\": {\n    \"ultrafast\": {\n      \"command\": \"ultrafast-mcp\",\n      \"args\": [\n        \"--workspace\",\n        " + Quote(ws) + "\n      ]\n    }\n  }\n}\n";
        }

        private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        public static List<string> Install(string target, string dest, bool force)
        {
            var written = new List<string>();
            var sk = Skills();
            void Write(string path, string text)
            {
                if (File.Exists(path) && !force && target != "codex") throw new IOException(path + " exists (use --force to overwrite)");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllText(path, text, Utf8);
                written.Add(path);
            }
            if (target == "claude")
            {
                foreach (var kv in sk) Write(Path.Combine(dest, ".claude", "skills", kv.Key, "SKILL.md"), kv.Value.TrimEnd('\n') + Footer);
            }
            else if (target == "cursor")
            {
                foreach (var kv in sk)
                {
                    Split(kv.Value, out var meta, out var body);
                    string d = meta.TryGetValue("description", out var dd) ? dd : kv.Key;
                    Write(Path.Combine(dest, ".cursor", "rules", "ultrafast-" + kv.Key + ".mdc"), "---\ndescription: " + d + "\nalwaysApply: false\n---\n" + body.TrimEnd('\n') + Footer);
                }
            }
            else
            {
                var block = new List<string> { Begin, "# UltrafastEngine skills (installed by `ultrafast-agent install --target codex`)", "" };
                foreach (var kv in sk)
                {
                    Split(kv.Value, out var meta, out var body);
                    block.AddRange(new[] { "## Skill: " + kv.Key, "", "_When: " + (meta.TryGetValue("description", out var d) ? d : "") + "_", "", body.TrimEnd('\n'), "" });
                }
                block.Add(Footer.Trim('\n', ' ', '\r', '\t'));
                block.Add(End);
                block.Add("");
                string p = Path.Combine(dest, "AGENTS.md");
                string old = File.Exists(p) ? File.ReadAllText(p, Utf8) : "";
                string joined = string.Join("\n", block);
                string neu;
                if (old.Contains(Begin) && old.Contains(End))
                {
                    int i = old.IndexOf(Begin, StringComparison.Ordinal), j = old.IndexOf(End, StringComparison.Ordinal) + End.Length;
                    neu = old.Substring(0, i) + joined.TrimEnd('\n') + old.Substring(j);
                }
                else neu = (old.Trim().Length > 0 ? old.TrimEnd('\n') + "\n\n" : "") + joined;
                Write(p, neu);
            }
            return written;
        }
    }
}
