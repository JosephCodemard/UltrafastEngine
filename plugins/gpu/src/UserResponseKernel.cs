// UserResponseKernel.cs -- a device kernel generated per problem for the extra time-domain responses of an element (user
// expressions, two-photon absorption, extra Kerr terms; Physics.DeviceResponses gives their C source).
//
// The kernel evaluates the complex index change U = dn_re + i dn_im at every sample of every row (1-D: one row of n samples;
// spatial: one row per transverse point) from the field in time (natural FFT order), binding the response variables
// (ExpressionTimeResponse.Vars) as the CPU does (EnvelopeProblem.Nonlinear, SpatialEnvelopeProblem.Nonlinear). The Kerr kernels
// (uf_env_kerr, uf_sp_kerr) then add U to the Kerr index change. Programs are compiled once per device context and source.
//
// Classes: UserResponseKernel
//   Name(code) -> kernel name        Source(code) -> program body (compiled with Kernels.Header)
//   Prepare(stream, code) -> name    compiles on first use
using UltrafastEngine.Physics;

namespace UltrafastEngine.Plugins.Gpu
{
    public static class UserResponseKernel
    {
        public static string Name(DeviceResponseCode code) => "uf_ur_" + code.Key;

        /// <summary>
        /// Program body. Arguments: T (field in time, component y at block), n, M (rows), block (= M n), scalar,
        /// A (the intensity divisor: 1-D Aeff, spatial 1), tn (time of each natural index, s), t_arrival, z, L, omega0, lambda0,
        /// aeff (1-D effective area), px / py / pa (per-row x, y, area; read when perRow), spatial (0 / 1), F (running fluence,
        /// read when useF), U (out).
        /// </summary>
        public static string Source(DeviceResponseCode code) => @"
KERNEL void " + Name(code) + @"(G const cplx* T, int n, int M, int block, int scalar, double A, G const double* tn,
    double v_t_arrival, double v_z, double v_L, double v_omega0, double v_lambda0, double aeff,
    G const double* px, G const double* py, G const double* pa, int perRow, double v_spatial, G const double* F, int useF, G cplx* U)
{
    for (int g = GID; g < block; g += GSZ) {
        int r = g / n, i = g - r * n;
        double v_t = tn[i];
        double v_t_lab = v_t + v_t_arrival;
        cplx ax = T[g];
        cplx ay = scalar ? C2(0.0, 0.0) : T[block + g];
        double v_Ix = cabs2(ax) / A, v_Iy = cabs2(ay) / A;
        double v_I = v_Ix + v_Iy, v_I_inst = v_I, v_E = 0.0;
        double v_Aeff = perRow ? pa[r] : aeff;
        double v_x = perRow ? px[r] : 0.0, v_y = perRow ? py[r] : 0.0;
        double v_r = sqrt(v_x * v_x + v_y * v_y), v_theta = atan2(v_y, v_x);
        double v_F = useF ? F[g] : 0.0;
        (void)v_t_lab; (void)v_I_inst; (void)v_E; (void)v_Aeff; (void)v_r; (void)v_theta; (void)v_F;
        (void)v_z; (void)v_L; (void)v_omega0; (void)v_lambda0; (void)v_spatial;
        U[g] = C2(" + code.Re + @", " + code.Im + @");
    }
}
";

        /// <summary>Compile the program for <paramref name="code"/> on the stream's device (once) and return the kernel name.</summary>
        public static string Prepare(GpuStream s, DeviceResponseCode code)
        {
            string name = Name(code);
            s.Context.EnsureProgram(name, Source(code));
            return name;
        }
    }
}
