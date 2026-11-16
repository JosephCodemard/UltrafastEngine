// FP32 emulation back-end "cpu-fp32" (study only, docs/reference/speed.md): the CPU steppers, but every nonlinear operator evaluation sees its
// input and returns its rate rounded to single precision, and the linear rate is single precision too. This is what a mixed-precision GPU
// kernel would compute in its operator (the FFTs and the stepper arithmetic stay double, so it is a lower bound on the FP32 error).
// Main classes: Fp32EmulatedBackend (IComputeBackend), Fp32Problem (wraps an IPropagationProblem). Not for production runs.
using System;
using System.Numerics;
using UltrafastEngine.Physics;

namespace UltrafastEngine.Numerics
{
    public sealed class Fp32EmulatedBackend : IComputeBackend
    {
        public string Name => "cpu-fp32";
        public string Description => "CPU with single-precision nonlinear operator (emulation for the FP32 study; not a speed-up)";
        public bool Supports(IPropagationProblem problem) => true;
        public Complex[] Propagate(IPropagationProblem p, Complex[] a0, StepperSettings st, StepStatistics stats, Action<double> progress)
        {
            stats.Notes.Add("cpu-fp32: nonlinear operator and linear rate rounded to single precision");
            return Steppers.PropagateCpu(p is IBreakpoints ? new Fp32BreakProblem(p) : new Fp32Problem(p), a0, st, stats, progress);
        }
    }

    internal sealed class Fp32BreakProblem : Fp32Problem, IBreakpoints
    {
        private readonly IBreakpoints _b;
        public Fp32BreakProblem(IPropagationProblem p) : base(p) { _b = (IBreakpoints)p; }
        public double NextBreakpoint(double z) => _b.NextBreakpoint(z);
    }

    internal class Fp32Problem : IPropagationProblem
    {
        private readonly IPropagationProblem _p;
        private readonly Complex[] _linear;
        private readonly Complex[] _in;
        public Fp32Problem(IPropagationProblem p)
        {
            _p = p; _linear = Round(p.LinearRate, new Complex[p.LinearRate.Length]); _in = new Complex[p.Length];
        }
        public int Length => _p.Length;
        public double LengthM => _p.LengthM;
        public Complex[] LinearRate => _linear;
        public double AreaAt(double z) => _p.AreaAt(z);
        public double Nonlinear(Complex[] state, double z, Complex[] rateOut)
        {
            Round(state, _in);
            double r = _p.Nonlinear(_in, z, rateOut);
            Round(rateOut, rateOut);
            return r;
        }
        private static Complex[] Round(Complex[] x, Complex[] y)
        {
            for (int i = 0; i < x.Length; i++) y[i] = new Complex((float)x[i].Real, (float)x[i].Imaginary);
            return y;
        }
    }
}
