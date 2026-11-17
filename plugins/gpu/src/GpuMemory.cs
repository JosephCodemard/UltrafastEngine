// GpuMemory.cs -- the device-memory decisions of the GPU back-ends as pure functions (testable without a device).
//
//   static class GpuMemory
//     bool Fits(long neededBytes, long freeBytes)                      needed <= 0.8 x free (free < 0: unknown, accepted)
//     int MaxConcurrent(long neededBytes, long totalBytes, int slots)  how many propagations of this size may run at once: 0.8 x total / needed, between 1 and slots
//     string Short(long needed, long free)                              "needs 1.2 GB, 600 MB free"
//     static Func<int, long?> FreeOverride, TotalOverride               tests: a fake device size (index -> bytes)
//
// Reference: docs/plugins/gpu.md, docs/reference/memory.md.

using System;
using System.Globalization;

namespace UltrafastEngine.Plugins.Gpu
{
    public static class GpuMemory
    {
        /// <summary>A problem is accepted when it needs at most this fraction of the free device memory (the rest is for the FFT plans and the driver).</summary>
        public const double Fraction = 0.8;

        public static Func<int, long?> FreeOverride, TotalOverride;

        public static bool Fits(long neededBytes, long freeBytes) => freeBytes < 0 || neededBytes <= Fraction * freeBytes;

        /// <summary>Propagations of <paramref name="neededBytes"/> each that fit on the device together (at most <paramref name="slots"/>, at least 1).</summary>
        public static int MaxConcurrent(long neededBytes, long totalBytes, int slots)
        {
            if (slots < 1) slots = 1;
            if (totalBytes <= 0 || neededBytes <= 0) return slots;
            double k = Fraction * totalBytes / neededBytes;
            return k >= slots ? slots : (int)Math.Max(1, Math.Floor(k));
        }

        public static string Short(long needed, long free) =>
            "needs " + UltrafastEngine.Engine.MemoryEstimate.Human(needed) + ", " + (free < 0 ? "free memory unknown" : UltrafastEngine.Engine.MemoryEstimate.Human(free) + " free");
    }
}
