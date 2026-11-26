// ZemaxConnectionTests.cs -- the OS-independent ZOS-API connection core (plugins/zemax/src/UltrafastEngine.Zemax.Sampling/
// ZosConnectionCore.cs) driven through a fake IZosApi: connection-mode selection from the CLI flags, the order of the steps,
// licence / version / no-system / load failures with their messages, and that only a standalone instance is ever closed.
// The real ZOS-API calls behind the interfaces (ZosConnection.cs) need OpticStudio: plugins/zemax/HARDWARE-CHECKLIST.md step 3.
using System;
using System.Collections.Generic;
using UltrafastEngine.Zemax;

namespace UltrafastEngine.Tests
{
    [TestArea("Zemax plugin: ZOS-API connection")]
    public class ZemaxConnectionTests
    {
        private sealed class FakeApp : IZosApplicationHandle
        {
            public bool Licence = true, HasSystem = true, LoadOk = true;
            public string Status = "PremiumEdition";
            public ZosVersion Ver = new ZosVersion(24, 1, 0);
            public int Closed;
            public string Loaded;
            public bool IsValidLicenseForApi => Licence;
            public string LicenseStatus => Status;
            public ZosVersion Version => Ver;
            public bool HasPrimarySystem => HasSystem;
            public bool LoadFile(string path) { Loaded = path; return LoadOk; }
            public void Close() => Closed++;
        }

        private sealed class FakeApi : IZosApi
        {
            public bool Found = true;
            public FakeApp App = new FakeApp();
            public List<string> Calls = new List<string>();
            public bool Initialize(string dir) { Calls.Add("init:" + (dir ?? "")); return Found; }
            public string InstallDirectory => "C:\\OpticStudio";
            public IZosApplicationHandle CreateStandalone() { Calls.Add("standalone"); return App; }
            public IZosApplicationHandle ConnectAsExtension(int instance) { Calls.Add("interactive:" + instance); return App; }
            public IZosApplicationHandle ConnectToApplication() { Calls.Add("extension"); return App; }
        }

        private static ConnectionOptions Opts(params string[] a) => ConnectionOptions.FromArgs(a);

        private static ZosConnectionException Fails(Action a)
        {
            try { a(); }
            catch (ZosConnectionException ex) { return ex; }
            throw new Exception("expected a ZosConnectionException");
        }

        [Test]
        public void ModeSelection_DefaultsToInteractiveAndFileAloneMeansStandalone()
        {
            Assert.True(Opts("export", "x.json").Mode == ConnectionMode.Interactive, "default is Interactive Extension");
            Assert.True(Opts("--standalone").Mode == ConnectionMode.Standalone, "--standalone");
            Assert.True(Opts("--extension").Mode == ConnectionMode.Extension, "--extension");
            var f = Opts("info", "--file", "lens.zos");
            Assert.True(f.Mode == ConnectionMode.Standalone && f.FileToLoad == "lens.zos", "--file alone keeps its old meaning (private instance)");
            var i = Opts("--instance", "2", "--opticstudio", "D:\\OS");
            Assert.Equal(2, i.Instance, "instance number");
            Assert.True(i.OpticStudioDirectory == "D:\\OS", "install folder");
            Assert.True(Opts("--standalone", "--standalone").Mode == ConnectionMode.Standalone, "a repeated flag is not a conflict");
        }

        [Test]
        public void ModeSelection_RejectsConflictsWithMessagesThatSayWhatToDo()
        {
            var both = Fails(() => Opts("--standalone", "--interactive"));
            Assert.True(both.Failure == ZosConnectionFailure.InvalidArguments && both.Message.Contains("Choose one"), both.Message);
            var file = Fails(() => Opts("--interactive", "--file", "a.zos"));
            Assert.True(file.Message.Contains("--standalone") && file.Message.Contains("replace"), "--file into the user's OpticStudio is refused: " + file.Message);
            Assert.True(Fails(() => Opts("--instance", "-1")).Message.Contains("non-negative"), "negative instance");
            Assert.True(Fails(() => Opts("--instance", "two")).Message.Contains("'two'"), "non-integer instance");
            Assert.True(Fails(() => Opts("--opticstudio")).Message.Contains("needs a value"), "missing value");
            var w = Opts("--standalone", "--instance", "1");
            Assert.True(w.Warnings.Count == 1 && w.Instance == 0, "--instance outside interactive mode is a warning, ignored");
        }

