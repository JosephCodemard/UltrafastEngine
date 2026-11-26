// ZosConnectionCore.cs -- the OS-independent half of the ZOS-API connection: which connection mode to use, the order of the
// connection steps (ZOSAPI_NetHelper initialisation, connect, licence check, version check, primary system, optional file load),
// the error messages, and clean shutdown. The Windows binding (UltrafastEngine.Zemax, ZosConnection.cs) implements the two small
// interfaces below over the real ZOS-API; the tests implement them with a fake, so everything here runs on any OS.
//
// Main types
//   enum ConnectionMode { Interactive, Extension, Standalone }
//   ConnectionOptions        ConnectionOptions.FromArgs(IList<string> args)   -- parses the CLI connection flags, rejects conflicts
//                            bool ClosesApplicationOnExit                     -- only a standalone instance is ours to close
//   ZosVersion               ZosVersion.Minimum; CompareTo; ToString() "24.1.0"
//   IZosApi                  bool Initialize(string dir); string InstallDirectory; CreateStandalone(); ConnectAsExtension(int); ConnectToApplication()
//   IZosApplicationHandle    IsValidLicenseForApi; LicenseStatus; Version; HasPrimarySystem; LoadFile(path); Close()
//   ZosConnectionException   Failure (ZosConnectionFailure) + a message that says what to do
//   ZosSession               ZosSession.Open(IZosApi api, ConnectionOptions o, Action<string> log); Dispose()
//
// Design notes. The connection sequence follows the official ZOS-API C# samples (Ansys) and, for the licence and version reads and
// the standalone/extension distinction, ZOSPy (https://github.com/MREYE-LUMC/ZOSPy, MIT licence), which was read as a guide; no ZOSPy code is shipped or required (decision E19). One deliberate difference: ZOSPy calls
// CloseApplication in extension mode too (documented there as closing only the connection); we never call it on an OpticStudio
// we did not start, so a wrong assumption cannot close the user's instance with unsaved work (TODO(zemax-hw): checklist step 3b).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace UltrafastEngine.Zemax
{
    /// <summary>How the program reaches OpticStudio.</summary>
    public enum ConnectionMode
    {
        /// <summary>Attach to a running OpticStudio waiting in Programming &gt; Interactive Extension (ZOSAPI_Connection.ConnectAsExtension(instance)). Default.</summary>
        Interactive,
        /// <summary>This program was started BY OpticStudio as a User Extension (Programming &gt; User Extensions; ZOSAPI_Connection.ConnectToApplication()).</summary>
        Extension,
        /// <summary>Start a private, invisible OpticStudio instance (ZOSAPI_Connection.CreateNewApplication()); closed on exit.</summary>
        Standalone
    }

    /// <summary>Why a connection failed (the message says what to do).</summary>
    public enum ZosConnectionFailure { InvalidArguments, NotInstalled, NoApplication, Licence, Version, NoSystem, LoadFailed }

    public sealed class ZosConnectionException : InvalidOperationException
    {
        public ZosConnectionFailure Failure { get; }
        public ZosConnectionException(ZosConnectionFailure failure, string message, Exception inner = null) : base(message, inner) { Failure = failure; }
    }

    /// <summary>ZOS-API version as reported by IZOSAPI_Application (ZOSMajorVersion.ZOSMinorVersion.ZOSSPVersion; e.g. 24.1.0 = OpticStudio 2024 R1).</summary>
    public readonly struct ZosVersion : IComparable<ZosVersion>
    {
        public int Major { get; }
        public int Minor { get; }
        public int ServicePack { get; }
        public ZosVersion(int major, int minor, int servicePack) { Major = major; Minor = minor; ServicePack = servicePack; }

        /// <summary>Oldest OpticStudio the binding accepts: 20.1 (every operand and ZOS-API member it uses exists there per the
        /// ZOS-API documentation; TODO(zemax-hw): not run on any version yet). Older versions are refused unless --skip-version-check.</summary>
        public static ZosVersion Minimum => new ZosVersion(20, 1, 0);

        public int CompareTo(ZosVersion o) => Major != o.Major ? Major.CompareTo(o.Major) : Minor != o.Minor ? Minor.CompareTo(o.Minor) : ServicePack.CompareTo(o.ServicePack);
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", Major, Minor, ServicePack);
    }

    /// <summary>What the user asked for on the command line.</summary>
    public sealed class ConnectionOptions
    {
        public ConnectionMode Mode { get; set; } = ConnectionMode.Interactive;
        /// <summary>Interactive Extension instance number (0 = the first OpticStudio waiting for a connection).</summary>
        public int Instance { get; set; }
        /// <summary>OpticStudio installation folder; null = let ZOSAPI_NetHelper find it (registry).</summary>
        public string OpticStudioDirectory { get; set; }
        /// <summary>Lens file to load (standalone only: loading into the user's OpticStudio would replace their open system).</summary>
        public string FileToLoad { get; set; }
        public bool SkipVersionCheck { get; set; }
        /// <summary>Non-fatal remarks on the flags.</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>Only an instance we started is ours to close; the user's OpticStudio (interactive or user extension) stays open.</summary>
        public bool ClosesApplicationOnExit => Mode == ConnectionMode.Standalone;

        /// <summary>Flags that take a value (for the caller's positional-argument parser).</summary>
        public static readonly string[] ValuedFlags = { "--instance", "--opticstudio", "--file" };

        public string Describe()
        {
            switch (Mode)
            {
                case ConnectionMode.Standalone: return "standalone (private OpticStudio instance, closed on exit)";
                case ConnectionMode.Extension: return "User Extension (started by OpticStudio)";
                default: return "Interactive Extension (instance " + Instance.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        /// <summary>Parse --standalone | --interactive | --extension, --instance n, --opticstudio dir, --file lens.zos,
        /// --skip-version-check. Other arguments are ignored. Throws <see cref="ZosConnectionException"/> (InvalidArguments).</summary>
        public static ConnectionOptions FromArgs(IList<string> args)
        {
            var o = new ConnectionOptions();
            var modes = new List<ConnectionMode>();
            string instance = null;
            for (int i = 0; i < args.Count; i++)
            {
                string a = args[i];
                string Value()
                {
                    if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal)) throw Bad(a + " needs a value.");
                    return args[++i];
                }
                switch (a)
                {
                    case "--standalone": modes.Add(ConnectionMode.Standalone); break;
                    case "--interactive": modes.Add(ConnectionMode.Interactive); break;
                    case "--extension": modes.Add(ConnectionMode.Extension); break;
                    case "--instance": instance = Value(); break;
                    case "--opticstudio": o.OpticStudioDirectory = Value(); break;
                    case "--file": o.FileToLoad = Value(); break;
                    case "--skip-version-check": o.SkipVersionCheck = true; break;
                }
            }
            var distinct = new HashSet<ConnectionMode>(modes);
            if (distinct.Count > 1) throw Bad("Choose one connection mode: --standalone, --interactive or --extension.");
            if (distinct.Count == 1) foreach (var m in distinct) o.Mode = m;
            else if (o.FileToLoad != null) o.Mode = ConnectionMode.Standalone; // --file alone has always meant a private instance
            if (o.FileToLoad != null && o.Mode != ConnectionMode.Standalone)
                throw Bad("--file loads a lens file and is only allowed with --standalone: in " + (o.Mode == ConnectionMode.Interactive ? "Interactive Extension" : "User Extension")
                    + " mode it would replace the system open in your OpticStudio. Open the file in OpticStudio instead.");
            if (instance != null)
            {
                if (!int.TryParse(instance, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 0)
                    throw Bad("--instance must be a non-negative integer (the Interactive Extension instance number), not '" + instance + "'.");
                if (o.Mode != ConnectionMode.Interactive) o.Warnings.Add("--instance is only used in Interactive Extension mode; ignored.");
                else o.Instance = n;
            }
            return o;
        }

        private static ZosConnectionException Bad(string m) => new ZosConnectionException(ZosConnectionFailure.InvalidArguments, m);
    }

    /// <summary>The ZOS-API entry points the connection uses (ZOSAPI_NetHelper.ZOSAPI_Initializer and ZOSAPI.ZOSAPI_Connection).</summary>
    public interface IZosApi
    {
        /// <summary>ZOSAPI_Initializer.Initialize() / Initialize(dir): find OpticStudio and hook the resolution of ZOSAPI.dll and
        /// ZOSAPI_Interfaces.dll. Must run before any ZOSAPI type is touched.</summary>
        bool Initialize(string opticStudioDirectory);
        /// <summary>ZOSAPI_Initializer.GetZemaxDirectory() after a successful Initialize.</summary>
        string InstallDirectory { get; }
        IZosApplicationHandle CreateStandalone();
        IZosApplicationHandle ConnectAsExtension(int instance);
        IZosApplicationHandle ConnectToApplication();
    }

    /// <summary>The part of IZOSAPI_Application the connection checks.</summary>
    public interface IZosApplicationHandle
    {
        bool IsValidLicenseForApi { get; }
        /// <summary>LicenseStatus as text (e.g. PremiumEdition, StandardEdition, KeyNotWorking).</summary>
        string LicenseStatus { get; }
        ZosVersion Version { get; }
        bool HasPrimarySystem { get; }
        bool LoadFile(string path);
        /// <summary>IZOSAPI_Application.CloseApplication().</summary>
        void Close();
    }

    /// <summary>An open, checked connection. Dispose closes a standalone instance and leaves the user's OpticStudio alone.</summary>
    public sealed class ZosSession : IDisposable
    {
        private readonly Action<string> _log;
        private int _disposed;
        public IZosApplicationHandle Application { get; private set; }
        public ConnectionOptions Options { get; }
        public ZosVersion Version { get; }
        public string InstallDirectory { get; }

        private ZosSession(IZosApplicationHandle app, ConnectionOptions o, ZosVersion v, string dir, Action<string> log)
        { Application = app; Options = o; Version = v; InstallDirectory = dir; _log = log; }

        /// <summary>Message when ZOSAPI_NetHelper cannot find OpticStudio.</summary>
        public static string NotInstalledMessage(string dir) => string.IsNullOrEmpty(dir)
            ? "ZOSAPI_NetHelper could not locate OpticStudio (it reads the installation folder from the registry). Pass --opticstudio <install dir>, e.g. --opticstudio \"C:\\Program Files\\Ansys Zemax OpticStudio 2025 R1\"."
            : "ZOSAPI_NetHelper found no OpticStudio in '" + dir + "' (the folder must contain ZOSAPI.dll and ZOSAPI_Interfaces.dll). Check --opticstudio.";

        public static string NoApplicationMessage(ConnectionOptions o)
        {
            switch (o.Mode)
            {
                case ConnectionMode.Standalone: return "Could not start a standalone OpticStudio instance. Check that OpticStudio starts by itself, that its licence is available (a network licence may be in use elsewhere), and that no dialog is waiting in another OpticStudio.";
                case ConnectionMode.Extension: return "Could not connect as a User Extension: this mode only works when OpticStudio itself starts the program (Programming > User Extensions). From a terminal use the default Interactive Extension mode, or --standalone.";
                default: return "Could not attach to OpticStudio (Interactive Extension, instance " + o.Instance.ToString(CultureInfo.InvariantCulture) + "). Open your system in OpticStudio, click Programming > Interactive Extension so it shows 'Waiting for connection', then run this program again" + (o.Instance == 0 ? "." : "; with several OpticStudio windows check --instance.");
            }
        }

        public static string LicenceMessage(string status) =>
            "This OpticStudio licence does not allow ZOS-API (licence status: " + (string.IsNullOrEmpty(status) ? "unknown" : status) + "). OpticStudio Professional or Premium (or Enterprise) is required; the Standard edition has no ZOS-API.";

        public static string VersionMessage(ZosVersion v) =>
            "OpticStudio " + v + " is older than the oldest version the binding supports (" + ZosVersion.Minimum + "). Update OpticStudio, or pass --skip-version-check to try anyway (operands may be missing).";

        /// <summary>Initialise, connect, check licence, version and primary system, load the file (standalone). On any failure after a
        /// standalone instance was started, that instance is closed before the exception propagates (no orphan OpticStudio process).</summary>
        public static ZosSession Open(IZosApi api, ConnectionOptions o, Action<string> log = null)
        {
            if (api == null) throw new ArgumentNullException(nameof(api));
            if (o == null) throw new ArgumentNullException(nameof(o));
            log = log ?? (_ => { });
            foreach (var w in o.Warnings) log("WARNING: " + w);

            bool found;
            try { found = api.Initialize(o.OpticStudioDirectory); }
            catch (Exception ex) { throw new ZosConnectionException(ZosConnectionFailure.NotInstalled, NotInstalledMessage(o.OpticStudioDirectory) + " (" + ex.Message + ")", ex); }
            if (!found) throw new ZosConnectionException(ZosConnectionFailure.NotInstalled, NotInstalledMessage(o.OpticStudioDirectory));
            string dir = null;
            try { dir = api.InstallDirectory; } catch { }
            if (!string.IsNullOrEmpty(dir)) log("OpticStudio found at " + dir);

            IZosApplicationHandle app;
            try
            {
                app = o.Mode == ConnectionMode.Standalone ? api.CreateStandalone()
                    : o.Mode == ConnectionMode.Extension ? api.ConnectToApplication()
                    : api.ConnectAsExtension(o.Instance);
            }
            catch (Exception ex) { throw new ZosConnectionException(ZosConnectionFailure.NoApplication, NoApplicationMessage(o) + " (" + ex.Message + ")", ex); }
            if (app == null) throw new ZosConnectionException(ZosConnectionFailure.NoApplication, NoApplicationMessage(o));

            try
            {
                if (!app.IsValidLicenseForApi)
                {
                    string status = null;
                    try { status = app.LicenseStatus; } catch { }
                    throw new ZosConnectionException(ZosConnectionFailure.Licence, LicenceMessage(status));
                }
                var v = app.Version;
                if (v.CompareTo(ZosVersion.Minimum) < 0)
                {
                    if (!o.SkipVersionCheck) throw new ZosConnectionException(ZosConnectionFailure.Version, VersionMessage(v));
                    log("WARNING: OpticStudio " + v + " is older than " + ZosVersion.Minimum + "; continuing because of --skip-version-check.");
                }
                if (!app.HasPrimarySystem) throw new ZosConnectionException(ZosConnectionFailure.NoSystem, "OpticStudio has no primary optical system open.");
                if (o.FileToLoad != null && !app.LoadFile(o.FileToLoad))
                    throw new ZosConnectionException(ZosConnectionFailure.LoadFailed, "OpticStudio could not load '" + o.FileToLoad + "' (check the path and that the file opens in OpticStudio).");
                log("Connected to OpticStudio " + v + ", " + o.Describe());
                return new ZosSession(app, o, v, dir, log);
            }
            catch
            {
                if (o.ClosesApplicationOnExit) CloseQuietly(app, log);
                throw;
            }
        }

        private static void CloseQuietly(IZosApplicationHandle app, Action<string> log)
        {
            try { app.Close(); }
            catch (Exception ex) { log("WARNING: could not close the standalone OpticStudio instance (" + ex.Message + "); end OpticStudio.exe in the Task Manager if it is still running."); }
        }

        /// <summary>Close a standalone instance; for the user's OpticStudio only drop our reference. Safe to call twice
        /// (from Ctrl+C and from a using block).</summary>
        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;
            var app = Application;
            Application = null;
            if (app != null && Options.ClosesApplicationOnExit) CloseQuietly(app, _log);
        }
    }
}
