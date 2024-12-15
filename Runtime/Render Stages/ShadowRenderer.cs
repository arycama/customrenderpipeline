using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class ShadowRenderer : RenderFeature<(ScriptableRenderContext context, CullingResults cullingResults, List<ShadowRequest> directionalShadowRequests, List<ShadowRequest> pointShadowRequests, Vector3 viewPosition)>
    {
        private readonly ShadowSettings settings;
        private readonly TerrainSystem terrainSystem;

        public ShadowRenderer(ShadowSettings settings, RenderGraph renderGraph, TerrainSystem terrainSystem) : base(renderGraph)
        {
            this.settings = settings;
            this.terrainSystem = terrainSystem;
        }

        public override void Render((ScriptableRenderContext context, CullingResults cullingResults, List<ShadowRequest> directionalShadowRequests, List<ShadowRequest> pointShadowRequests, Vector3 viewPosition) data)
        {
            // Render Shadows
            RTHandle directionalShadows;
            if (data.directionalShadowRequests.Count == 0)
            {
                directionalShadows = renderGraph.EmptyTextureArray;
            }
            else
            {
                directionalShadows = renderGraph.GetTexture(settings.DirectionalShadowResolution, settings.DirectionalShadowResolution, GraphicsFormat.D16_UNorm, data.directionalShadowRequests.Count, TextureDimension.Tex2DArray);

                for (var i = 0; i < data.directionalShadowRequests.Count; i++)
                {
                    var shadowRequest = data.directionalShadowRequests[i];
                    var splitData = shadowRequest.ShadowSplitData;

                    using (var pass = renderGraph.AddRenderPass<ShadowRenderPass>("Render Directional Light Shadows"))
                    {
                        pass.Initialize(data.context, data.cullingResults, shadowRequest.VisibleLightIndex, BatchCullingProjectionType.Orthographic, splitData, settings.ShadowBias, settings.ShadowSlopeBias, false);

                        // Doesn't actually do anything for this pass, except tells the rendergraph system that it gets written to
                        pass.WriteTexture(directionalShadows);

                        pass.SetRenderFunction((
                            viewPosition: data.viewPosition,
                            worldToView: shadowRequest.ViewMatrix,
                            worldToClip: shadowRequest.ProjectionMatrix * shadowRequest.ViewMatrix,
                            target: directionalShadows,
                            index: i
                        ),
                        (command, pass, data) =>
                        {
                            command.SetRenderTarget(data.target, 0, CubemapFace.Unknown, data.index);
                            command.ClearRenderTarget(true, false, Color.clear);

                            // TODO: Use different matrices for shadows?
                            pass.SetMatrix(command, "_WorldToView", data.worldToView);
                            pass.SetMatrix(command, "_WorldToClip", data.worldToClip);
                            pass.SetVector(command, "_ViewPosition", data.viewPosition);
                        });
                    }

                    terrainSystem.CullShadow(data.viewPosition, shadowRequest.CullingPlanes);
                    terrainSystem.RenderShadow(data.viewPosition, directionalShadows, shadowRequest.CullingPlanes, shadowRequest.ProjectionMatrix * shadowRequest.ViewMatrix, i, settings.ShadowBias, settings.ShadowSlopeBias);
                }
            }

            ListPool<ShadowRequest>.Release(data.directionalShadowRequests);

            // Process point shadows 
            RTHandle pointShadows;
            if (data.pointShadowRequests.Count == 0)
            {
                pointShadows = renderGraph.EmptyCubemapArray;
            }
            else
            {
                pointShadows = renderGraph.GetTexture(settings.PointShadowResolution, settings.PointShadowResolution, GraphicsFormat.D32_SFloat, data.pointShadowRequests.Count, TextureDimension.CubeArray);

                for (var i = 0; i < data.pointShadowRequests.Count; i++)
                {
                    var shadowRequest = data.pointShadowRequests[i];
                    if (!shadowRequest.IsValid)
                        continue;

                    using (var pass = renderGraph.AddRenderPass<ShadowRenderPass>("Render Point Light Shadows"))
                    {
                        pass.Initialize(data.context, data.cullingResults, shadowRequest.VisibleLightIndex, BatchCullingProjectionType.Perspective, shadowRequest.ShadowSplitData, settings.PointShadowBias, settings.PointShadowSlopeBias, true);

                        // Doesn't actually do anything for this pass, except tells the rendergraph system that it gets written to
                        pass.WriteTexture(pointShadows);

                        pass.SetRenderFunction((
                            viewPosition: data.viewPosition,
                            worldToView: shadowRequest.ViewMatrix,
                            worldToClip: GL.GetGPUProjectionMatrix(shadowRequest.ProjectionMatrix, true) * shadowRequest.ViewMatrix,
                            target: pointShadows,
                            index: i
                        ),
                        (command, pass, data) =>
                        {
                            command.SetRenderTarget(data.target, data.target, 0, CubemapFace.Unknown, data.index);
                            command.ClearRenderTarget(true, false, Color.clear);

                            pass.SetVector(command, "_ViewPosition", data.viewPosition);
                            pass.SetMatrix(command, "_WorldToView", data.worldToView);
                            pass.SetMatrix(command, "_WorldToClip", data.worldToClip);
                        });

                    }
                }
            }

            ListPool<ShadowRequest>.Release(data.pointShadowRequests);

            var result = new Result(directionalShadows, pointShadows, settings.DirectionalShadowResolution, 1.0f / settings.DirectionalShadowResolution, settings.PcfFilterRadius, settings.PcfFilterSigma);
            renderGraph.ResourceMap.SetRenderPassData(result, renderGraph.FrameIndex);
        }

        public readonly struct Result : IRenderPassData
        {
            private readonly RTHandle directionalShadows;
            private readonly RTHandle pointShadows;
            private readonly int shadowMapResolution;
            private readonly float rcpShadowMapResolution;
            private readonly float shadowFilterRadius;
            private readonly float shadowFilterSigma;

            public Result(RTHandle directionalShadows, RTHandle pointShadows, int shadowMapResolution, float rcpShadowMapResolution, float shadowFilterRadius, float shadowFilterSigma)
            {
                this.directionalShadows = directionalShadows ?? throw new ArgumentNullException(nameof(directionalShadows));
                this.pointShadows = pointShadows ?? throw new ArgumentNullException(nameof(pointShadows));
                this.shadowMapResolution = shadowMapResolution;
                this.rcpShadowMapResolution = rcpShadowMapResolution;
                this.shadowFilterRadius = shadowFilterRadius;
                this.shadowFilterSigma = shadowFilterSigma;
            }

            public readonly void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("_DirectionalShadows", directionalShadows);
                pass.ReadTexture("_PointShadows", pointShadows);
            }

            public readonly void SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetFloat(command, "ShadowMapResolution", shadowMapResolution);
                pass.SetFloat(command, "RcpShadowMapResolution", rcpShadowMapResolution);
                pass.SetFloat(command, "ShadowFilterRadius", shadowFilterRadius);
                pass.SetFloat(command, "ShadowFilterSigma", shadowFilterSigma);
            }
        }
    }
}