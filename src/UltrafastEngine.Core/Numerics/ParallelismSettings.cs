using System;

namespace UltrafastEngine.Numerics
{
    /// <summary>
    /// How much CPU parallelism ComplexFft/Rk4IpSolver are allowed to use. A plain
    /// static holder rather than threading a settings object through every numerics
    /// call -- these are process-wide execution-resource knobs (like GC mode), not
    /// physics parameters, so they don't belong on FrequencyGrid/PulseField. Set once
    /// at startup (see RunConfig.Parallelism / ZemaxConsole.Program.Main) before any
    /// propagation runs; safe to leave at its default (all cores) otherwise.
    /// </summary>
    public static class ParallelismSettings
    {
        private static int _maxDegreeOfParallelism = Environment.ProcessorCount;

        /// <summary>Upper bound on concurrent worker threads for FFTs and the
        /// independent-polarization nonlinear evaluation. Defaults to
        /// Environment.ProcessorCount. Set to 1 to force fully single-threaded
        /// execution (e.g. for reproducible timing comparisons, or if you suspect a
        /// threading-related bug and want to rule it out).</summary>
        public static int MaxDegreeOfParallelism
        {
            get => _maxDegreeOfParallelism;
            set => _maxDegreeOfParallelism = value < 1
                ? throw new ArgumentOutOfRangeException(nameof(value), "MaxDegreeOfParallelism must be >= 1.")
                : value;
        }

        /// <summary>FFTs below this length run single-threaded regardless of
        /// MaxDegreeOfParallelism -- below a few thousand points, Task/thread-pool
        /// dispatch overhead exceeds the work being parallelized, so splitting the
        /// butterfly passes across threads would make small transforms SLOWER, not
        /// faster. Not user-configurable (it's an implementation-overhead threshold,
        /// not a physics or quality setting) -- revisit only if profiling on real
        /// hardware shows it's miscalibrated.</summary>
        public const int MinLengthToParallelize = 4096;
    }
}
