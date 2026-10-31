// PathLedger.cs -- what one path through a system has accumulated, kept beside the field.
//
// The field is stored in a co-moving frame: each linear term removes its carrier phase and group delay. One path never
// needs them back; two paths that meet do. The ledger keeps those numbers, and the running totals the executor reports.
//
//   class PathLedger
//     double ArrivalS, CumulativeLengthM, CumulativeBIntegralRad    running totals reported per element
//     DispersionPoint System                                        system dispersion since the last carrier change
//     double PhaseRad, FrameDelayS                                  carrier phase and group delay taken out of the field
//     string SourceId; bool PhaseKnown                              where the light came from; false if an element lost the phase
//     BeamState Beam; double TiltX/YRad, OffsetX/YM                 the tracked beam here, and its misalignment, for mode overlap
//     TailLedger Tail                                               delayed light on this path (Branched systems with grid.tail)
//     PathLedger Clone()
//     void Add(DispersionPoint own)                                 add one element's phase and delay
//
// The true field is the stored one times exp(i (PhaseRad + (omega - omega0) FrameDelayS)).
// Design: docs/design/branching.md, section 6.

using UltrafastEngine.Physics;

namespace UltrafastEngine.Engine
{
    /// <summary>Accumulated timing, phase and totals of one path (see the file header).</summary>
    public sealed class PathLedger
    {
        /// <summary>Lab-frame arrival time of the carrier group delay (s): the reported value.</summary>
        public double ArrivalS;
        public double CumulativeLengthM, CumulativeBIntegralRad;
        public DispersionPoint System;
        /// <summary>Carrier phase removed from the field (rad).</summary>
        public double PhaseRad;
        /// <summary>Group delay removed from the field (s). Equal to <see cref="ArrivalS"/> except for terms with narrow
        /// spectral features, whose frame uses their smooth part only.</summary>
        public double FrameDelayS;
        public string SourceId = "input";
        /// <summary>False once an element could not report its carrier phase: fringe positions downstream are then arbitrary.</summary>
        public bool PhaseKnown = true;

        /// <summary>The tracked beam at the end of this path; null if the run has none.</summary>
        public Optics.BeamState Beam;
        /// <summary>Misalignment accumulated by this path against the nominal axis: angles (rad) and offsets (m).</summary>
        public double TiltXRad, TiltYRad, OffsetXM, OffsetYM;
        public TailLedger Tail;

        /// <summary>A copy for a new branch. The tail is copied too, since each branch changes its own.</summary>
        public PathLedger Clone()
        {
            var c = (PathLedger)MemberwiseClone();
            c.Tail = Tail?.Clone();
            return c;
        }

        public void Add(DispersionPoint own)
        {
            PhaseRad += own.FramePhaseRad;
            FrameDelayS += own.FrameDelayS;
            if (own.PhaseUnknown) PhaseKnown = false;
        }
    }
}
