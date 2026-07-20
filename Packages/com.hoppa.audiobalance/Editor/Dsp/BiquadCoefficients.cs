namespace Hoppa.AudioBalance.Editor
{
    /// <summary>Normalised direct-form-I biquad coefficients (a0 folded into the rest).</summary>
    public readonly struct BiquadCoefficients
    {
        public readonly double B0;
        public readonly double B1;
        public readonly double B2;
        public readonly double A1;
        public readonly double A2;

        public BiquadCoefficients(double b0, double b1, double b2, double a1, double a2)
        {
            B0 = b0;
            B1 = b1;
            B2 = b2;
            A1 = a1;
            A2 = a2;
        }
    }
}
