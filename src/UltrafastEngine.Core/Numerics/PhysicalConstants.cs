namespace UltrafastEngine.Numerics
{
    /// <summary>
    /// SI physical constants. Every quantity in UltrafastEngine.Core is SI
    /// (radians/second, seconds, metres, watts, joules) -- unit conversion to
    /// nm/fs/mm only happens at the IO/UI boundary, never inside the physics engine.
    /// </summary>
    public static class PhysicalConstants
    {
        public const double SpeedOfLightMPerS = 2.99792458e8;
        /// <summary>Self-focusing critical power of a Gaussian beam, P_cr = factor lambda^2 / (8 pi n0 n2) (Marburger 1975: 3.77; Fibich and Gaeta 2000 give 3.72
        /// for the collapse threshold). The reported P / P_cr uses 3.77.</summary>
        public const double CriticalPowerFactor = 3.77;
    }
}
