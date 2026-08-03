using UnityEngine;
using UnityEngine.Rendering;
using Unmath;

namespace CustomRenderPipeline
{
    public class DrawInstancedProceduralRenderPass<T> : DrawRenderPass<T>
    {
        private Material material;
        private int passIndex;
        private float depthBias, slopeDepthBias;
        private bool zClip;
        private int submeshIndex, count;
        private Mesh mesh;

        public override string ToString()
        {
            return $"{Name} {material} {passIndex}";
        }

        public void Initialize(Mesh mesh, int submeshIndex, Material material, int count, Int2 size, int viewCount, int passIndex = 0, float depthBias = 0.0f, float slopeDepthBias = 0.0f, bool zClip = true, bool isScreenPass = false)
        {
            this.mesh = mesh;
            this.submeshIndex = submeshIndex;
            this.material = material;
            this.passIndex = passIndex;
            this.depthBias = depthBias;
            this.slopeDepthBias = slopeDepthBias;
            this.zClip = zClip;
            this.count = count;
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
            foreach (var keyword in keywords)
                Command.EnableKeyword(material, new LocalKeyword(material.shader, keyword));

            if (depthBias != 0.0f || slopeDepthBias != 0.0f)
                Command.SetGlobalDepthBias(depthBias, slopeDepthBias);

            if (mesh == null)
                return;

            Command.SetGlobalFloat("ZClip", zClip ? 1 : 0);
            Command.DrawMeshInstancedProcedural(mesh, submeshIndex, material, passIndex, count, PropertyBlock);
            Command.SetGlobalFloat("ZClip", 1);

            if (depthBias != 0.0f || slopeDepthBias != 0.0f)
                Command.SetGlobalDepthBias(0.0f, 0.0f);

            foreach (var keyword in keywords)
                Command.DisableKeyword(material, new LocalKeyword(material.shader, keyword));
        }
    }
}