using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public class WaterCaustics : RenderFeature
    {
        private WaterSystem.Settings settings;
        private Material material;
        private Mesh causticsMesh;
        private GraphicsBuffer indexBuffer;

        public WaterCaustics(RenderGraph renderGraph, WaterSystem.Settings settings) : base(renderGraph)
        {
            this.settings = settings;
            material = new Material(Shader.Find("Hidden/Water Caustics")) { hideFlags = HideFlags.HideAndDontSave };

            var divisions = 128;
            var triangles = new ushort[divisions * divisions * 6];
            for (int ti = 0, vi = 0, y = 0; y < divisions; y++, vi++)
            {
                for (int x = 0; x < divisions; x++, ti += 6, vi++)
                {
                    triangles[ti + 0] = (ushort)vi;
                    triangles[ti + 1] = (ushort)(vi + divisions + 1);
                    triangles[ti + 2] = (ushort)(vi + 1);
                    triangles[ti + 3] = (ushort)(vi + 1);
                    triangles[ti + 4] = (ushort)(vi + divisions + 1);
                    triangles[ti + 5] = (ushort)(vi + divisions + 2);
                }
            }

            indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, divisions * divisions * 6, sizeof(ushort));
            indexBuffer.SetData(triangles);

            //indexBuffer = GraphicsUtilities.GenerateGridIndexBuffer(divisions, false);

        }

        public void Render()
        {
            var Profile = settings.Profile;
            var patchSizes = new Vector4(Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 0f), Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 1f), Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 2f), Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 3f));

            var patchSize = patchSizes[settings.CasuticsCascade];
            var tempResult = renderGraph.GetTexture(settings.CasuticsResolution * 2, settings.CasuticsResolution * 2, GraphicsFormat.R16G16B16A16_SFloat);

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Ocean Caustics Render"))
            {
                pass.AddRenderPassData<DirectionalLightInfo>();
                pass.AddRenderPassData<OceanFftResult>();
                pass.WriteTexture(tempResult);

                pass.SetRenderFunction((command, pass) =>
                {
                    var viewMatrix = Matrix4x4.LookAt(Vector3.zero, Vector3.down, Vector3.forward).inverse;
                    var projectionMatrix = Matrix4x4.Ortho(-patchSize, patchSize, -patchSize, patchSize, 0, settings.CausticsDepth * 2);
                    command.SetViewProjectionMatrices(viewMatrix, projectionMatrix);

                    command.SetRenderTarget(tempResult);
                    command.ClearRenderTarget(false, true, Color.clear);

                    pass.SetFloat(command, "_CausticsDepth", settings.CausticsDepth);
                    pass.SetFloat(command, "_CausticsCascade", settings.CasuticsCascade);
                    pass.SetFloat(command, "_CausticsSpacing", patchSize / 128f);
                    pass.SetFloat(command, "_PatchSize", patchSize);
                    pass.SetVector(command, "_RefractiveIndex", Vector3.one * (1.0f / 1.34f));

                    command.DrawProcedural(indexBuffer, Matrix4x4.identity, material, 0, MeshTopology.Triangles, indexBuffer.count);
                });
            }

            var result = renderGraph.GetTexture(settings.CasuticsResolution, settings.CasuticsResolution, GraphicsFormat.B10G11R11_UFloatPack32, hasMips: true, autoGenerateMips: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ocean Caustics Blit"))
            {
                pass.Initialize(material, 1);
                pass.ReadTexture("_MainTex", tempResult);
                pass.WriteTexture(result, RenderBufferLoadAction.DontCare);
            }

            renderGraph.ResourceMap.SetRenderPassData(new CausticsResult(result, settings.CasuticsCascade, settings.CausticsDepth), renderGraph.FrameIndex);
        }
    }
}