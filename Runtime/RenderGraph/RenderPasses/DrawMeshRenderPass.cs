using UnityEngine;
using UnityEngine.Rendering;
using Unmath;

namespace CustomRenderPipeline
{
    public class DrawMeshRenderPass<T> : DrawRenderPass<T>
    {
        private Material material;
        private int passIndex;
        private float depthBias, slopeDepthBias;
        private bool zClip;
        private int submeshIndex;
        private Mesh mesh;
        private Matrix4x4 matrix;
        private SinglePassStereoMode stereoMode;

        public override string ToString()
        {
            return $"{Name} {material} {passIndex}";
        }

        public void Initialize(Mesh mesh, int submeshIndex, Material material, Matrix4x4 matrix, Int2 size, int viewCount, int passIndex = 0, float depthBias = 0.0f, float slopeDepthBias = 0.0f, bool zClip = true, bool isScreenPass = false, SinglePassStereoMode stereoMode = SinglePassStereoMode.None)
        {
            this.mesh = mesh;
            this.submeshIndex = submeshIndex;
            this.material = material;
            this.passIndex = passIndex;
            this.depthBias = depthBias;
            this.slopeDepthBias = slopeDepthBias;
            this.zClip = zClip;
            this.matrix = matrix;
            this.stereoMode = stereoMode;
            Size = size;
            ViewCount = viewCount;
            IsScreenPass = isScreenPass;
        }

        public override void Reset()
        {
            base.Reset();
            material = null;
            passIndex = 0;
            zClip = true;
        }

        protected override void Execute()
        {
            if (mesh == null)
                return;

            foreach (var keyword in keywords)
                Command.EnableKeyword(material, new LocalKeyword(material.shader, keyword));

            if (depthBias != 0.0f || slopeDepthBias != 0.0f)
                Command.SetGlobalDepthBias(depthBias, slopeDepthBias);

            if (stereoMode == SinglePassStereoMode.Instancing)
            {
                Command.SetInstanceMultiplier(2u);
                Command.EnableShaderKeyword("STEREO_INSTANCING_ON");
                Command.SetSinglePassStereo(SinglePassStereoMode.Instancing);
            }
            else if (stereoMode == SinglePassStereoMode.Multiview)
            {
                Command.EnableShaderKeyword("STEREO_MULTIVIEW_ON");
                Command.SetSinglePassStereo(SinglePassStereoMode.Multiview);
            }

            Command.SetGlobalFloat("ZClip", zClip ? 1 : 0);
            Command.DrawMesh(mesh, matrix, material, submeshIndex, passIndex, PropertyBlock);
            Command.SetGlobalFloat("ZClip", 1);

            if (stereoMode == SinglePassStereoMode.Instancing)
            {
                Command.SetSinglePassStereo(SinglePassStereoMode.None);
                Command.DisableShaderKeyword("STEREO_INSTANCING_ON");
                Command.SetInstanceMultiplier(1u);
            }
            else if (stereoMode == SinglePassStereoMode.Multiview)
            {
                Command.SetSinglePassStereo(SinglePassStereoMode.None);
                Command.DisableShaderKeyword("STEREO_MULTIVIEW_ON");
            }

            if (depthBias != 0.0f || slopeDepthBias != 0.0f)
                Command.SetGlobalDepthBias(0.0f, 0.0f);

            foreach (var keyword in keywords)
                Command.DisableKeyword(material, new LocalKeyword(material.shader, keyword));
        }
    }
}