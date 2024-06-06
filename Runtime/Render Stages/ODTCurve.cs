namespace Arycama.CustomRenderPipeline
{
    public enum ODTCurve : uint
    {
        // reference curves, no parameterization
        RefLdr,
        Ref1000Nit,
        Ref2000Nit,
        Ref4000Nit,

        // Adjustable curves, parameterized for range, level, etc
        AdjLdr,
        Adj1000Nit,
        Adj2000Nit,
        Adj4000Nit
    };
}