        [Test]
        public void Open_EachModeCallsItsZosApiEntryPointAfterInitialisation()
        {
            foreach (var (args, call) in new[] { (new[] { "--interactive", "--instance", "3" }, "interactive:3"), (new[] { "--extension" }, "extension"), (new[] { "--standalone" }, "standalone") })
            {
                var api = new FakeApi();
                var log = new List<string>();
                using (var s = ZosSession.Open(api, Opts(args), log.Add))
                {
                    Assert.Equal(2, api.Calls.Count, "init then connect");
                    Assert.True(api.Calls[0] == "init:" && api.Calls[1] == call, string.Join(",", api.Calls));
                    Assert.True(s.Version.ToString() == "24.1.0", "version read");
                    Assert.True(log.Exists(l => l.Contains("Connected to OpticStudio 24.1.0")), "connection logged");
                }
            }
        }

        [Test]
        public void Shutdown_ClosesOnlyAStandaloneInstanceAndOnlyOnce()
        {
            var api = new FakeApi();
            var s = ZosSession.Open(api, Opts("--standalone"));
            s.Dispose(); s.Dispose();
            Assert.Equal(1, api.App.Closed, "standalone closed exactly once");
            foreach (var mode in new[] { "--interactive", "--extension" })
            {
                var a = new FakeApi();
                ZosSession.Open(a, Opts(mode)).Dispose();
                Assert.Equal(0, a.App.Closed, "the user's OpticStudio is never closed (" + mode + ")");
            }
        }

        [Test]
        public void Open_ReportsLicenceVersionAndMissingInstallationClearly()
        {
            var noOs = new FakeApi { Found = false };
            var e1 = Fails(() => ZosSession.Open(noOs, Opts()));
            Assert.True(e1.Failure == ZosConnectionFailure.NotInstalled && e1.Message.Contains("--opticstudio"), e1.Message);
            Assert.Equal(1, noOs.Calls.Count, "no connection attempt without an installation");

            var lic = new FakeApi(); lic.App.Licence = false; lic.App.Status = "StandardEdition";
            var e2 = Fails(() => ZosSession.Open(lic, Opts()));
            Assert.True(e2.Failure == ZosConnectionFailure.Licence && e2.Message.Contains("StandardEdition") && e2.Message.Contains("Professional"), e2.Message);

            var old = new FakeApi(); old.App.Ver = new ZosVersion(19, 8, 0);
            var e3 = Fails(() => ZosSession.Open(old, Opts()));
            Assert.True(e3.Failure == ZosConnectionFailure.Version && e3.Message.Contains("19.8.0") && e3.Message.Contains("20.1.0"), e3.Message);
            var log = new List<string>();
            ZosSession.Open(old, Opts("--skip-version-check"), log.Add).Dispose();
            Assert.True(log.Exists(l => l.StartsWith("WARNING") && l.Contains("19.8.0")), "--skip-version-check connects with a warning");
            Assert.True(new ZosVersion(20, 1, 0).CompareTo(ZosVersion.Minimum) == 0 && new ZosVersion(21, 0, 0).CompareTo(new ZosVersion(20, 9, 9)) > 0, "version order");
        }

        [Test]
        public void Open_ModeSpecificMessageWhenNothingAnswers()
        {
            var api = new FakeApi { App = null };
            Assert.True(Fails(() => ZosSession.Open(api, Opts())).Message.Contains("Programming > Interactive Extension"), "interactive hint");
            Assert.True(Fails(() => ZosSession.Open(api, Opts("--extension"))).Message.Contains("User Extensions"), "extension hint");
            var e = Fails(() => ZosSession.Open(api, Opts("--standalone")));
            Assert.True(e.Failure == ZosConnectionFailure.NoApplication && e.Message.Contains("standalone"), e.Message);
        }

        [Test]
        public void Open_AFailedCheckClosesTheStandaloneInstanceItStarted()
        {
            var lic = new FakeApi(); lic.App.Licence = false;
            Fails(() => ZosSession.Open(lic, Opts("--standalone")));
            Assert.Equal(1, lic.App.Closed, "no orphan OpticStudio after a licence failure");
            var load = new FakeApi(); load.App.LoadOk = false;
            var e = Fails(() => ZosSession.Open(load, Opts("--file", "C:\\lens.zos")));
            Assert.True(e.Failure == ZosConnectionFailure.LoadFailed && e.Message.Contains("lens.zos"), e.Message);
            Assert.Equal(1, load.App.Closed, "closed after a failed load");
            var userOs = new FakeApi(); userOs.App.HasSystem = false;
            Assert.True(Fails(() => ZosSession.Open(userOs, Opts())).Failure == ZosConnectionFailure.NoSystem, "no primary system");
            Assert.Equal(0, userOs.App.Closed, "the user's OpticStudio stays open after a failed check");
        }
    }
}
