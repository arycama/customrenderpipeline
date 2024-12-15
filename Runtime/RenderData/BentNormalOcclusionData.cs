namespace Arycama.CustomRenderPipeline
{
    public class CameraTargetData : RTHandleData
    {
        // TODO: Rename to CameraTarget?
        public CameraTargetData(RTHandle handle) : base(handle, "_Input")
        {
        }
    }

    public class BentNormalOcclusionData : RTHandleData
    {
        public BentNormalOcclusionData(RTHandle handle) : base(handle, "_BentNormalOcclusion")
        {
        }
    }

    public class BloomData : RTHandleData
    {
        public BloomData(RTHandle handle) : base(handle, "_Bloom")
        {
        }
    }
    public class VelocityData : RTHandleData
    {
        public VelocityData(RTHandle handle) : base(handle, "Velocity")
        {
        }
    }

}