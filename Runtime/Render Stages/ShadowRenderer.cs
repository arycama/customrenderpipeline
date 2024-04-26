using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class ShadowRenderer : RenderFeature
    {
        private readonly ShadowSettings settings;

        public ShadowRenderer(ShadowSettings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
        }

        public void Render(ScriptableRenderContext context, CullingResults cullingResults, Camera camera, List<ShadowRequest> directionalShadowRequests, List<ShadowRequest> pointShadowRequests)
        {
            // Render Shadows
            RTHandle directionalShadows;
            if (directionalShadowRequests.Count == 0)
            {
                directionalShadows = renderGraph.EmptyTextureArray;
            }
            else
            {
                directionalShadows = renderGraph.GetTexture(settings.DirectionalShadowResolution, settings.DirectionalShadowResolution, GraphicsFormat.D16_UNorm, directionalShadowRequests.Count, TextureDimension.Tex2DArray);

                for (var i = 0; i < directionalShadowRequests.Count; i++)
                {
                    using (var pass = renderGraph.AddRenderPass<ShadowRenderPass>("Render Directional Light Shadows"))
                    {
                        var shadowRequest = directionalShadowRequests[i];
                        pass.Initialize(context, cullingResults, shadowRequest.VisibleLightIndex, BatchCullingProjectionType.Orthographic, shadowRequest.ShadowSplitData, settings.ShadowBias, settings.ShadowSlopeBias, false);

                        // Doesn't actually do anything for this pass, except tells the rendergraph system that it gets written to
                        pass.WriteTexture(directionalShadows);

                        int index = i;
                        var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                        {
                            command.SetRenderTarget(directionalShadows, 0, CubemapFace.Unknown, index);
                            command.ClearRenderTarget(true, false, Color.clear);

                            pass.SetMatrix(command, "_WorldToView", data.worldToView);
                            pass.SetMatrix(command, "_WorldToClip", data.worldToClip);
                            pass.SetVector(command, "_ViewPosition", data.viewPosition);
                        });

                        data.viewPosition = camera.transform.position;
                        data.worldToView = shadowRequest.ViewMatrix;
                        data.worldToClip = shadowRequest.ProjectionMatrix * shadowRequest.ViewMatrix;
                    }
                }
            }

            ListPool<ShadowRequest>.Release(directionalShadowRequests);

            // Process point shadows 
            RTHandle pointShadows;
            if (pointShadowRequests.Count == 0)
            {
                pointShadows = renderGraph.EmptyCubemapArray;
            }
            else
            {
                pointShadows = renderGraph.GetTexture(settings.PointShadowResolution, settings.PointShadowResolution, GraphicsFormat.D32_SFloat, pointShadowRequests.Count, TextureDimension.CubeArray);

                for (var i = 0; i < pointShadowRequests.Count; i++)
                {
                    var shadowRequest = pointShadowRequests[i];
                    if (!shadowRequest.IsValid)
                        continue;

                    using (var pass = renderGraph.AddRenderPass<ShadowRenderPass>("Render Point Light Shadows"))
                    {
                        pass.Initialize(context, cullingResults, shadowRequest.VisibleLightIndex, BatchCullingProjectionType.Perspective, shadowRequest.ShadowSplitData, settings.PointShadowBias, settings.PointShadowSlopeBias, true);

                        // Doesn't actually do anything for this pass, except tells the rendergraph system that it gets written to
                        pass.WriteTexture(pointShadows);
                        int index = i;

                        var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                        {
                            command.SetRenderTarget(pointShadows, 0, CubemapFace.Unknown, index);
                            command.ClearRenderTarget(true, false, Color.clear);

                            pass.SetVector(command, "_ViewPosition", data.viewPosition);
                            pass.SetMatrix(command, "_WorldToView", data.worldToView);
                            pass.SetMatrix(command, "_WorldToClip", data.worldToClip);
                        });

                        data.viewPosition = camera.transform.position;
                        data.worldToView = shadowRequest.ViewMatrix;
                        data.worldToClip = GL.GetGPUProjectionMatrix(shadowRequest.ProjectionMatrix, true) * shadowRequest.ViewMatrix;
                    }
                }
            }

            ListPool<ShadowRequest>.Release(pointShadowRequests);

            var result = new Result(directionalShadows, pointShadows);
            renderGraph.ResourceMap.SetRenderPassData(result);
        }

        private class PassData
        {
            internal Vector3 viewPosition;
            internal Matrix4x4 worldToView;
            internal Matrix4x4 worldToClip;
        }

        public struct Result : IRenderPassData
        {
            private readonly RTHandle directionalShadows;
            private readonly RTHandle pointShadows;

            public Result(RTHandle directionalShadows, RTHandle pointShadows)
            {
                this.directionalShadows = directionalShadows ?? throw new ArgumentNullException(nameof(directionalShadows));
                this.pointShadows = pointShadows ?? throw new ArgumentNullException(nameof(pointShadows));
            }

            public void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("_DirectionalShadows", directionalShadows);
                pass.ReadTexture("_PointShadows", pointShadows);
            }

            public void SetProperties(RenderPass pass, CommandBuffer command)
            {
            }
        }
    }
}