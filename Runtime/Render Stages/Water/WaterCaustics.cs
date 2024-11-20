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

            var count = 128;
            var isQuad = false;
            var alternateIndices = false;
            var indicesPerQuad = isQuad ? 4 : 6;
            var bufferSize = count * count * indicesPerQuad;
            var triangles = new uint[bufferSize];
            
            for (int y = 0, i = 0, vi = 0; y < count; y++, vi++)
            {
                var rowStart = y * (count + 1);

                for (int x = 0; x < count; x++, i += indicesPerQuad, vi++)
                {
                    var columnStart = rowStart + x;

                    var flip = alternateIndices ? (x & 1) == (y & 1) : true;

                    if (isQuad)
                    {
                        if (flip)
                        {
                            triangles[i + 0] = (uint)(columnStart);
                            triangles[i + 1] = (uint)(columnStart + count + 1);
                            triangles[i + 2] = (uint)(columnStart + count + 2);
                            triangles[i + 3] = (uint)(columnStart + 1);
                        }
                        else
                        {
                            triangles[i + 1] = (uint)(columnStart + count + 1);
                            triangles[i + 2] = (uint)(columnStart + count + 2);
                            triangles[i + 3] = (uint)(columnStart + 1);
                            triangles[i + 0] = (uint)(columnStart);
                        }
                    }
                    else
                    {
                        if (flip)
                        {
                            triangles[i + 0] = (uint)columnStart;
                            triangles[i + 1] = (uint)(columnStart + count + 1);
                            triangles[i + 2] = (uint)(columnStart + count + 2);
                            triangles[i + 3] = (uint)(columnStart + count + 2);
                            triangles[i + 4] = (uint)(columnStart + 1);
                            triangles[i + 5] = (uint)columnStart;
                        }
                        else
                        {
                            triangles[i + 0] = (uint)columnStart;
                            triangles[i + 1] = (uint)(columnStart + count + 1);
                            triangles[i + 2] = (uint)(columnStart + 1);
                            triangles[i + 3] = (uint)(columnStart + 1);
                            triangles[i + 4] = (uint)(columnStart + count + 1);
                            triangles[i + 5] = (uint)(columnStart + count + 2);
                        }
                    }
                }
            }

            indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, bufferSize, sizeof(uint));
            indexBuffer.SetData(triangles);
        }

        protected override void Cleanup(bool disposing)
        {
            indexBuffer.Dispose();
        }

        public void Render()
        {
            var Profile = settings.Profile;
            var patchSizes = new Vector4(Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 0f), Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 1f), Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 2f), Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 3f));
            var patchSize = patchSizes[settings.CasuticsCascade];

            var temp0 = renderGraph.GetTexture(129, 129, GraphicsFormat.R32G32B32A32_SFloat);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ocean Caustics Blit"))
            {
                pass.Initialize(material, 2);
                pass.WriteTexture(temp0, RenderBufferLoadAction.DontCare);

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat(command, "_CausticsDepth", settings.CausticsDepth);
                    pass.SetFloat(command, "_CausticsCascade", settings.CasuticsCascade);
                    pass.SetFloat(command, "_PatchSize", patchSize);
                    pass.SetVector(command, "_RefractiveIndex", Vector3.one * (1.0f / 1.34f));
                });
            }

            var tempResult = renderGraph.GetTexture(settings.CasuticsResolution * 2, settings.CasuticsResolution * 2, GraphicsFormat.B10G11R11_UFloatPack32);
            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Ocean Caustics Render"))
            {
                pass.AddRenderPassData<DirectionalLightInfo>();
                pass.AddRenderPassData<OceanFftResult>();
                pass.WriteTexture(tempResult);
                pass.ReadTexture("_Input", temp0);
                
                pass.SetRenderFunction((command, pass) =>
                {
                    var viewMatrix = Matrix4x4.LookAt(Vector3.zero, Vector3.down, Vector3.forward).inverse;
                    var projectionMatrix = Matrix4x4.Ortho(-patchSize, patchSize, -patchSize, patchSize, 0, settings.CausticsDepth * 2);
                    command.SetViewProjectionMatrices(viewMatrix, projectionMatrix);

                    command.SetRenderTarget(tempResult);
                    command.ClearRenderTarget(false, true, Color.clear);

                    pass.SetFloat(command, "_CausticsDepth", settings.CausticsDepth);
                    pass.SetFloat(command, "_CausticsCascade", settings.CasuticsCascade);
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