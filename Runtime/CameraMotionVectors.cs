using UnityEngine;
using UnityEngine.Rendering;

public class CameraMotionVectors
{
    private readonly Material motionVectorsMaterial;

    public CameraMotionVectors()
    {
        motionVectorsMaterial = new Material(Shader.Find("Hidden/Camera Motion Vectors")) { hideFlags = HideFlags.HideAndDontSave };
    }

    public void Render(CommandBuffer command, RenderTargetIdentifier motionVectors, RenderTargetIdentifier cameraDepth)
    {
        using var profilerScope = command.BeginScopedSample("Camera Motion Vectors");
        command.SetRenderTarget(new RenderTargetBinding(motionVectors, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, cameraDepth, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store) { flags = RenderTargetFlags.ReadOnlyDepthStencil });
        command.SetGlobalTexture("_CameraDepth", cameraDepth);
        command.DrawProcedural(Matrix4x4.identity, motionVectorsMaterial, 0, MeshTopology.Triangles, 3);
    }
}