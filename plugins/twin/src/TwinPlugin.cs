// TwinPlugin.cs -- the digital-twin plugin (plugins/twin): registers `ultrafast twin ...`.
//
//   TwinPlugin       : IPlugin      registers the command.
//   TwinCommand      : ICliCommand  `ultrafast twin <init|fit|validate|predict|design|track|serve|check> ...` -- starts
//                                   `python -m ultrafast_twin <args>` (interpreter: ULTRAFAST_PYTHON, else python3 / python)
//                                   with the plugin's python/ folder on PYTHONPATH, and returns its exit code.
//
// The twin is Python because it orchestrates engine runs and uses NumPy; the fit itself is the fitting plugin (C#).

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UltrafastEngine.Plugins;

namespace UltrafastEngine.Plugins.Twin
{
    public sealed class TwinPlugin : IPlugin
    {
        public const string PluginVersion = "2.2.0";
        public string Id => "twin";
        public string Name => "Digital twin: projects, validation, experiment design, drift tracking, agent server";
        public string Version => PluginVersion;
        public string Description => "The `ultrafast twin` command; the work is done by the ultrafast_twin Python module.";

        public void Register(PluginContext ctx) => ctx.RegisterCommand(new TwinCommand());
    }

    public sealed class TwinCommand : ICliCommand
    {
        public string Name => "twin";
        public string Usage => "ultrafast twin <init|fit|validate|predict|design|track|check|serve> [options]   (digital twin of a lab; `ultrafast twin --help`)";

        /// <summary>The python/ folder of the plugin (…/twin/lib/net10.0/x.dll → …/twin/python).</summary>
        internal static string PythonFolder()
        {
            var dll = Path.GetDirectoryName(typeof(TwinCommand).Assembly.Location) ?? ".";
            return Path.GetFullPath(Path.Combine(dll, "..", "..", "python"));
        }

        public int Run(string[] args, TextWriter output, TextWriter error)
        {
            string py = Environment.GetEnvironmentVariable("ULTRAFAST_PYTHON");
            if (string.IsNullOrEmpty(py)) py = Environment.OSVersion.Platform == PlatformID.Win32NT ? "python" : "python3";
            var psi = new ProcessStartInfo { FileName = py, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
#if NET
            psi.ArgumentList.Add("-m"); psi.ArgumentList.Add("ultrafast_twin");
            foreach (var a in args) psi.ArgumentList.Add(a);
#else
            psi.Arguments = "-m ultrafast_twin " + string.Join(" ", Array.ConvertAll(args, a => "\"" + a.Replace("\"", "\\\"") + "\""));
#endif
            string folder = PythonFolder();
            string old = Environment.GetEnvironmentVariable("PYTHONPATH");
            psi.EnvironmentVariables["PYTHONPATH"] = string.IsNullOrEmpty(old) ? folder : folder + Path.PathSeparator + old;
            try
            {
                using (var p = Process.Start(psi))
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) output.WriteLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) error.WriteLine(e.Data); };
                    p.BeginOutputReadLine(); p.BeginErrorReadLine();
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                error.WriteLine("ultrafast twin: could not start Python (" + py + "): " + ex.Message + ". Install Python 3.10+ with NumPy and the ultrafast package, or set ULTRAFAST_PYTHON.");
                return 2;
            }
        }
    }
}
