using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class CameraMotionVectors : RenderFeature
    {
        private readonly Material material;

        public CameraMotionVectors(RenderGraph renderGraph) : base(renderGraph)
        {
            material = new Material(Shader.Find("Hidden/Camera Motion Vectors")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Render(RTHandle motionVectors, RTHandle cameraDepth, int width, int height, Matrix4x4 nonJitteredVpMatrix, Matrix4x4 previousVpMatrix, Matrix4x4 invVpMatrix, Camera camera)
        {
            using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Camera Motion Vectors");
            pass.Initialize(material, camera: camera);
            pass.ReadTexture("_CameraDepth", cameraDepth);
            pass.WriteTexture(motionVectors);
            pass.WriteDepth(cameraDepth, RenderTargetFlags.ReadOnlyDepthStencil);

            var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
            {
                pass.SetVector(command, "_ScaledResolution", data.scaledResolution);
                pass.SetMatrix(command, "_WorldToNonJitteredClip", data.nonJitteredVpMatrix);
                pass.SetMatrix(command, "_WorldToPreviousClip", data.previousVpMatrix);
                pass.SetMatrix(command, "_ClipToWorld", data.invVpMatrix);
            });

            data.scaledResolution = new Vector4(width, height, 1.0f / width, 1.0f / height);
            data.nonJitteredVpMatrix = nonJitteredVpMatrix;
            data.previousVpMatrix = previousVpMatrix;
            data.invVpMatrix = invVpMatrix;
        }

        private class PassData
        {
            internal Vector4 scaledResolution;
            internal Matrix4x4 nonJitteredVpMatrix;
            internal Matrix4x4 previousVpMatrix;
            internal Matrix4x4 invVpMatrix;
        }
    }
}