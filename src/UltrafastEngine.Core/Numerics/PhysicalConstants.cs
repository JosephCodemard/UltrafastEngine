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
    }
}
