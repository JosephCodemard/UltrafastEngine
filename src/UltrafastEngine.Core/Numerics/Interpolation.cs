using System;

namespace UltrafastEngine.Numerics
{
    public interface IInterpolator
    {
        double Evaluate(double x);
        double[] EvaluateAll(double[] xQuery);
    }

    /// <summary>
    /// Piecewise-linear interpolation with edge clamping (queries outside the
    /// known range return the nearest edge value rather than extrapolating).
    /// Used for transmission/loss curves, where a smooth second derivative
    /// doesn't matter and overshoot beyond [0,1] would be physically wrong.
    /// </summary>
    public sealed class LinearInterpolator : IInterpolator
    {
        private readonly double[] _x;
        private readonly double[] _y;

        public LinearInterpolator(double[] xKnown, double[] yKnown)
        {
            if (xKnown.Length != yKnown.Length || xKnown.Length < 2)
                throw new ArgumentException("LinearInterpolator needs at least 2 matching (x,y) points.");
            _x = xKnown;
            _y = yKnown;
        }

        public double Evaluate(double x)
        {
            int n = _x.Length;
            if (x <= _x[0]) return _y[0];
            if (x >= _x[n - 1]) return _y[n - 1];

            int i = LowerBound(_x, x);
            double t = (x - _x[i]) / (_x[i + 1] - _x[i]);
            return _y[i] + t * (_y[i + 1] - _y[i]);
        }

        public double[] EvaluateAll(double[] xQuery)
        {
            var result = new double[xQuery.Length];
            for (int i = 0; i < xQuery.Length; i++) result[i] = Evaluate(xQuery[i]);
            return result;
        }

        internal static int LowerBound(double[] sortedX, double x)
        {
            int lo = 0, hi = sortedX.Length - 2;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (sortedX[mid] <= x) lo = mid; else hi = mid - 1;
            }
            return lo;
        }
    }

    /// <summary>
    /// Natural cubic spline (second derivative = 0 at both ends), standard
    /// tridiagonal (Thomas algorithm) construction. Used for OPD/phase curves,
    /// where a smooth second and third derivative genuinely matters because
    /// we differentiate this data twice and three times (GDD, TOD).
    ///
    /// Needs at least 4 points to be meaningful -- LinearOpticalSegment falls
    /// back to LinearInterpolator below that, which will give much noisier
    /// GDD/TOD but at least won't throw.
    /// </summary>
    public sealed class NaturalCubicSpline : IInterpolator
    {
        private readonly double[] _x;
        private readonly double[] _y;
        private readonly double[] _a, _b, _c, _d; // per-segment cubic coefficients

        public NaturalCubicSpline(double[] xKnown, double[] yKnown)
        {
            if (xKnown.Length != yKnown.Length || xKnown.Length < 3)
                throw new ArgumentException("NaturalCubicSpline needs at least 3 matching (x,y) points.");

            int n = xKnown.Length;
            _x = xKnown;
            _y = yKnown;

            var h = new double[n - 1];
            for (int i = 0; i < n - 1; i++) h[i] = xKnown[i + 1] - xKnown[i];

            var alpha = new double[n];
            for (int i = 1; i < n - 1; i++)
                alpha[i] = 3.0 / h[i] * (yKnown[i + 1] - yKnown[i]) - 3.0 / h[i - 1] * (yKnown[i] - yKnown[i - 1]);

            var l = new double[n];
            var mu = new double[n];
            var z = new double[n];
            l[0] = 1.0; mu[0] = 0.0; z[0] = 0.0;

            for (int i = 1; i < n - 1; i++)
            {
                l[i] = 2.0 * (xKnown[i + 1] - xKnown[i - 1]) - h[i - 1] * mu[i - 1];
                mu[i] = h[i] / l[i];
                z[i] = (alpha[i] - h[i - 1] * z[i - 1]) / l[i];
            }
            l[n - 1] = 1.0; z[n - 1] = 0.0;

            _c = new double[n];
            _b = new double[n];
            _d = new double[n];
            _a = new double[n];
            for (int i = 0; i < n; i++) _a[i] = yKnown[i];

            for (int j = n - 2; j >= 0; j--)
            {
                _c[j] = z[j] - mu[j] * _c[j + 1];
                _b[j] = (yKnown[j + 1] - yKnown[j]) / h[j] - h[j] * (_c[j + 1] + 2.0 * _c[j]) / 3.0;
                _d[j] = (_c[j + 1] - _c[j]) / (3.0 * h[j]);
            }
        }

        public double Evaluate(double x)
        {
            int n = _x.Length;
            double xc = x;
            if (xc <= _x[0]) xc = _x[0];
            if (xc >= _x[n - 1]) xc = _x[n - 1];

            int i = LinearInterpolator.LowerBound(_x, xc);
            double dx = xc - _x[i];
            return _a[i] + _b[i] * dx + _c[i] * dx * dx + _d[i] * dx * dx * dx;
        }

        public double[] EvaluateAll(double[] xQuery)
        {
            var result = new double[xQuery.Length];
            for (int i = 0; i < xQuery.Length; i++) result[i] = Evaluate(xQuery[i]);
            return result;
        }

        public double MinX => _x[0];
        public double MaxX => _x[_x.Length - 1];

        /// <summary>First derivative of the spline (clamped to the end segments' cubic
        /// outside the knot range, i.e. continued, not flattened).</summary>
        public double Derivative(double x)
        {
            int i = SegmentIndex(x);
            double dx = x - _x[i];
            return _b[i] + 2.0 * _c[i] * dx + 3.0 * _d[i] * dx * dx;
        }

        /// <summary>Second derivative of the spline (piecewise linear, continuous).</summary>
        public double SecondDerivative(double x)
        {
            int i = SegmentIndex(x);
            double dx = x - _x[i];
            return 2.0 * _c[i] + 6.0 * _d[i] * dx;
        }

        /// <summary>Evaluate WITHOUT clamping: inside the knot range this is the spline;
        /// outside it the curve is continued LINEARLY with the end slope (constant first
        /// derivative). Used for group-delay curves, where "constant GDD beyond the last
        /// sampled wavelength" is a far more physical continuation than the flat (zero-GDD)
        /// clamp Evaluate() applies -- and in any case PropagationEngine reports how much
        /// spectral energy ever falls outside the sampled range.</summary>
        public double EvaluateLinearExtrapolate(double x)
        {
            int n = _x.Length;
            if (x < _x[0]) return _y[0] + Derivative(_x[0]) * (x - _x[0]);
            if (x > _x[n - 1]) return _y[n - 1] + Derivative(_x[n - 1]) * (x - _x[n - 1]);
            return Evaluate(x);
        }

        private int SegmentIndex(double x)
        {
            int n = _x.Length;
            double xc = x < _x[0] ? _x[0] : (x > _x[n - 1] ? _x[n - 1] : x);
            return LinearInterpolator.LowerBound(_x, xc);
        }
    }
}
