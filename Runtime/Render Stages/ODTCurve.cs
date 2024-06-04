namespace Arycama.CustomRenderPipeline
{
    public enum ODTCurve : uint
    {
        // reference curves, no parameterization
        ODT_LDR_Ref,
        ODT_1000Nit_Ref,
        ODT_2000Nit_Ref,
        ODT_4000Nit_Ref,

        // Adjustable curves, parameterized for range, level, etc
        ODT_LDR_Adj,
        ODT_1000Nit_Adj,
        ODT_2000Nit_Adj,
        ODT_4000Nit_Adj,

        ODT_Invalid = 0xffffffff
    };
}

