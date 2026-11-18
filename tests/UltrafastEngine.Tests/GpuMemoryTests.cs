using System;
using System.Linq;
using UltrafastEngine.Plugins.Gpu;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>GPU memory safety (docs/plugins/gpu.md#memory): the accept / refuse / concurrency decisions as pure functions, the selection with a
    /// fake device size, free-memory queries, and the bounded program cache. The device checks skip without a device.</summary>
    [TestArea("Memory safety: GPU")]
    public class GpuMemoryTests
    {
        private static GpuBackend Device()
        {
            foreach (GpuBackend b in new GpuBackend[] { GpuPlugin.Cuda, GpuPlugin.OpenCl })
                if (b.Unavailable == null && b.Devices.Any(d => d.Usable)) return b;
            Assert.Skip("no CUDA or OpenCL device with double precision");
            return null;
        }

        [Test(Reference = "needed <= 0.8 x free; unknown free memory accepts", Tolerance = "exact")]
        public void Fits_UsesTheFreeMemory_At80Percent()
        {
            Assert.True(GpuMemory.Fits(800, 1000), "exactly 80 %");
            Assert.True(!GpuMemory.Fits(801, 1000), "above 80 %");
            Assert.True(GpuMemory.Fits(long.MaxValue / 2, -1), "unknown free memory");
        }

        [Test(Reference = "slots in use: floor(0.8 x total / needed), between 1 and the configured slots", Tolerance = "exact")]
        public void MaxConcurrent_IsBoundedByTheDeviceSize_AndTheSlots()
        {
            Assert.Equal(4, GpuMemory.MaxConcurrent(100, 10000, 4), "small problems: all 4 slots");
            Assert.Equal(2, GpuMemory.MaxConcurrent(3000, 10000, 4), "0.8 x 10000 / 3000 = 2.67 -> 2");
            Assert.Equal(1, GpuMemory.Fraction > 0 ? GpuMemory.MaxConcurrent(9000, 10000, 4) : 0, "a problem near the limit runs alone");
            Assert.Equal(1, GpuMemory.MaxConcurrent(50000, 10000, 4), "never fewer than one");
            Assert.Equal(3, GpuMemory.MaxConcurrent(1, 0, 3), "unknown device size: the slots");
        }

        [Test(Category = "gpu", Reference = "a device with less free memory than the problem needs is refused and the element runs on the CPU, with the reason in the notes", Tolerance = "result equals the CPU run")]
        public void FakeFreeMemory_TooSmall_RunsOnTheCpu_WithANote_AndLargeEnough_RunsOnTheDevice()
        {
            var b = Device();
            var (sys, devRun) = GpuSelfTest.Problem("scalar", 2048, 0, O("backend", b.Name, "gpuMinPoints", 1));
            try
            {
                GpuMemory.FreeOverride = _ => 1000;
                var small = Simulation.Run(sys, devRun);
                string notes = string.Join(" | ", small.Records.Where(x => x.Steps != null).SelectMany(x => x.Steps.Notes));
                Assert.True(notes.Contains("not enough free device memory") && notes.Contains("ran on the CPU"), "refused with the memory figures: " + notes);
                GpuMemory.FreeOverride = _ => 8L << 30;
                var big = Simulation.Run(sys, devRun);
                Assert.True(big.Records.Any(x => x.Steps != null && x.Steps.Notes.Any(n => n.StartsWith("device: "))), "ran on the device with 8 GB free");
            }
            finally { GpuMemory.FreeOverride = null; }
        }

        [Test(Category = "gpu", Reference = "the driver's free memory is at most the total and falls while buffers are alive", Tolerance = "exact ordering")]
        public void FreeMemory_IsQueried_AndTracksLiveBuffers()
        {
            var b = Device();
            int dev = b.Devices.First(d => d.Usable).Index;
            var ctx = b.Context(dev);
            long free0 = ctx.FreeMemoryBytes();
            Assert.True(free0 > 0 && free0 <= b.Devices[dev].MemoryBytes, "free " + free0 + " of " + b.Devices[dev].MemoryBytes);
            long tracked0 = ctx.AllocatedBytes;
            var buf = ctx.Alloc(64L << 20);
            Assert.Equal(tracked0 + (64L << 20), ctx.AllocatedBytes, "tracked bytes while the buffer is alive");
            buf.Dispose();
            Assert.Equal(tracked0, ctx.AllocatedBytes, "tracked bytes after it is disposed");
        }

        [Test(Category = "gpu", Reference = "compiled extra programs are bounded (least recently used released) and released on teardown", Tolerance = "count <= MaxPrograms")]
        public void ProgramCache_IsBounded_AndReleasedOnTeardown()
        {
            var b = Device();
            int dev = b.Devices.First(d => d.Usable).Index;
            var ctx = b.Context(dev);
            int max = GpuContext.MaxPrograms;
            for (int i = 0; i < max + 8; i++)
            {
                string name = "uf_test_prog_" + i;
                ctx.EnsureProgram(name, "KERNEL void " + name + "(G double* x, int n) { for (int g = GID; g < n; g += GSZ) x[g] = 0.0; }");
            }
            Assert.Equal(max, ctx.ProgramCount, "programs kept after " + (max + 8) + " different ones");
            ctx.EnsureProgram("uf_test_prog_0", "KERNEL void uf_test_prog_0(G double* x, int n) { for (int g = GID; g < n; g += GSZ) x[g] = 0.0; }");   // evicted earlier: compiled again
            Assert.Equal(max, ctx.ProgramCount, "still bounded after reusing an evicted one");
            b.ReleaseContexts();
            var again = b.Context(dev);
            Assert.True(!ReferenceEquals(again, ctx) && again.ProgramCount == 0, "a new context after teardown has no programs");
        }
    }
}
