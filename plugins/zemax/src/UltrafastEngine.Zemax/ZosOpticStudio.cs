// ZosOpticStudio.cs -- IOpticStudio (UltrafastEngine.Zemax.Sampling/IOpticStudio.cs) over ZOS-API: the sampler's view of the
// system open in OpticStudio. The class is split by concern (partial class):
//   ZosOpticStudio.cs     this file: construction from a connected IOpticalSystem, SystemFile
//   ZosSystemAccess.cs    system data and Lens Data Editor: lens units, environment, surfaces, wavelengths, global matrices,
//                         indices, write-back (SetGradient1)
//   ZosAnalyses.cs        computations: batch ray trace (TraceChief), merit-function operands (Operand, EvaluateRows)
// The connection itself (NetHelper initialisation, modes, licence / version checks, shutdown) is ZosConnection.cs.
//
//   new ZosOpticStudio(IOpticalSystem system)          e.g. new ZosOpticStudio(ZosConnection.Connect(options, log).System)
using ZOSAPI;

namespace UltrafastEngine.Zemax
{
    /// <summary>
    /// <see cref="IOpticStudio"/> over ZOS-API. Operands are addressed by NAME (parsed into MeritOperandType at run
    /// time), so an operand missing from an older OpticStudio fails with a clear message instead of breaking the
    /// build. Checked against the ZOS-API assemblies in lib/ (ILDERow.Coating / SemiDiameter,
    /// ISDEnvironmentData.Temperature / Pressure, and every operand the sampler uses).
    /// </summary>
    public sealed partial class ZosOpticStudio : IOpticStudio, ISurfaceEditor
    {
        private readonly IOpticalSystem _s;
        public ZosOpticStudio(IOpticalSystem system) { _s = system ?? throw new System.ArgumentNullException(nameof(system)); }

        public string SystemFile => _s.SystemFile;
    }
}
