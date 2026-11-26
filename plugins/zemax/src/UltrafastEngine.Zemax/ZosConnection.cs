// ZosConnection.cs -- the ZOS-API connection of the Windows binding (net48): implements the core's IZosApi / IZosApplicationHandle
// (UltrafastEngine.Zemax.Sampling/ZosConnectionCore.cs, where the sequence, checks and messages live and are tested) over the real
// ZOSAPI_NetHelper and ZOSAPI assemblies, and hands the program the connected IOpticalSystem.
//
// Main types
//   ZosApiInitializer     static bool Initialize(string dir); static string InstallDirectory   -- touches ONLY ZOSAPI_NetHelper
//   ZosApi : IZosApi      CreateStandalone() / ConnectAsExtension(int) / ConnectToApplication()
//   ZosApplication        IZosApplicationHandle over IZOSAPI_Application; IOpticalSystem PrimarySystem
//   ZosConnection         static ZosConnection Connect(ConnectionOptions o, Action<string> log); IOpticalSystem System; Dispose()
//
// Load order (as in the official ZOS-API C# samples): ZOSAPI.dll and ZOSAPI_Interfaces.dll are not copied next to the exe; they are
// resolved from the OpticStudio installation by ZOSAPI_NetHelper's AssemblyResolve hook, which ZOSAPI_Initializer.Initialize installs.
// The JIT loads the assemblies a method refers to when it compiles that method, so Initialize is called from a method that names no
// ZOSAPI type, and every method that names one is NoInlining (it must not be compiled into a caller before Initialize has run).
// Program.Main initialises first, before it calls a command method whose body names ZOSAPI types (IOpticalSystem, ...).
using System;
using System.Runtime.CompilerServices;
using ZOSAPI;
using ZOSAPI_NetHelper;

namespace UltrafastEngine.Zemax
{
    /// <summary>ZOSAPI_NetHelper only (copied next to the exe): safe to call before OpticStudio's assemblies can be resolved.</summary>
    internal static class ZosApiInitializer
    {
        private static bool? _result;
        private static Exception _error;

        /// <summary>Runs ZOSAPI_Initializer.Initialize once and caches the outcome. Program.Main calls it before any method that
        /// names a ZOSAPI type is compiled; ZosSession.Open calls it again (cached) and turns a failure into the user message.</summary>
        public static bool Initialize(string opticStudioDirectory)
        {
            if (_result == null)
            {
                try { _result = string.IsNullOrEmpty(opticStudioDirectory) ? ZOSAPI_Initializer.Initialize() : ZOSAPI_Initializer.Initialize(opticStudioDirectory); }
                catch (Exception ex) { _result = false; _error = ex; }
            }
            if (_error != null) throw new InvalidOperationException(_error.Message, _error);
            return _result.Value;
        }

        public static string InstallDirectory => ZOSAPI_Initializer.GetZemaxDirectory();
    }

    internal sealed class ZosApi : IZosApi
    {
        private object _connection; // ZOSAPI_Connection; typed object so this class's layout needs no ZOSAPI type

        public bool Initialize(string opticStudioDirectory) => ZosApiInitializer.Initialize(opticStudioDirectory);
        public string InstallDirectory => ZosApiInitializer.InstallDirectory;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private ZOSAPI_Connection Connection() => (ZOSAPI_Connection)(_connection ?? (_connection = new ZOSAPI_Connection()));

        [MethodImpl(MethodImplOptions.NoInlining)]
        public IZosApplicationHandle CreateStandalone() => ZosApplication.Wrap(Connection().CreateNewApplication());

        // TODO(zemax-hw): ConnectAsExtension returns null (rather than throwing) when no OpticStudio waits in Interactive Extension mode.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public IZosApplicationHandle ConnectAsExtension(int instance) => ZosApplication.Wrap(Connection().ConnectAsExtension(instance));

        // TODO(zemax-hw): ConnectToApplication (the User Extension template's call) works when OpticStudio starts this exe.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public IZosApplicationHandle ConnectToApplication() => ZosApplication.Wrap(Connection().ConnectToApplication());
    }

    internal sealed class ZosApplication : IZosApplicationHandle
    {
        public IZOSAPI_Application App { get; }
        private ZosApplication(IZOSAPI_Application app) { App = app; }
        public static ZosApplication Wrap(IZOSAPI_Application app) => app == null ? null : new ZosApplication(app);

        public bool IsValidLicenseForApi => App.IsValidLicenseForAPI;
        // TODO(zemax-hw): LicenseStatus (LicenseStatusType) and ZOSMajorVersion / ZOSMinorVersion / ZOSSPVersion: names from the ZOS-API
        // documentation and ZOSPy; 2024 R1 is expected to report 24.1.x.
        public string LicenseStatus => App.LicenseStatus.ToString();
        public ZosVersion Version => new ZosVersion(App.ZOSMajorVersion, App.ZOSMinorVersion, App.ZOSSPVersion);
        public bool HasPrimarySystem => App.PrimarySystem != null;
        public IOpticalSystem PrimarySystem => App.PrimarySystem;
        public bool LoadFile(string path) => App.PrimarySystem.LoadFile(path, false);
        public void Close() => App.CloseApplication();
    }

    /// <summary>A checked ZOS-API connection and its primary system. Dispose closes a standalone instance only; Ctrl+C does the same
    /// (no orphan OpticStudio process when an export is interrupted).</summary>
    public sealed class ZosConnection : IDisposable
    {
        private readonly ZosSession _session;
        private readonly ConsoleCancelEventHandler _onCancel;
        public IOpticalSystem System { get; private set; }
        public ZosSession Session => _session;

        private ZosConnection(ZosSession session)
        {
            _session = session;
            System = ((ZosApplication)session.Application).PrimarySystem;
            if (session.Options.ClosesApplicationOnExit)
            {
                _onCancel = (s, e) => { Console.Error.WriteLine("interrupted: closing the standalone OpticStudio instance"); _session.Dispose(); };
                Console.CancelKeyPress += _onCancel;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ZosConnection Connect(ConnectionOptions options, Action<string> log) => new ZosConnection(ZosSession.Open(new ZosApi(), options, log));

        public void Dispose()
        {
            if (_onCancel != null) Console.CancelKeyPress -= _onCancel;
            System = null;
            _session.Dispose();
        }
    }
}
